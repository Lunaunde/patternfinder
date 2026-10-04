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
	private void DetectingProcess()
	{
		_detectingPattern = new DetectingPattern();
		_detectingPattern.detectingInsideRate = -float.MaxValue;
		foreach(var body in _area.GetOverlappingAreas())
		{
			if(body is PatternObjectArea patternObjectArea)
			{
				if(patternObjectArea.FrameInsideRate(GlobalPosition) > _detectingPattern.detectingInsideRate)
				{
					GD.Print("Detecting pattern: " + patternObjectArea.Name + ", inside rate: " + patternObjectArea.FrameInsideRate(GlobalPosition));
					_detectingPattern.patternObjectArea = patternObjectArea;
					_detectingPattern.detectingInsideRate = patternObjectArea.FrameInsideRate(GlobalPosition);
				}
			}
		}

		if(_detectingPattern.detectingInsideRate > Config.FRAME_DETECTED_RATE)
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
			_detectType = DetectType.None;
			_detectedSprite.Modulate = new Color(1, 1, 1, 0.0f);
			_undetectedSprite.Modulate = new Color(1, 1, 1, 1.0f);
			_stickForce = Vector2.Zero;
		}
		else
		{
			_detectedSprite.Modulate = new Color(1, 1, 1, (float)frameInsideRat);
			_undetectedSprite.Modulate = new Color(1, 1, 1, (float)(1.0f-frameInsideRat));
			_stickForce = (_detectingPattern.patternObjectArea.GlobalPosition - GlobalPosition).Normalized() * (float)(frameInsideRat * Config.STICK_FORCE_RATE);
		}
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
				StickToDetectingPattern();
				if(_detectingPattern.patternObjectArea.FrameInsideRate(GlobalPosition) < Config.FORCE_STICK_RATE || delta.Length() > Config.STICK_FORCE_RATE)
				{
					GD.Print("FrameInsideRate: " + _detectingPattern.patternObjectArea.FrameInsideRate(GlobalPosition));
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
