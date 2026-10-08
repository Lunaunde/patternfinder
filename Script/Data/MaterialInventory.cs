using Godot;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

public sealed class MaterialEntry
{
	public string MaterialId { get; }
	public string DisplayName { get; }
	public Texture2D Texture { get; }
	public Vector2 Scale { get; }

	public MaterialEntry(string materialId, string displayName, Texture2D texture, Vector2? scale = null)
	{
		MaterialId = materialId;
		DisplayName = displayName;
		Texture = texture;
		Scale = scale ?? Vector2.One;
	}

	public static bool IsUsableScale(Vector2 scale) => float.IsFinite(scale.X) && float.IsFinite(scale.Y)
		&& scale.X != 0 && scale.Y != 0;
}

/// <summary>Garden materials are unique by material ID and occupy at most fourteen slots.</summary>
public sealed class MaterialInventory
{
	public const int Capacity = 14;
	public const int PageSize = 7;
	private readonly List<MaterialEntry> _entries = new();
	private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
	private readonly ReadOnlyCollection<MaterialEntry> _view;

	public int Count => _entries.Count;
	public IReadOnlyList<MaterialEntry> Entries => _view;
	public event Action Changed;

	public MaterialInventory()
	{
		_view = _entries.AsReadOnly();
	}

	public bool Contains(string id) => id != null && _ids.Contains(id);

	public bool TryAdd(string id, string displayName, Texture2D texture, Vector2? scale = null)
		=> TryInsert(Count, id, displayName, texture, scale);

	/// <summary>Insert a newly collected material at an index from zero through Count.</summary>
	public bool TryInsert(int index, string id, string displayName, Texture2D texture, Vector2? scale = null)
	{
		Vector2 materialScale = scale ?? Vector2.One;
		if (index < 0 || index > Count || string.IsNullOrWhiteSpace(id)
			|| texture == null || !MaterialEntry.IsUsableScale(materialScale) || Count >= Capacity || Contains(id))
			return false;
		_ids.Add(id);
		_entries.Insert(index, new MaterialEntry(id, string.IsNullOrWhiteSpace(displayName) ? id : displayName, texture, materialScale));
		Changed?.Invoke();
		return true;
	}

	/// <summary>Delete a collected material, freeing both its slot and its ID for recollection.</summary>
	public bool TryRemove(string id)
	{
		int index = FindIndex(id);
		if (index < 0)
			return false;
		_entries.RemoveAt(index);
		_ids.Remove(id);
		Changed?.Invoke();
		return true;
	}

	/// <summary>Move an existing entry to its final index, zero through Count - 1, after removing it from its old position.</summary>
	public bool TryMove(string id, int destinationIndex)
	{
		if (destinationIndex < 0 || destinationIndex >= Count)
			return false;
		int sourceIndex = FindIndex(id);
		if (sourceIndex < 0 || sourceIndex == destinationIndex)
			return false;
		MaterialEntry entry = _entries[sourceIndex];
		_entries.RemoveAt(sourceIndex);
		_entries.Insert(destinationIndex, entry);
		Changed?.Invoke();
		return true;
	}

	private int FindIndex(string id) => _entries.FindIndex(entry => string.Equals(entry.MaterialId, id, StringComparison.Ordinal));
}
