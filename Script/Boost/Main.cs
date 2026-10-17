using Godot;

public partial class Main : Node
{
    [Export] private PackedScene LevelScene { get; set; }
    [Export] private PackedScene NotebookScene { get; set; }
    [Export] private PackedScene FarmeScene { get; set; }

    private Camera2D _camera;
    private Background _background;
    private Frame _frame;
    private VirtualJoystick _joystick;
    private DragDetection _dragDetection;
    private TextureButton _photoButton;
    private TextureButton _noteIcon;
    private Bar _bar;
    private PhotoFlow _photoFlow;
    private bool _joystickHeld;
    private bool _keyboardHeld;
    private bool _framingRetained;
    private int _joystickPointer = -2;
    private int _lastPointer = -1;
    private float _cameraXBound;
    private Vector2 _worldViewportSize;
    private bool _gardenBarWasOpen;
    private readonly MaterialInventory _emptyInventory = new();

    public Level CurrentLevel { get; private set; }
    public NotebookUI Notebook { get; private set; }
    public MaterialInventory Inventory => CurrentLevel?.Inventory ?? _emptyInventory;
    public bool IsFramingHeld => _joystickHeld || _keyboardHeld;
    public bool IsFramingActive => GodotObject.IsInstanceValid(_frame) && !_frame.IsQueuedForDeletion();
    public bool IsPhotographing => _photoFlow?.IsBusy ?? false;
    public bool IsNotebookOpen { get; private set; }

    public bool OpenNotebook()
    {
        if (IsPhotographing || IsFramingHeld || IsNotebookOpen)
            return false;
        _gardenBarWasOpen = _bar.IsOpen;
        IsNotebookOpen = true;
        Notebook.BindLevel(CurrentLevel);
        Notebook.Show();
        _bar.SetNotebookMode(true);
        _bar.SetOpen(true, false);
        _dragDetection.SetInteractionEnabled(false);
        ReleaseDirections();
        UpdateFraming();
        return true;
    }

    public void CloseNotebook()
    {
        if (!IsNotebookOpen)
            return;
        Notebook.Board.CancelDrag();
        _bar.SetNotebookMode(false);
        Notebook.Hide();
        IsNotebookOpen = false;
        _bar.SetOpen(_gardenBarWasOpen, false);
        _dragDetection.SetInteractionEnabled(true);
        UpdateFraming();
    }

    private void MoveCamera(float deltaX)
    {
        if (IsPhotographing || IsNotebookOpen)
            return;
        var position = _camera.GlobalPosition;
        position.X = Mathf.Clamp(position.X - deltaX / _camera.Zoom.X, -_cameraXBound, _cameraXBound);
        _camera.GlobalPosition = position;
        _camera.ForceUpdateScroll();
        ConstrainFrameToViewport(false);
    }

    private void UpdateCameraBounds()
    {
        Vector2 worldViewportSize = GetViewport().GetVisibleRect().Size / _camera.Zoom;
        _worldViewportSize = worldViewportSize;
        _background.ScaleToViewport(worldViewportSize);
        _cameraXBound = _background.GetCameraXBound(worldViewportSize.X);
        var position = _camera.GlobalPosition;
        position.X = Mathf.Clamp(position.X, -_cameraXBound, _cameraXBound);
        _camera.GlobalPosition = position;
        _camera.ForceUpdateScroll();
        ConstrainFrameToViewport(false);
    }

    private Rect2 GetWorldViewportRect()
    {
        Rect2 screenRect = GetViewport().GetVisibleRect();
        Transform2D transform = GetViewport().GetCanvasTransform().AffineInverse();
        Rect2 worldRect = new(transform * screenRect.Position, Vector2.Zero);
        worldRect = worldRect.Expand(transform * new Vector2(screenRect.End.X, screenRect.Position.Y));
        worldRect = worldRect.Expand(transform * screenRect.End);
        return worldRect.Expand(transform * new Vector2(screenRect.Position.X, screenRect.End.Y));
    }

    private void ConstrainFrameToViewport(bool followHorizontally)
    {
        if (!IsFramingActive || IsPhotographing || IsNotebookOpen)
            return;

        Rect2 viewportRect = GetWorldViewportRect();
        Rect2 frameRect = _frame.WorldBounds;
        if (followHorizontally && frameRect.Size.X <= viewportRect.Size.X)
        {
            float overflow = frameRect.Position.X < viewportRect.Position.X
                ? frameRect.Position.X - viewportRect.Position.X
                : Mathf.Max(0, frameRect.End.X - viewportRect.End.X);
            if (!Mathf.IsZeroApprox(overflow))
            {
                Vector2 cameraPosition = _camera.GlobalPosition;
                cameraPosition.X = Mathf.Clamp(cameraPosition.X + overflow, -_cameraXBound, _cameraXBound);
                _camera.GlobalPosition = cameraPosition;
                _camera.ForceUpdateScroll();
                viewportRect = GetWorldViewportRect();
            }
        }

        // Clamp the visible frame independently of the recognition/capture geometry.
        Vector2 position = _frame.GlobalPosition;
        Vector2 minimum = viewportRect.Position + position - frameRect.Position;
        Vector2 maximum = viewportRect.End + position - frameRect.End;
        position.X = minimum.X <= maximum.X
            ? Mathf.Clamp(position.X, minimum.X, maximum.X)
            : (minimum.X + maximum.X) * 0.5f;
        position.Y = minimum.Y <= maximum.Y
            ? Mathf.Clamp(position.Y, minimum.Y, maximum.Y)
            : (minimum.Y + maximum.Y) * 0.5f;
        _frame.GlobalPosition = position;
    }

    private void JoystickPressed()
    {
        if (IsPhotographing || IsNotebookOpen)
            return;
        _joystickHeld = true;
        _joystickPointer = _lastPointer;
        UpdateFraming();
    }

    private void JoystickReleased(Vector2 unused)
    {
        _joystickHeld = false;
        _joystickPointer = -2;
        UpdateFraming();
    }

    private void UpdateFraming()
    {
        if (IsNotebookOpen)
        {
            RemoveFrame();
        }
        else if (IsFramingHeld)
        {
            _framingRetained = false;
            if (!IsPhotographing && !IsFramingActive)
            {
                _frame = FarmeScene.Instantiate<Frame>();
                AddChild(_frame);
                _frame.GlobalPosition = _camera.GlobalPosition;
                ConstrainFrameToViewport(false);
            }
        }
        else if (IsFramingActive)
        {
            _framingRetained |= _frame.IsDetected;
            if (!_framingRetained)
                RemoveFrame();
        }
        if (_photoButton != null)
        {
            _photoButton.Visible = !IsNotebookOpen && IsFramingActive && !IsPhotographing;
            _noteIcon.Visible = !IsNotebookOpen && !IsFramingHeld && !IsPhotographing;
            _joystick.Visible = !IsNotebookOpen;
        }
    }

    private void RemoveFrame()
    {
        _framingRetained = false;
        if (GodotObject.IsInstanceValid(_frame))
            _frame.QueueFree();
        _frame = null;
    }

    public void CancelFraming()
    {
        _joystickHeld = false;
        _keyboardHeld = false;
        _joystickPointer = -2;
        RemoveFrame();
        _joystick?.Notification((int)Control.NotificationResized);
        ReleaseDirections();
        UpdateFraming();
    }

    public async void TakePhotograph()
    {
        if (!IsPhotographing && !IsNotebookOpen && IsFramingActive)
            await _photoFlow.PhotographAsync(_frame);
    }

    private void OnPhotoBusyChanged(bool busy)
    {
        _dragDetection.SetInteractionEnabled(!busy);
        _bar.SetInteractionEnabled(!busy);
        // Keep the original gesture alive so the native joystick receives its release.
        _joystick.Modulate = new Color(1, 1, 1, busy ? 0 : 1);
        _photoButton.Hide();
        _noteIcon.Hide();
        if (busy)
        {
            _bar.Hide();
            _frame?.Hide();
            ReleaseDirections();
        }
        else
        {
            _frame?.Show();
            UpdateFraming();
        }
    }

    private static void ReleaseDirections()
    {
        foreach (string action in new[] { "ui_left", "ui_right", "ui_up", "ui_down" })
            Input.ActionRelease(action);
    }

    public override void _Input(InputEvent input)
    {
        if (input is InputEventMouseButton mouse && mouse.ButtonIndex == MouseButton.Left)
        {
            if (mouse.Pressed)
                _lastPointer = -1;
            else if (_joystickHeld && _joystickPointer == -1)
                JoystickReleased(Vector2.Zero);
        }
        else if (input is InputEventScreenTouch touch)
        {
            if (touch.Pressed)
                _lastPointer = touch.Index;
            else if (_joystickHeld && _joystickPointer == touch.Index)
                JoystickReleased(Vector2.Zero);
        }
        if (input is InputEventKey key && !key.Echo)
        {
            if (IsNotebookOpen)
            {
                if (key.Pressed && key.PhysicalKeycode == Key.Escape)
                {
                    CloseNotebook();
                    GetViewport().SetInputAsHandled();
                }
                return;
            }
            if (key.PhysicalKeycode == Key.Space)
            {
                _keyboardHeld = key.Pressed;
                UpdateFraming();
                GetViewport().SetInputAsHandled();
            }
            else if (key.Pressed && (key.PhysicalKeycode == Key.Enter || key.PhysicalKeycode == Key.KpEnter))
            {
                TakePhotograph();
                GetViewport().SetInputAsHandled();
            }
            else if (key.Pressed && key.PhysicalKeycode == Key.Escape && IsFramingActive)
            {
                CancelFraming();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    public override void _Notification(int what)
    {
        if (what != NotificationWMWindowFocusOut)
            return;
        CancelFraming();
    }

    public override void _Ready()
    {
        _camera = GetNode<Camera2D>("Camera2D");
        var ui = GetNode<Control>("UILayer/AspectRatioContainer/UIRoot/DesignRoot");
        _joystick = ui.GetNode<VirtualJoystick>("VirtualJoystick");
        _dragDetection = ui.GetNode<DragDetection>("DragDetection");
        _photoButton = ui.GetNode<TextureButton>("PhotoButton");
        _noteIcon = ui.GetNode<TextureButton>("NoteIcon");
        _joystick.Pressed += JoystickPressed;
        _joystick.Released += JoystickReleased;
        _photoButton.Pressed += TakePhotograph;
        _noteIcon.Pressed += () => OpenNotebook();
        _dragDetection.DragMovedX += MoveCamera;

        CurrentLevel = LevelScene.Instantiate<Level>();
        CurrentLevel.Name = "Level";
        AddChild(CurrentLevel);
        _background = CurrentLevel.Background;
        UpdateCameraBounds();
        GetViewport().SizeChanged += UpdateCameraBounds;

        _bar = new Bar { Name = "MaterialBar", ZIndex = 10 };
        ui.AddChild(_bar);
        _bar.Initialize(Inventory);
        Notebook = NotebookScene.Instantiate<NotebookUI>();
        Notebook.ZIndex = 5;
        Notebook.Visible = false;
        ui.AddChild(Notebook);
        // GUI hit testing follows scene order: keep the shared card bar above the notebook shell.
        ui.MoveChild(_bar, -1);
        Notebook.BindLevel(CurrentLevel);
        Notebook.CloseRequested += CloseNotebook;
        _bar.MaterialDropTarget = point => IsNotebookOpen && Notebook.Board.ContainsCanvasPoint(point);
        _bar.MaterialDropReceiver = (entry, point) => IsNotebookOpen
            && Notebook.Board.TryPlaceMaterial(CurrentLevel, entry, point, out _);
        _bar.MaterialPreviewSize = entry => Notebook.Board.GetMaterialPreviewSize(entry);
        _bar.MaterialPreviewTexture = entry => Notebook.Board.GetMaterialPreviewTexture(entry);
        var panel = new PhotoPanel { Name = "PhotoPanel" };
        ui.AddChild(panel);
        var capture = new PhotoCapture { Name = "PhotoCapture" };
        AddChild(capture);
        capture.Initialize(GetViewport());
        _photoFlow = new PhotoFlow { Name = "PhotoFlow" };
        AddChild(_photoFlow);
        _photoFlow.Initialize(capture, panel, _bar, Inventory,
            GetNode<ColorRect>("UILayer/ScreenShade"), GetNode<Control>("UILayer/InputBlocker"));
        _photoFlow.BusyChanged += OnPhotoBusyChanged;
        UpdateFraming();
    }

    public override void _PhysicsProcess(double delta)
    {
        Vector2 worldViewportSize = GetViewport().GetVisibleRect().Size / _camera.Zoom;
        if (!_worldViewportSize.IsEqualApprox(worldViewportSize))
            UpdateCameraBounds();
        if (!IsFramingActive || IsPhotographing || IsNotebookOpen)
            return;
        if (IsFramingHeld)
            _frame.Move(Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down"));
        ConstrainFrameToViewport(IsFramingHeld);
        if (!IsFramingHeld)
            _frame.RefreshDetection();
    }
}
