using Godot;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

/// <summary>
/// Level-owned, editor-authored targets. Material IDs are compared within this puzzle only;
/// the owning Level and the player's board retain level scope and target occupancy.
/// </summary>
[Tool]
[GlobalClass]
public partial class Puzzle : Node2D
{
	[Export] public PuzzleDefinition Definition { get; set; } = new();
	private readonly List<PuzzleBlock> _targets = new();
	private readonly ReadOnlyCollection<PuzzleBlock> _targetView;

	/// <summary>A stable read-only view, in scene-tree order. Refresh after adding or removing authored targets.</summary>
	public IReadOnlyList<PuzzleBlock> Targets => _targetView;
	public Control PuzzleFinish => GetNodeOrNull<Control>("PuzzleFinish");
	public bool IsFinished { get; private set; }

	public Puzzle()
	{
		_targetView = _targets.AsReadOnly();
	}

	public override void _Ready()
	{
		RefreshTargets();
		// The preview is separate from PuzzleBase, whose authored visuals may be used during play.
		if (!Engine.IsEditorHint())
		{
			GetNodeOrNull<CanvasItem>("NotebookPreview")?.Hide();
			SetFinished(false);
		}
	}

	/// <summary>The notebook page reports completion; an empty puzzle stays unfinished.</summary>
	internal void SetFinished(bool finished)
	{
		IsFinished = finished && _targets.Count > 0;
		if (PuzzleFinish != null)
			PuzzleFinish.Visible = IsFinished;
	}

	public IReadOnlyList<PuzzleBlock> GetTargets() => _targetView;

	public void RefreshTargets()
	{
		_targets.Clear();
		Node blocks = GetNodeOrNull("PuzzleBlocks");
		CollectTargets(blocks ?? this);
	}

	private void CollectTargets(Node parent)
	{
		foreach (Node child in parent.GetChildren())
		{
			// A nested puzzle has a separate coordinate system and target set.
            if (child is Puzzle || child is Level)
				continue;
			if (child is PuzzleBlock target)
				_targets.Add(target);
			CollectTargets(child);
		}
	}

	/// <summary>Find the nearest valid target; canUse lets the board skip occupied targets.</summary>
	public bool TryFindMatch(string materialId, Vector2 localPosition, float localRotationRadians,
		out PuzzleBlock target, Predicate<PuzzleBlock> canUse = null)
	{
		target = null;
		double bestDistance = double.PositiveInfinity;
		double bestAngle = double.PositiveInfinity;
		foreach (PuzzleBlock candidate in _targets)
		{
			if (!GodotObject.IsInstanceValid(candidate) || (canUse != null && !canUse(candidate))
				|| !TryEvaluate(candidate, materialId, localPosition, localRotationRadians,
					out double distance, out double angle))
				continue;
			if (distance < bestDistance || (distance == bestDistance && angle < bestAngle))
			{
				target = candidate;
				bestDistance = distance;
				bestAngle = angle;
			}
		}
		return target != null;
	}

	public bool Matches(PuzzleBlock target, string materialId, Vector2 localPosition, float localRotationRadians)
		=> _targets.Contains(target)
			&& TryEvaluate(target, materialId, localPosition, localRotationRadians, out _, out _);

	private bool TryEvaluate(PuzzleBlock target, string materialId, Vector2 localPosition,
		float localRotationRadians, out double distance, out double angleDegrees)
	{
		distance = double.PositiveInfinity;
		angleDegrees = double.PositiveInfinity;
		if (!GodotObject.IsInstanceValid(target) || string.IsNullOrWhiteSpace(materialId)
			|| !string.Equals(materialId, target.MaterialId, StringComparison.Ordinal)
			|| !IsFinite(localPosition) || !float.IsFinite(localRotationRadians)
			|| !target.TryGetTolerances(Definition, out float positionTolerance, out float angleTolerance)
			|| !TryGetTargetPose(target, out Vector2 targetPosition, out float targetRotation))
			return false;
		double dx = (double)localPosition.X - targetPosition.X;
		double dy = (double)localPosition.Y - targetPosition.Y;
		distance = Math.Sqrt(dx * dx + dy * dy);
		angleDegrees = Math.Abs(Math.IEEERemainder((double)localRotationRadians - targetRotation, Math.Tau))
			* 180 / Math.PI;
		return distance <= positionTolerance && angleDegrees <= angleTolerance;
	}

	/// <summary>Resolve a target pose in Puzzle coordinates, including any intermediary Node2D transforms.</summary>
	public bool TryGetTargetPose(PuzzleBlock target, out Vector2 localPosition, out float localRotationRadians)
	{
		localPosition = Vector2.Zero;
		localRotationRadians = 0;
		if (!GodotObject.IsInstanceValid(target))
			return false;
		Transform2D transform = Transform2D.Identity;
		Node current = target;
		while (current != this)
		{
			if (current == null)
				return false;
			if (current is Node2D node)
				transform = node.Transform * transform;
			current = current.GetParent();
		}
		localPosition = transform.Origin;
		localRotationRadians = transform.X.Angle();
		return IsFinite(localPosition) && IsFinite(transform.X)
			&& IsFinite(transform.Y) && transform.X.LengthSquared() > 0
			&& float.IsFinite(localRotationRadians);
	}

	private static bool IsFinite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);
}
