using Godot;
using System;

public partial class DragDetection : Control
{
	[Signal]
	public delegate void DragMovedXEventHandler(float deltaX);
	private bool _isDragging = false;

	private void OnGuiInput(InputEvent @event)
	{
		if(@event is InputEventMouseButton mouseEvent)
		{
			if(mouseEvent.ButtonIndex == MouseButton.Left)
			{
				_isDragging = mouseEvent.Pressed;
			}
		}
		if(_isDragging && @event is InputEventMouseMotion mouseMotion)
		{
			float deltaX = mouseMotion.Relative.X;
			EmitSignal(nameof(DragMovedX), deltaX);
		}
	}
	public override void _Ready()
	{
		GuiInput += OnGuiInput;
	}
}
