using Godot;

/// <summary>A configurable collectible pattern produced by a garden prototype.</summary>
[GlobalClass]
public partial class PatternVariant : Resource
{
	[Export] public string MaterialId { get; set; } = "";
	[Export] public string MaterialName { get; set; } = "";
	[Export] public Texture2D MaterialTexture { get; set; }
	/// <summary>Source texture pixels to Puzzle-local units; independent of the garden prototype's scale.</summary>
	[Export] public Vector2 Scale { get; set; } = Vector2.One;
}
