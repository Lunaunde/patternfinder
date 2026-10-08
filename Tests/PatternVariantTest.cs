using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Exercises the actual prototype selector, stable IDs, and fifth-recognition guarantee.</summary>
public partial class PatternVariantTest : Node
{
    private readonly List<PatternObject> _prototypes = new();
    private int _checks;

    public override void _Ready()
    {
        try
        {
            Texture2D texture = MakeTexture(Colors.Olive);
            Texture2D otherTexture = MakeTexture(Colors.Teal);
            CheckSingleCandidateAndFrozenSelection(texture, otherTexture);
            CheckInvalidConfiguration(texture);
            CheckScaleCatalogue(texture);
            CheckFifthRecognition(texture);
            CheckFullLibrary(texture);
            CheckUniformUniqueCandidates(texture);
            GD.Print($"Pattern variants: {_checks} checks passed.");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
        finally
        {
            foreach (PatternObject prototype in _prototypes)
                prototype.Free();
        }
    }

    private void CheckSingleCandidateAndFrozenSelection(Texture2D texture, Texture2D otherTexture)
    {
        PatternObject prototype = Prototype();
        var variant = Variant("single", "Single card", texture);
        variant.Scale = new Vector2(0.25f, 0.125f);
        prototype.Variants.Add(variant);
        var inventory = new MaterialInventory();
        Check(prototype.TrySelectMaterial(inventory, out MaterialEntry frozen)
            && frozen.MaterialId == "single" && frozen.DisplayName == "Single card" && frozen.Texture == texture
            && frozen.Scale == new Vector2(0.25f, 0.125f),
            "A prototype with one configured variant selects that collectible.");
        variant.MaterialId = "edited-id";
        variant.MaterialName = "Edited card";
        variant.MaterialTexture = otherTexture;
        variant.Scale = new Vector2(9, 9);
        prototype.Variants.Clear();
        Check(frozen.MaterialId == "single" && frozen.DisplayName == "Single card" && frozen.Texture == texture
            && frozen.Scale == new Vector2(0.25f, 0.125f),
            "Changing a Resource after selection cannot change the frozen ID, name, texture, or scale.");
        Check(inventory.Count == 0 && !inventory.Contains("single"),
            "Selecting a candidate does not collect it before the photo presentation commits.");
        Check(!prototype.TrySelectMaterial(inventory, out MaterialEntry cleared) && cleared == null
            && prototype.SuccessfulRecognitionCount == 1,
            "Clearing the selected candidates produces a failure without spending another recognition attempt.");
        prototype.Variants = null;
        Check(!prototype.TrySelectMaterial(inventory, out _) && prototype.SuccessfulRecognitionCount == 1,
            "A null candidate list fails safely without changing the recognition count.");
        prototype.Variants = new Godot.Collections.Array<PatternVariant>();
        Check(!prototype.TrySelectMaterial(inventory, out _) && prototype.SuccessfulRecognitionCount == 1,
            "A fresh empty candidate list fails without consuming a recognition attempt.");
        prototype.Variants.Add(Variant("unnamed", " ", texture));
        Check(prototype.TrySelectMaterial(inventory, out MaterialEntry unnamed) && unnamed.DisplayName == "unnamed"
            && prototype.SuccessfulRecognitionCount == 2,
            "An unnamed candidate has a usable ID-based display name.");
    }

    private void CheckInvalidConfiguration(Texture2D texture)
    {
        PatternObject prototype = Prototype();
        var inventory = new MaterialInventory();
        Check(!prototype.TrySelectMaterial(inventory, out _) && prototype.SuccessfulRecognitionCount == 0,
            "An unconfigured prototype produces a recognition failure without spending a guarantee attempt.");
        prototype.Variants.Add(null);
        prototype.Variants.Add(Variant(" ", "Empty ID", texture));
        prototype.Variants.Add(Variant("missing-texture", "Missing texture", null));
        prototype.Variants.Add(Variant("empty-texture", "Zero-size texture", new AtlasTexture()));
        prototype.Variants.Add(new PatternVariant { MaterialId = "zero-scale", MaterialTexture = texture, Scale = Vector2.Zero });
        prototype.Variants.Add(new PatternVariant { MaterialId = "nan-scale", MaterialTexture = texture, Scale = new Vector2(float.NaN, 1) });
        prototype.Variants.Add(new PatternVariant { MaterialId = "infinite-scale", MaterialTexture = texture, Scale = new Vector2(1, float.PositiveInfinity) });
        Check(!prototype.TrySelectMaterial(inventory, out _) && prototype.SuccessfulRecognitionCount == 0,
            "A wholly invalid variant list fails safely without producing a collectible.");
        var invalidNativeTexture = new AtlasTexture();
        invalidNativeTexture.Dispose();
        prototype.Variants.Add(Variant("disposed-texture", "Disposed texture", invalidNativeTexture));
        Check(!prototype.TrySelectMaterial(inventory, out _) && prototype.SuccessfulRecognitionCount == 0,
            "A disposed texture is excluded before any size query or successful-recognition count change.");
        prototype.Variants.Add(Variant("valid", "", texture));
        Check(prototype.TrySelectMaterial(inventory, out MaterialEntry selected)
            && selected.MaterialId == "valid" && selected.DisplayName == "valid"
            && prototype.SuccessfulRecognitionCount == 1,
            "Mixed invalid candidates do not prevent a valid candidate from being selected.");
    }

    private void CheckScaleCatalogue(Texture2D texture)
    {
        var level = new Level();
        try
        {
            var patterns = new Node2D { Name = "Patterns" };
            level.AddChild(patterns);
            var prototype = new PatternObject();
            patterns.AddChild(prototype);
            var small = new PatternVariant { MaterialId = "small", MaterialTexture = texture, Scale = new Vector2(0.25f, 0.25f) };
            prototype.Variants.Add(small);
            prototype.Variants.Add(new PatternVariant { MaterialId = "large", MaterialTexture = texture, Scale = new Vector2(0.5f, 0.5f) });
            prototype.Variants.Add(new PatternVariant { MaterialId = "invalid", MaterialTexture = texture, Scale = new Vector2(1, 0) });
            prototype.Variants.Add(new PatternVariant { MaterialId = "mirrored", MaterialTexture = texture, Scale = new Vector2(-0.25f, 0.125f) });
            Check(new PatternVariant().Scale == Vector2.One, "Unconfigured variants use a source-pixel scale of one.");
            level.RefreshMaterials();
            Check(level.TryResolveMaterial("small", out MaterialEntry frozenSmall)
                && level.TryResolveMaterial("large", out MaterialEntry large)
                && frozenSmall.Texture == large.Texture && frozenSmall.Scale.X * 2 == large.Scale.X,
                "The same source texture can have explicit independent sizes in the Level catalogue.");
            small.Scale = new Vector2(3, 3);
            Check(frozenSmall.Scale == new Vector2(0.25f, 0.25f), "A catalogue entry retains its scale snapshot after the Resource changes.");
            Check(!level.OwnsMaterial("invalid") && level.TryResolveMaterial("mirrored", out MaterialEntry mirrored)
                && mirrored.Scale == new Vector2(-0.25f, 0.125f),
                "Catalogue validation rejects zero scale while preserving finite signed nonuniform scale.");
        }
        finally { level.Free(); }
    }

    private void CheckFifthRecognition(Texture2D texture)
    {
        PatternObject prototype = Prototype();
        prototype.Variants.Add(Variant("collected-a", "A", texture));
        prototype.Variants.Add(Variant("collected-b", "B", texture));
        prototype.Variants.Add(Variant("missing-c", "C", texture));
        var inventory = new MaterialInventory();
        inventory.TryAdd("collected-a", "A", texture);
        inventory.TryAdd("collected-b", "B", texture);
        for (int attempt = 1; attempt < 5; attempt++)
            Check(prototype.TrySelectMaterial(inventory, out MaterialEntry candidate)
                && new[] { "collected-a", "collected-b", "missing-c" }.Contains(candidate.MaterialId),
                $"Ordinary successful recognition {attempt} chooses an available candidate.");
        PatternObject independent = Prototype();
        independent.Variants.Add(Variant("independent", "Independent", texture));
        Check(independent.TrySelectMaterial(inventory, out _) && independent.SuccessfulRecognitionCount == 1
            && prototype.SuccessfulRecognitionCount == 4,
            "Recognition counters belong to each prototype instance rather than another plant's photos.");
        Godot.Collections.Array<PatternVariant> configured = prototype.Variants;
        prototype.Variants = new Godot.Collections.Array<PatternVariant> { null };
        Check(!prototype.TrySelectMaterial(inventory, out _) && prototype.SuccessfulRecognitionCount == 4,
            "A failed recognition between the fourth and fifth successes does not consume the guarantee.");
        prototype.Variants = configured;
        Check(prototype.TrySelectMaterial(inventory, out MaterialEntry fifth) && fifth.MaterialId == "missing-c"
            && prototype.SuccessfulRecognitionCount == 5 && inventory.Count == 2,
            "The fifth valid recognition guarantees the uncollected candidate without mutating the library.");
        for (int attempt = 6; attempt < 10; attempt++)
            Check(prototype.TrySelectMaterial(inventory, out _), $"Ordinary recognition {attempt} remains valid.");
        Check(prototype.TrySelectMaterial(inventory, out MaterialEntry tenth) && tenth.MaterialId == "missing-c",
            "If the material has not been committed, the tenth valid recognition guarantees it again.");
        inventory.TryAdd("missing-c", "C", texture);
        for (int attempt = 11; attempt <= 15; attempt++)
            Check(prototype.TrySelectMaterial(inventory, out MaterialEntry candidate) && inventory.Contains(candidate.MaterialId),
                $"Recognition {attempt} remains valid after every candidate has been collected.");
        Check(prototype.SuccessfulRecognitionCount == 15 && inventory.Count == 3,
            "The guarantee becomes ordinary selection when no uncollected candidate remains.");
    }

    private void CheckFullLibrary(Texture2D texture)
    {
        var inventory = new MaterialInventory();
        inventory.TryAdd("known", "Known", texture);
        for (int index = 1; index < 14; index++)
            inventory.TryAdd($"filler-{index}", $"Filler {index}", texture);
        PatternObject prototype = Prototype();
        prototype.Variants.Add(Variant("known", "Known", texture));
        prototype.Variants.Add(Variant("new-while-full", "New", texture));
        for (int attempt = 1; attempt < 5; attempt++)
            prototype.TrySelectMaterial(inventory, out _);
        Check(prototype.TrySelectMaterial(inventory, out MaterialEntry fifth)
            && fifth.MaterialId == "new-while-full" && inventory.Count == 14 && !inventory.Contains(fifth.MaterialId),
            "A full library does not bypass recognition or mark a rejected material as collected.");
    }

    private void CheckUniformUniqueCandidates(Texture2D texture)
    {
        PatternObject prototype = Prototype();
        prototype.Variants.Add(Variant("a", "First A", texture));
        prototype.Variants.Add(Variant("a", "Duplicate A", texture));
        prototype.Variants.Add(Variant("a", "Another duplicate A", texture));
        prototype.Variants.Add(Variant("b", "B", texture));
        prototype.Variants.Add(Variant("c", "C", texture));
        var inventory = new MaterialInventory();
        var counts = new Dictionary<string, int> { ["a"] = 0, ["b"] = 0, ["c"] = 0 };
        bool valid = true;
        for (int draw = 0; draw < 6000; draw++)
        {
            if (!prototype.TrySelectMaterial(inventory, out MaterialEntry selected) || !counts.ContainsKey(selected.MaterialId))
            {
                valid = false;
                break;
            }
            counts[selected.MaterialId]++;
            if (selected.MaterialId == "a" && selected.DisplayName != "First A")
                valid = false;
        }
        Check(valid && prototype.SuccessfulRecognitionCount == 6000,
            "Only unique valid IDs participate, and duplicate definitions preserve the first valid card.");
        // A broad statistical bound catches accidental duplicate weighting without a fragile exact sequence.
        Check(counts.Values.All(count => count >= 1500 && count <= 2500),
            $"Unique candidates have approximately equal chances despite duplicate configuration: a={counts["a"]}, b={counts["b"]}, c={counts["c"]}.");
        Check(inventory.Count == 0, "Repeated random recognition never inserts materials into the inventory itself.");
    }

    private PatternObject Prototype()
    {
        var prototype = new PatternObject();
        _prototypes.Add(prototype);
        return prototype;
    }

    private static PatternVariant Variant(string id, string name, Texture2D texture) => new()
    {
        MaterialId = id, MaterialName = name, MaterialTexture = texture
    };

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
