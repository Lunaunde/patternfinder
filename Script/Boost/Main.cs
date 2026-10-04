using Godot;
using System;

public partial class Main : Node
{
	[Export]
	private PackedScene BackgroundScene { get; set; }
	[Export]
	private Texture2D BackgroundTexture { get; set; }

	[Export]
	private PackedScene FarmeScene { get; set; }

	private Camera2D _camera;
	private Frame _frame;
	private VirtualJoystick _joystick;
	private float _cameraXBound = 0.0f;

	private void MoveCamera(float deltaX)
	{
		_camera.GlobalPosition -= new Vector2((float)deltaX, 0);
		if(Math.Abs(_camera.GlobalPosition.X) > _cameraXBound)
		{
			_camera.GlobalPosition = new Vector2(_cameraXBound * Math.Sign(_camera.GlobalPosition.X), _camera.GlobalPosition.Y);
		}
	}
	private void SummonFrame()
	{
		if(_frame != null)
		{
			_frame.QueueFree();
		}
		_frame = FarmeScene.Instantiate<Frame>();
		_frame.GlobalPosition = _camera.GlobalPosition;
		AddChild(_frame);
	}
	private void DeleteFrame(Vector2 justToFitVector2 = new Vector2())
	{
		if(_frame != null)
		{
			_frame.QueueFree();
			_frame = null;
		}
	}
	public override void _Ready()
	{
		_camera = GetNode<Camera2D>("Camera2D");
		_joystick = GetNode<VirtualJoystick>("UILayer/VirtualJoystick");
		_joystick.Pressed += SummonFrame;
		_joystick.Released += DeleteFrame;

		var background = BackgroundScene.Instantiate<Background>();
		AddChild(background);
		background.SetTexture(BackgroundTexture);

		var dragDetection = GetNode<DragDetection>("UILayer/DragDetection");

		_cameraXBound = background.GetCameraXBound();
		dragDetection.DragMovedX += MoveCamera;
	}



	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	public override void _PhysicsProcess(double delta)
    {
		if(_frame != null)
        {
            Vector2 dir = Input.GetVector(
                "ui_left", "ui_right",
                "ui_up", "ui_down"
            );
			_frame.Move(dir);
        }
    }
}
