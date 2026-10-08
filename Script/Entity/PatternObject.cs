using Godot;
using System;
using System.Collections.Generic;

public partial class PatternObject : Sprite2D
{
	[Export] public Godot.Collections.Array<PatternVariant> Variants { get; set; } = new();

	// This counts valid recognition selections, including duplicates and full-library results.
	// PhotoFlow calls it once after its coverage check and before asynchronous capture.
	public long SuccessfulRecognitionCount { get; private set; }
	private readonly Random _variantRandom = new();

	/// <summary>
	/// Freeze a uniformly selected candidate. Every fifth valid recognition selects an
	/// uncollected material when one exists. This method never commits to the inventory.
	/// </summary>
	public bool TrySelectMaterial(MaterialInventory inventory, out MaterialEntry selected)
	{
		selected = null;
		if (inventory == null)
			throw new ArgumentNullException(nameof(inventory));
		List<MaterialEntry> candidates = GetValidMaterials();
		if (candidates.Count == 0)
			return false;

		SuccessfulRecognitionCount++;
		List<MaterialEntry> pool = candidates;
		if (SuccessfulRecognitionCount % 5 == 0)
		{
			List<MaterialEntry> uncollected = candidates.FindAll(candidate => !inventory.Contains(candidate.MaterialId));
			if (uncollected.Count > 0)
				pool = uncollected;
		}
		selected = pool[_variantRandom.Next(pool.Count)];
		return true;
	}

	private List<MaterialEntry> GetValidMaterials()
	{
		List<MaterialEntry> candidates = new();
		HashSet<string> ids = new(StringComparer.Ordinal);
		if (Variants != null)
		{
			foreach (PatternVariant variant in Variants)
			{
				if (GodotObject.IsInstanceValid(variant))
					AddMaterial(candidates, ids, variant.MaterialId, variant.MaterialName, variant.MaterialTexture, variant.Scale);
			}
		}
		return candidates;
	}

	private static void AddMaterial(List<MaterialEntry> candidates, HashSet<string> ids,
		string id, string name, Texture2D texture, Vector2 scale)
	{
		if (string.IsNullOrWhiteSpace(id) || !GodotObject.IsInstanceValid(texture) || !MaterialEntry.IsUsableScale(scale))
			return;
		if (texture is AtlasTexture atlas && !GodotObject.IsInstanceValid(atlas.Atlas))
			return;
		Vector2 size = texture.GetSize();
		if (size.X <= 0 || size.Y <= 0 || !ids.Add(id))
			return;
		// Copy the values rather than returning the mutable Resource itself.
		candidates.Add(new MaterialEntry(id, string.IsNullOrWhiteSpace(name) ? id : name, texture, scale));
	}

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		if(Texture != null)
		{
			var collisionShape = GetNode<CollisionShape2D>("Area2D/CollisionShape2D");
			GD.Print( Texture != null ? "PatternObject: " + Name + ", texture size: " + Texture.GetSize() : "PatternObject: " + Name + ", texture is null");
			GD.Print( collisionShape != null ? "PatternObject: " + Name + ", collision shape is not null" : "PatternObject: " + Name + ", collision shape is null");
			if(Texture != null && collisionShape != null)
			{
				var textureSize = Texture.GetSize();
                collisionShape.Shape = new RectangleShape2D{Size = textureSize};
			}
		}
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
