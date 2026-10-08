using Godot;

public partial class DragDetection : Control
{
    [Signal]
    public delegate void DragMovedXEventHandler(float deltaX);

    private bool _isDragging;
    private int _touchIndex = -1;
    private bool _interactionEnabled = true;

    public void SetInteractionEnabled(bool enabled)
    {
        _interactionEnabled = enabled;
        _isDragging = false;
        _touchIndex = -1;
    }

    private void OnGuiInput(InputEvent input)
    {
        if (!_interactionEnabled)
            return;
        if (input is InputEventMouseButton button && button.ButtonIndex == MouseButton.Left)
            _isDragging = button.Pressed;
        else if (_isDragging && input is InputEventMouseMotion motion)
            EmitSignal(SignalName.DragMovedX, GetGlobalTransformWithCanvas().BasisXform(motion.Relative).X);
        else if (input is InputEventScreenTouch touch)
        {
            if (touch.Pressed && _touchIndex < 0)
                _touchIndex = touch.Index;
            else if (!touch.Pressed && touch.Index == _touchIndex)
                _touchIndex = -1;
        }
        else if (input is InputEventScreenDrag drag && drag.Index == _touchIndex)
            EmitSignal(SignalName.DragMovedX, GetGlobalTransformWithCanvas().BasisXform(drag.Relative).X);
    }

    public override void _Input(InputEvent input)
    {
        // A button may consume the release outside this Control.
        if (input is InputEventMouseButton button && !button.Pressed)
            _isDragging = false;
        if (input is InputEventScreenTouch touch && !touch.Pressed && touch.Index == _touchIndex)
            _touchIndex = -1;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMWindowFocusOut)
            SetInteractionEnabled(_interactionEnabled);
    }

    public override void _Ready() => GuiInput += OnGuiInput;
}
