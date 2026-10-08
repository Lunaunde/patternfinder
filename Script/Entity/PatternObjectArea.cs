using Godot;
using System;

public partial class PatternObjectArea : Area2D
{
	public PatternObject Pattern => GetParent() as PatternObject;

	public float FrameInsideRate(Vector2 framePosition)
	{
		Vector2 delta = framePosition - GlobalPosition;
		Vector2 detectedRectSize = GetNode<CollisionShape2D>("CollisionShape2D").Shape.GetRect().Size / 2.0f * GlobalScale;
		float rateX = detectedRectSize.X != 0 ? Math.Abs(delta.X) / detectedRectSize.X : float.MaxValue;
		float rateY = detectedRectSize.Y != 0 ? Math.Abs(delta.Y) / detectedRectSize.Y : float.MaxValue;
		return 1 - (rateX + rateY);
	}
}
