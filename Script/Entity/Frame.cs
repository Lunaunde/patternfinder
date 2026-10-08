using Godot;
using System;

public partial class Frame : Node2D
{
	enum DetectType
	{
		None,
//		Detecting,
		Detected
	}
	struct DetectingPattern
	{
		public PatternObjectArea patternObjectArea;
		public float detectingInsideRate;
	}
	Area2D _area;
	Sprite2D _detectedSprite;
	Sprite2D _undetectedSprite;
	DetectType _detectType = DetectType.None;
	// float _detectingTime = 0.0f;
	// Vector2 _detectingPatternPosition;
	// Vector2 _detectingStartPosition;
	DetectingPattern _detectingPattern;
	Vector2 _stickForce;
	public bool IsDetected => _detectType == DetectType.Detected;

	public Rect2 CaptureWorldRect
	{
		get
		{
			var collisionShape = GetNodeOrNull<CollisionShape2D>("Area2D/CollisionShape2D");
			if(collisionShape?.Shape == null)
				return new Rect2(GlobalPosition, Vector2.Zero);

			Rect2 localRect = collisionShape.Shape.GetRect();
			Transform2D transform = collisionShape.GlobalTransform;
			Rect2 worldRect = new Rect2(transform * localRect.Position, Vector2.Zero);
			worldRect = worldRect.Expand(transform * new Vector2(localRect.End.X, localRect.Position.Y));
			worldRect = worldRect.Expand(transform * localRect.End);
			return worldRect.Expand(transform * new Vector2(localRect.Position.X, localRect.End.Y));
		}
	}

	public bool TryGetCaptureTarget(out PatternObject target, out float insideRate)
	{
		target = null;
		insideRate = 0.0f;
		if(!TryFindBestPattern(out var candidate))
			return false;

		target = candidate.patternObjectArea.Pattern;
		insideRate = candidate.detectingInsideRate;
		return true;
	}

	private bool TryFindBestPattern(out DetectingPattern candidate)
	{
		candidate = new DetectingPattern { detectingInsideRate = -float.MaxValue };
		if(!GodotObject.IsInstanceValid(_area) || !_area.IsInsideTree())
			return false;

		foreach(var body in _area.GetOverlappingAreas())
		{
			if(body is PatternObjectArea patternObjectArea &&
				GodotObject.IsInstanceValid(patternObjectArea) && !patternObjectArea.IsQueuedForDeletion())
			{
				PatternObject pattern = patternObjectArea.Pattern;
				var collisionShape = patternObjectArea.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
				if(!GodotObject.IsInstanceValid(pattern) || pattern.IsQueuedForDeletion() ||
					collisionShape?.Shape == null || collisionShape.Disabled)
					continue;

				float insideRate = patternObjectArea.FrameInsideRate(GlobalPosition);
				if(float.IsFinite(insideRate) && insideRate > candidate.detectingInsideRate)
				{
					candidate.patternObjectArea = patternObjectArea;
					candidate.detectingInsideRate = insideRate;
				}
			}
		}
		return candidate.patternObjectArea != null;
	}

	private void DetectingProcess()
	{
		if(TryFindBestPattern(out _detectingPattern) &&
			_detectingPattern.detectingInsideRate > Config.FRAME_DETECTED_RATE)
		{
			_detectType = DetectType.Detected;
			// _detectingPatternPosition = _detectingPattern.patternObjectArea.GlobalPosition;
			// _detectingStartPosition = GlobalPosition;
			// _detectingTime = Time.GetTicksMsec() / 1000.0f;
		}
	}
	// private void MoveToDetectingPattern()
	// {
	// 	double timeRate = ((Time.GetTicksMsec() / 1000.0) - _detectingTime) / Config.FRAME_DETECTED_MOVING_TIME;
	// 	if(timeRate > 1.0)
	// 	{
	// 		timeRate = 1.0;
	// 		_detectType = DetectType.Detected;
	// 	}
	// 	_detectedSprite.Modulate = new Color(1, 1, 1, (float)timeRate);
	// 	_undetectedSprite.Modulate = new Color(1, 1, 1, 1.0f - (float)timeRate);
	// 	double movingRate = (1 + Math.Sin(- Math.PI / 2.0 + timeRate * Math.PI)) / 2.0;
	// 	GlobalPosition = (_detectingPatternPosition - _detectingStartPosition) * (float)movingRate + _detectingStartPosition;
	// }
	private void StickToDetectingPattern()
	{
		double frameInsideRat = _detectingPattern.patternObjectArea.FrameInsideRate(GlobalPosition);
		if(frameInsideRat < Config.OUT_OF_STICK_RATE)
		{
			ExitDetectedState();
		}
		else
		{
			_detectedSprite.Modulate = new Color(1, 1, 1, (float)frameInsideRat);
			_undetectedSprite.Modulate = new Color(1, 1, 1, (float)(1.0f-frameInsideRat));
			_stickForce = (_detectingPattern.patternObjectArea.GlobalPosition - GlobalPosition).Normalized() * (float)(frameInsideRat * Config.STICK_FORCE_RATE);
		}
	}
	private void ExitDetectedState()
	{
		_detectType = DetectType.None;
		_detectedSprite.Modulate = new Color(1, 1, 1, 0.0f);
		_undetectedSprite.Modulate = new Color(1, 1, 1, 1.0f);
		_stickForce = Vector2.Zero;
	}
	public void Move(Vector2 delta)
	{
		switch(_detectType)
		{
			case DetectType.None:
				DetectingProcess();
				GlobalPosition += delta * Config.JOYSTICK_SPEED;
				break;
			// case DetectType.Detecting:
			// 	MoveToDetectingPattern();
			// 	break;
			case DetectType.Detected:
				if(!TryFindBestPattern(out _detectingPattern))
				{
					ExitDetectedState();
					GlobalPosition += delta * Config.JOYSTICK_SPEED;
					break;
				}
				StickToDetectingPattern();
				if(_detectingPattern.patternObjectArea.FrameInsideRate(GlobalPosition) < Config.FORCE_STICK_RATE || delta.Length() > Config.STICK_FORCE_RATE)
				{
					GlobalPosition += (delta + _stickForce) * Config.JOYSTICK_SPEED;
				}
				else
				{
					GlobalPosition = _detectingPattern.patternObjectArea.GlobalPosition;
				}
				break;
		}
	}
	public override void _Ready()
	{
		_area = GetNode<Area2D>("Area2D");
		_detectedSprite = GetNode<Sprite2D>("Detected");
		_undetectedSprite = GetNode<Sprite2D>("Undetected");
		_detectedSprite.Modulate = new Color(1, 1, 1, 0.0f);
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _PhysicsProcess(double delta)
	{
	}
}
