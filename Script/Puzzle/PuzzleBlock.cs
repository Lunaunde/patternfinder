using Godot;

/// <summary>An editor-positioned target. Artwork is an editor preview; texture and pose remain available at runtime.</summary>
[Tool]
[GlobalClass]
public partial class PuzzleBlock : Sprite2D
{
	[Export] public string MaterialId { get; set; } = "";
	[Export] public bool UsePuzzleDefinition { get; set; } = true;

	[Export(PropertyHint.Range, "0,500,0.1,or_greater")]
	public float PositionTolerance { get; set; } = 40;

	[Export(PropertyHint.Range, "0,180,0.1")]
	public float AngleToleranceDegrees { get; set; } = 15;

	public override void _EnterTree()
	{
		if (!Engine.IsEditorHint())
			Hide();
	}

	public bool TryGetTolerances(PuzzleDefinition definition, out float positionTolerance,
		out float angleToleranceDegrees)
	{
		positionTolerance = 0;
		angleToleranceDegrees = 0;
		if (UsePuzzleDefinition)
		{
			if (!GodotObject.IsInstanceValid(definition) || !definition.HasValidTolerances)
				return false;
			positionTolerance = definition.PositionTolerance;
			angleToleranceDegrees = definition.AngleToleranceDegrees;
			return true;
		}
		if (!PuzzleDefinition.IsValidTolerance(PositionTolerance)
			|| !PuzzleDefinition.IsValidTolerance(AngleToleranceDegrees))
			return false;
		positionTolerance = PositionTolerance;
		angleToleranceDegrees = AngleToleranceDegrees;
		return true;
	}
}
