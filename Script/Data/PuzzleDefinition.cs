using Godot;

/// <summary>Shared match tolerances measured in Puzzle-local units and degrees.</summary>
[Tool]
[GlobalClass]
public partial class PuzzleDefinition : Resource
{
	[Export(PropertyHint.Range, "0,500,0.1,or_greater")]
	public float PositionTolerance { get; set; } = 40;

	[Export(PropertyHint.Range, "0,180,0.1")]
	public float AngleToleranceDegrees { get; set; } = 15;

	public bool HasValidTolerances => IsValidTolerance(PositionTolerance)
		&& IsValidTolerance(AngleToleranceDegrees);

	internal static bool IsValidTolerance(float value) => float.IsFinite(value) && value >= 0;
}
