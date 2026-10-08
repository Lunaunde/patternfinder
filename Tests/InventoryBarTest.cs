using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Checks collection, insertion, deletion and stable reorder rules used by the material drawer.</summary>
public partial class InventoryBarTest : Node
{
    private int _checks;

    public override void _Ready()
    {
        try
        {
            Texture2D texture = MakeTexture(Colors.Olive);
            Texture2D replacement = MakeTexture(Colors.Teal);
            CheckInventoryRules(texture, replacement);
            CheckInsertionAndReordering(texture, replacement);
            CheckScaleStorage(texture);
            GD.Print($"Material inventory: {_checks} checks passed.");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private void CheckInventoryRules(Texture2D texture, Texture2D replacement)
    {
        var inventory = new MaterialInventory();
        int changes = 0;
        inventory.Changed += () => changes++;
        Check(MaterialInventory.Capacity == 14 && MaterialInventory.PageSize == 7,
            "The library has fourteen slots and retains its seven-card display span.");
        Check(inventory.Count == 0 && inventory.Entries.Count == 0,
            "A fresh inventory starts empty.");
        Check(inventory.TryAdd("stable-id", "Original", texture), "The first material is accepted.");
        Check(!inventory.TryAdd("stable-id", "Renamed", replacement),
            "The same stable ID cannot be collected again even when its name and art change.");
        Check(inventory.Count == 1 && changes == 1 && inventory.Entries[0].DisplayName == "Original"
            && inventory.Entries[0].Texture == texture,
            "Rejecting a duplicate preserves the first card and does not announce a data change.");
        Check(inventory.TryAdd("different-id", "Original", texture),
            "Different IDs remain distinct even when the displayed name and art match.");
        Check(!inventory.TryAdd("", "Empty ID", texture) && !inventory.TryAdd("missing-art", "Missing", null),
            "Invalid materials do not consume a slot.");
        for (int index = 3; index <= 14; index++)
            Check(inventory.TryAdd($"rule-{index}", $"Material {index}", texture),
                $"Valid material {index} is accepted up to capacity.");
        string[] idsBefore = inventory.Entries.Select(entry => entry.MaterialId).ToArray();
        Check(!inventory.TryAdd("fifteenth", "Overflow", replacement) && !inventory.Contains("fifteenth"),
            "The fifteenth material is rejected without entering the ID set.");
        Check(inventory.Count == 14 && inventory.Entries.Count == 14 && changes == 14
            && inventory.Entries.Select(entry => entry.MaterialId).SequenceEqual(idsBefore)
            && idsBefore.Distinct(StringComparer.Ordinal).Count() == 14,
            "Capacity rejection preserves card order, unique IDs, count, and change notifications.");
        Check(inventory.TryMove("stable-id", inventory.Count - 1) && inventory.Count == 14
            && inventory.Contains("stable-id") && changes == 15,
            "Reordering a full collection keeps the moved card counted toward the fourteen-card limit.");
        Check(!inventory.TryInsert(0, "overflow-front", "Overflow", replacement) && changes == 15
            && inventory.Count == 14 && !inventory.Contains("overflow-front"),
            "Front insertion also rejects overflow without treating a moved card as a free slot.");
        MaterialEntry[] beforeRemoval = inventory.Entries.ToArray();
        Check(inventory.TryRemove("rule-7") && inventory.Count == 13 && !inventory.Contains("rule-7") && changes == 16,
            "Deleting a material from a full collection releases its slot and its stable ID.");
        Check(inventory.TryInsert(0, "rule-7", "Recollected", replacement) && inventory.Count == 14
            && inventory.Entries[0].DisplayName == "Recollected" && inventory.Entries[0].Texture == replacement
            && inventory.Contains("rule-7") && changes == 17,
            "A deleted material can be collected again at the left edge of the now-full collection.");
        Check(inventory.Entries.Skip(1).SequenceEqual(beforeRemoval.Where(entry => entry.MaterialId != "rule-7"))
            && inventory.Entries.Select(entry => entry.MaterialId).Distinct(StringComparer.Ordinal).Count() == 14,
            "Deletion and recollection preserve all other card objects and their relative order.");
    }

    private void CheckInsertionAndReordering(Texture2D texture, Texture2D replacement)
    {
        var inventory = new MaterialInventory();
        var eventOrders = new List<string[]>();
        bool consistentEvents = true;
        inventory.Changed += () =>
        {
            MaterialEntry[] entries = inventory.Entries.ToArray();
            consistentEvents &= entries.Length == inventory.Count
                && entries.All(entry => inventory.Contains(entry.MaterialId))
                && entries.Select(entry => entry.MaterialId).Distinct(StringComparer.Ordinal).Count() == entries.Length;
            eventOrders.Add(entries.Select(entry => entry.MaterialId).ToArray());
        };
        Check(!inventory.TryMove("missing", 0) && !inventory.TryRemove("missing") && eventOrders.Count == 0,
            "Moving or deleting from an empty collection has no side effects.");
        Check(inventory.TryInsert(0, "A", "Card A", texture)
            && inventory.TryAdd("B", "Card B", texture) && inventory.TryAdd("C", "Card C", texture),
            "Insertion accepts the first card and the existing add interface appends fixture cards.");
        MaterialEntry originalA = inventory.Entries[0];
        Check(inventory.TryInsert(0, "H", "Head card", replacement)
            && HasOrder(inventory, "H", "A", "B", "C") && inventory.Count == 4,
            "A new card inserted at the left shifts old cards right without replacing them.");
        Check(inventory.TryInsert(2, "M", "Middle card", texture)
            && HasOrder(inventory, "H", "A", "M", "B", "C"),
            "Inserting between existing cards preserves the relative order of the old collection.");
        Check(inventory.TryInsert(inventory.Count, "T", "Tail card", replacement)
            && HasOrder(inventory, "H", "A", "M", "B", "C", "T"),
            "An insertion index equal to Count appends at the right edge.");
        MaterialEntry[] originalCards = inventory.Entries.ToArray();
        int eventsBeforeFailure = eventOrders.Count;
        Check(!inventory.TryInsert(-1, "negative", "Negative", texture)
            && !inventory.TryInsert(inventory.Count + 1, "past-end", "Past end", texture)
            && !inventory.TryInsert(0, "A", "Replacement A", replacement)
            && !inventory.TryInsert(0, " ", "Empty ID", texture)
            && !inventory.TryInsert(0, "no-art", "Missing art", null),
            "Invalid insertion positions, duplicate IDs and missing data are rejected.");
        Check(inventory.Entries.SequenceEqual(originalCards) && inventory.Count == 6
            && eventOrders.Count == eventsBeforeFailure
            && !inventory.Contains("negative") && !inventory.Contains("past-end") && !inventory.Contains("no-art"),
            "Failed insertions change neither cards, count, ID membership nor notifications.");

        Check(inventory.TryMove("H", 5) && HasOrder(inventory, "A", "M", "B", "C", "T", "H"),
            "Moving the first card to the final index shifts intervening cards left.");
        Check(inventory.TryMove("H", 0) && HasOrder(inventory, "H", "A", "M", "B", "C", "T"),
            "Moving the last card to the first index restores the original sequence.");
        Check(inventory.TryMove("B", 1) && HasOrder(inventory, "H", "B", "A", "M", "C", "T"),
            "An interior card can be inserted before another card without swapping away its neighbours.");
        Check(inventory.TryMove("H", 3) && HasOrder(inventory, "B", "A", "M", "H", "C", "T"),
            "Forward reorder destinations refer to the final index after removing the moved card.");
        Check(inventory.Count == 6 && originalCards.All(entry => inventory.Entries.Contains(entry))
            && inventory.Entries.All(entry => inventory.Contains(entry.MaterialId)),
            "Reordering keeps the same card objects, textures, stable IDs and inventory count.");
        MaterialEntry[] beforeRejectedMoves = inventory.Entries.ToArray();
        eventsBeforeFailure = eventOrders.Count;
        Check(!inventory.TryMove("B", 0) && !inventory.TryMove("B", -1)
            && !inventory.TryMove("B", inventory.Count) && !inventory.TryMove("missing", 1)
            && !inventory.TryMove(null, 1),
            "An unchanged position, invalid final index or missing stable ID is not a reorder.");
        Check(inventory.Entries.SequenceEqual(beforeRejectedMoves) && eventOrders.Count == eventsBeforeFailure,
            "Rejected and unchanged reorders leave the collection and observer notifications untouched.");

        Check(inventory.TryRemove("A") && !inventory.Contains("A") && inventory.Count == 5
            && HasOrder(inventory, "B", "M", "H", "C", "T"),
            "Deletion removes only the chosen stable ID and closes its gap while retaining neighbour order.");
        Check(inventory.TryInsert(2, "A", "Recollected A", replacement)
            && HasOrder(inventory, "B", "M", "A", "H", "C", "T")
            && inventory.Entries[2].Texture == replacement && inventory.Entries[2].DisplayName == "Recollected A"
            && !ReferenceEquals(inventory.Entries[2], originalA),
            "A deleted ID can be recollected with its new data at an arbitrary insertion point.");
        MaterialEntry[] beforeRejectedRemoval = inventory.Entries.ToArray();
        eventsBeforeFailure = eventOrders.Count;
        Check(!inventory.TryRemove("missing") && !inventory.TryRemove(null)
            && inventory.Entries.SequenceEqual(beforeRejectedRemoval) && eventOrders.Count == eventsBeforeFailure,
            "Removing an unknown ID does not touch cards or send an observer notification.");
        Check(consistentEvents && eventOrders.Count == 12
            && eventOrders[^1].SequenceEqual(inventory.Entries.Select(entry => entry.MaterialId)),
            "Every successful mutation sends one notification after the final count, ID set and card order agree.");
    }

    private void CheckScaleStorage(Texture2D texture)
    {
        var inventory = new MaterialInventory();
        int changes = 0;
        inventory.Changed += () => changes++;
        Check(inventory.TryAdd("default", "Default", texture) && inventory.Entries[0].Scale == Vector2.One,
            "Existing callers default to a source-pixel scale of one.");
        Vector2 authored = new(0.25f, 0.125f);
        Check(inventory.TryInsert(0, "scaled", "Scaled", texture, authored), "Collection stores an authored nonuniform scale.");
        MaterialEntry snapshot = inventory.Entries[0];
        authored.X = 3;
        Check(snapshot.Scale == new Vector2(0.25f, 0.125f)
            && inventory.TryMove("scaled", 1) && ReferenceEquals(inventory.Entries[1], snapshot),
            "Scale is a value snapshot that follows the original entry through reordering.");
        int beforeInvalid = changes;
        Check(!inventory.TryAdd("scaled", "Changed", texture, new Vector2(2, 2))
            && snapshot.Scale == new Vector2(0.25f, 0.125f), "Duplicate collection does not replace the stored scale.");
        Check(!inventory.TryAdd("zero", "Zero", texture, Vector2.Zero)
            && !inventory.TryAdd("nan", "NaN", texture, new Vector2(float.NaN, 1))
            && !inventory.TryAdd("infinite", "Infinity", texture, new Vector2(1, float.PositiveInfinity))
            && changes == beforeInvalid && inventory.Count == 2,
            "Invalid scale cannot create invisible or invalid pieces, consume slots, or trigger changes.");
        Check(inventory.TryRemove("scaled") && inventory.TryInsert(0, "scaled", "Recollected", texture, new Vector2(-0.5f, 0.5f))
            && inventory.Entries[0].Scale == new Vector2(-0.5f, 0.5f),
            "Deletion and recollection can adopt a new finite mirrored scale.");
    }

    private static bool HasOrder(MaterialInventory inventory, params string[] ids) =>
        inventory.Entries.Select(entry => entry.MaterialId).SequenceEqual(ids);

    private static Texture2D MakeTexture(Color color)
    {
        Image image = Image.CreateEmpty(32, 32, false, Image.Format.Rgba8);
        image.Fill(color);
        return ImageTexture.CreateFromImage(image);
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
        _checks++;
    }
}
