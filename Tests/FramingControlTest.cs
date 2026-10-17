using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;

/// <summary>Headless checks of the real Main's retained viewfinder and complete visible-frame bounds.</summary>
public partial class FramingControlTest : Node
{
    private const string UiPath = "UILayer/AspectRatioContainer/UIRoot/DesignRoot";
    private const float Epsilon = 0.75f;
    private Main _main;
    private Camera2D _camera;
    private DragDetection _drag;
    private TextureButton _photoButton;
    private int _checks;
    private bool _finished;
    private string _stage = "startup";

    public override async void _Ready()
    {
        GetTree().CreateTimer(60).Timeout += () =>
        {
            if (!_finished) { GD.PushError($"Framing control timed out during {_stage}."); GetTree().Quit(1); }
        };
        int exitCode = 0;
        try
        {
            _main = GetNode<Main>("Main");
            _camera = _main.GetNode<Camera2D>("Camera2D");
            _drag = _main.GetNode<DragDetection>($"{UiPath}/DragDetection");
            _photoButton = _main.GetNode<TextureButton>($"{UiPath}/PhotoButton");
            await PhysicsFrames(3);
            await CheckReleaseAndRetention();
            await CheckHorizontalFollowing();
            await CheckVerticalBounds();
            await CheckBoundsWithoutCameraScrolling();
            await CheckResizeAndZoom();
            await CheckRetainedBlankDrag();
            await CheckCancellationAndNotebook();
            await CheckRetainedPhotographCommand();
            GD.Print($"Framing control: {_checks} checks passed.");
        }
        catch (Exception exception)
        {
            exitCode = 1;
            GD.PushError($"Framing control failed during {_stage}: {exception}");
        }
        finally
        {
            _finished = true;
            ReleaseDirections();
            if (GodotObject.IsInstanceValid(_main))
            {
                _main.CancelFraming();
                // Scene exit cancels the final photo command before a renderer-dependent capture is needed.
                _main.Free();
            }
        }
        GetTree().Quit(exitCode);
    }

    private async Task CheckReleaseAndRetention()
    {
        _stage = "release and retained focus";
        Frame unfocused = await BeginFreshHold();
        Check(!unfocused.IsDetected, "An empty central garden position starts without focus.");
        Sprite2D detected = unfocused.GetNode<Sprite2D>("Detected");
        Sprite2D undetected = unfocused.GetNode<Sprite2D>("Undetected");
        Rect2 visibleBounds = TransformRect(detected.GetRect(), detected.GlobalTransform)
            .Merge(TransformRect(undetected.GetRect(), undetected.GlobalTransform));
        Check(Near(unfocused.WorldBounds.Position, visibleBounds.Position) && Near(unfocused.WorldBounds.Size, visibleBounds.Size),
            "Movement bounds cover the actual visible artwork regardless of the capture area's dimensions.");
        KeyInput(Key.Space, false);
        await PhysicsFrames(2);
        Check(!_main.IsFramingHeld && !_main.IsFramingActive && FindFrame() == null && !_photoButton.Visible,
            "Releasing an unfocused frame removes it and hides the photograph action.");

        Frame retained = await RetainFocusedFrame();
        Vector2 position = retained.GlobalPosition;
        Vector2 cameraPosition = _camera.GlobalPosition;
        Input.ActionPress("ui_right");
        await PhysicsFrames(4);
        ReleaseDirections();
        Check(Near(retained.GlobalPosition, position) && Near(_camera.GlobalPosition, cameraPosition),
            "A retained frame stays frozen when direction input arrives without an active hold.");
        AssertInsideViewport("retained frame");
        KeyInput(Key.Space, true);
        await PhysicsFrames(2);
        Check(_main.IsFramingHeld && FindFrame() == retained,
            "Beginning another keyboard hold resumes the existing retained frame.");
        KeyInput(Key.Space, false);
        await PhysicsFrames(2);
        Check(_main.IsFramingActive && !_main.IsFramingHeld && FindFrame() == retained,
            "Releasing the resumed focused frame retains the same instance again.");
        _main.CancelFraming();
        await PhysicsFrames(2);
        Check(!_main.IsFramingActive && FindFrame() == null, "Explicit cancellation removes a retained frame.");
    }

    private async Task CheckHorizontalFollowing()
    {
        _stage = "horizontal follow and garden limits";
        Frame frame = await BeginFreshHold();
        float bound = CameraXBound();
        Check(bound > Epsilon, "The real garden provides horizontal camera space for following.");
        Input.ActionPress("ui_right");
        await PhysicsUntil(() => _camera.GlobalPosition.X >= bound - Epsilon, 600, "right garden limit");
        await PhysicsFrames(8);
        Vector2 rightCamera = _camera.GlobalPosition;
        Vector2 rightFrame = frame.GlobalPosition;
        await PhysicsFrames(8);
        Check(Math.Abs(_camera.GlobalPosition.X - bound) <= Epsilon && Near(_camera.GlobalPosition, rightCamera)
            && Near(frame.GlobalPosition, rightFrame),
            "Rightward movement scrolls the camera to the garden limit, then stops the whole frame.");
        AssertInsideViewport("right garden limit");
        Check(Math.Abs(ScreenBounds(frame).End.X - GetViewport().GetVisibleRect().End.X) <= Epsilon,
            "The complete visible right edge reaches the viewport edge.");
        ReleaseDirections();
        Input.ActionPress("ui_left");
        await PhysicsUntil(() => _camera.GlobalPosition.X <= -bound + Epsilon, 600, "left garden limit");
        await PhysicsFrames(8);
        Vector2 leftCamera = _camera.GlobalPosition;
        Vector2 leftFrame = frame.GlobalPosition;
        await PhysicsFrames(8);
        Check(Math.Abs(_camera.GlobalPosition.X + bound) <= Epsilon && Near(_camera.GlobalPosition, leftCamera)
            && Near(frame.GlobalPosition, leftFrame),
            "Leftward movement also scrolls to the garden limit and cannot push the frame offscreen.");
        AssertInsideViewport("left garden limit");
        Check(Math.Abs(ScreenBounds(frame).Position.X - GetViewport().GetVisibleRect().Position.X) <= Epsilon,
            "The complete visible left edge reaches the viewport edge.");
        ReleaseDirections();
        _main.CancelFraming();
    }

    private async Task CheckVerticalBounds()
    {
        _stage = "vertical frame limits";
        Frame frame = await BeginFreshHold();
        Vector2 cameraPosition = _camera.GlobalPosition;
        Input.ActionPress("ui_up");
        await PhysicsUntil(() => ScreenBounds(frame).Position.Y <= GetViewport().GetVisibleRect().Position.Y + Epsilon,
            200, "top viewport edge");
        await PhysicsFrames(8);
        Vector2 topPosition = frame.GlobalPosition;
        await PhysicsFrames(8);
        Check(Near(frame.GlobalPosition, topPosition) && Near(_camera.GlobalPosition, cameraPosition),
            "Upward input stops the complete visible frame at the top edge without moving the camera.");
        AssertInsideViewport("top viewport edge");
        ReleaseDirections();
        Input.ActionPress("ui_down");
        await PhysicsUntil(() => ScreenBounds(frame).End.Y >= GetViewport().GetVisibleRect().End.Y - Epsilon,
            250, "bottom viewport edge");
        await PhysicsFrames(8);
        Vector2 bottomPosition = frame.GlobalPosition;
        await PhysicsFrames(8);
        Check(Near(frame.GlobalPosition, bottomPosition) && Near(_camera.GlobalPosition, cameraPosition),
            "Downward input stops the complete frame at the bottom edge without moving the camera.");
        AssertInsideViewport("bottom viewport edge");
        ReleaseDirections();
        _main.CancelFraming();
    }

    private async Task CheckBoundsWithoutCameraScrolling()
    {
        _stage = "frame bounds when the garden cannot scroll";
        Vector2I originalSize = GetWindow().Size;
        Vector2 originalZoom = _camera.Zoom;
        float originalAspect = ViewportAspect();
        Vector2 textureSize = _main.CurrentLevel.Background.BackgroundTexture.GetSize();
        float wideAspect = Mathf.Max(4800f / 900, textureSize.X / textureSize.Y * 1.25f);
        var wideSize = new Vector2I((int)Math.Ceiling(wideAspect * 900), 900);
        try
        {
            _main.CancelFraming();
            _camera.Zoom = Vector2.One;
            GetWindow().Size = wideSize;
            await PhysicsFrames(4);
            AssertWindowAndViewportSize(wideSize, "non-scrolling wide viewport");
            Check(Math.Abs(ViewportAspect() - originalAspect) > 0.01f,
                "The non-scrolling fixture changes the actual viewport aspect before testing frame limits.");
            Check(CameraXBound() <= Epsilon, "The wide viewport leaves no horizontal garden space for camera scrolling.");
            Frame frame = await BeginFreshHold();
            Rect2 viewport = GetViewport().GetVisibleRect();
            foreach (bool moveRight in new[] { true, false })
            {
                Vector2 outside = new(moveRight ? viewport.End.X + 50 : viewport.Position.X - 50, viewport.GetCenter().Y);
                frame.GlobalPosition = GetViewport().GetCanvasTransform().AffineInverse() * outside;
                Input.ActionPress(moveRight ? "ui_right" : "ui_left");
                await PhysicsFrames(4);
                ReleaseDirections();
                AssertInsideViewport(moveRight ? "right edge without camera scrolling" : "left edge without camera scrolling");
                Check(Math.Abs(_camera.GlobalPosition.X) <= Epsilon && CameraXBound() <= Epsilon,
                    "An outward directional hold clamps the whole frame while the non-scrolling camera remains at zero.");
                Rect2 frameRect = ScreenBounds(frame);
                float edge = moveRight ? frameRect.End.X : frameRect.Position.X;
                float expectedEdge = moveRight ? viewport.End.X : viewport.Position.X;
                Check(Math.Abs(edge - expectedEdge) <= Epsilon,
                    "A non-scrolling garden still lets the visible frame reach either screen edge without crossing it.");
            }
        }
        finally
        {
            _main.CancelFraming();
            _camera.Zoom = originalZoom;
            GetWindow().Size = originalSize;
            await PhysicsFrames(3);
        }
    }

    private async Task CheckResizeAndZoom()
    {
        _stage = "viewport resize and camera zoom";
        Frame frame = await RetainFocusedFrame();
        Vector2I originalSize = GetWindow().Size;
        Vector2 originalZoom = _camera.Zoom;
        try
        {
            foreach (Vector2I size in new[] { new Vector2I(1200, 900), new Vector2I(1800, 800) })
            {
                float previousAspect = ViewportAspect();
                GetWindow().Size = size;
                await PhysicsFrames(4);
                AssertWindowAndViewportSize(size, "retained-frame resize");
                Check(Math.Abs(ViewportAspect() - previousAspect) > 0.01f,
                    "The resize fixture changes the actual viewport aspect before checking retained-frame boundaries.");
                Check(_main.IsFramingActive && !_main.IsFramingHeld && FindFrame() == frame,
                    "Viewport resizing preserves the retained frame instead of restarting a hold.");
                AssertInsideViewport($"viewport resized to {size}");
                Check(Math.Abs(_camera.GlobalPosition.X) <= CameraXBound() + Epsilon,
                    "Viewport resizing keeps the camera inside the recomputed garden bounds.");
            }
            _camera.Zoom = new Vector2(1.4f, 1.3f);
            await PhysicsFrames(4);
            AssertInsideViewport("nonuniform camera zoom");
            Check(_main.IsFramingActive && !_main.IsFramingHeld && Math.Abs(_camera.GlobalPosition.X) <= CameraXBound() + Epsilon,
                "Changing camera zoom maintains retained-frame and garden boundaries without a resize event.");
            _drag.EmitSignal(DragDetection.SignalName.DragMovedX, -1000000f);
            await PhysicsFrames(2);
            AssertInsideViewport("zoomed drag to the right garden limit");
            Check(Math.Abs(_camera.GlobalPosition.X - CameraXBound()) <= Epsilon,
                "A blank drag uses the newly computed zoom-dependent right garden limit.");
            _camera.Zoom = new Vector2(3, 1.3f);
            await PhysicsFrames(3);
            _drag.EmitSignal(DragDetection.SignalName.DragMovedX, -1000000f);
            await PhysicsFrames(4);
            AssertInsideViewport("retained frame clamped away from its focus target");
            Check(_main.IsFramingActive && !_main.IsFramingHeld && !frame.IsDetected,
                "A retained frame displaced away from its target by zoom and garden dragging clears its stale focused appearance.");
        }
        finally
        {
            _camera.Zoom = originalZoom;
            GetWindow().Size = originalSize;
            _main.CancelFraming();
            await PhysicsFrames(3);
        }
    }

    private async Task CheckRetainedBlankDrag()
    {
        _stage = "blank garden drag with a retained frame";
        Frame frame = await RetainFocusedFrame();
        foreach (float delta in new[] { 1000000f, -1000000f })
        {
            _drag.EmitSignal(DragDetection.SignalName.DragMovedX, delta);
            await PhysicsFrames(2);
            Check(_main.IsFramingActive && !_main.IsFramingHeld && FindFrame() == frame,
                "Dragging the blank garden keeps the existing retained frame active.");
            AssertInsideViewport("retained frame after blank garden drag");
            Check(Math.Abs(Math.Abs(_camera.GlobalPosition.X) - CameraXBound()) <= Epsilon,
                "An extreme blank drag stops at the garden edge.");
        }
        _main.CancelFraming();
    }

    private async Task CheckCancellationAndNotebook()
    {
        _stage = "focus loss, Escape, and notebook lifecycle";
        await RetainFocusedFrame();
        KeyInput(Key.Escape, true);
        await PhysicsFrames(2);
        Check(!_main.IsFramingActive && !_main.IsFramingHeld && FindFrame() == null,
            "Escape explicitly clears retained framing.");
        await BeginFreshHold();
        Input.ActionPress("ui_up");
        _main.Notification((int)Node.NotificationWMWindowFocusOut);
        await PhysicsFrames(2);
        Check(!_main.IsFramingHeld && !_main.IsFramingActive && !Input.IsActionPressed("ui_up"),
            "Losing focus cancels an active hold and releases direction input.");
        await RetainFocusedFrame();
        _main.Notification((int)Node.NotificationWMWindowFocusOut);
        await PhysicsFrames(2);
        Check(!_main.IsFramingActive && FindFrame() == null, "Losing focus also clears a retained frame.");
        await RetainFocusedFrame();
        Check(_main.OpenNotebook(), "The notebook can open after releasing a focused frame.");
        await PhysicsFrames(2);
        Check(_main.IsNotebookOpen && !_main.IsFramingActive && FindFrame() == null && !_photoButton.Visible,
            "Opening the notebook removes retained framing and its photograph action.");
        KeyInput(Key.Space, true);
        Check(!_main.IsFramingHeld && !_main.IsFramingActive, "Notebook input cannot create a garden frame.");
        KeyInput(Key.Space, false);
        _main.CloseNotebook();
        await PhysicsFrames(2);
        Check(!_main.IsNotebookOpen && !_main.IsFramingActive && !_photoButton.Visible,
            "Returning from the notebook does not resurrect the cancelled frame.");
    }

    private async Task CheckRetainedPhotographCommand()
    {
        _stage = "photograph command after release";
        await RetainFocusedFrame();
        Check(_photoButton.Visible && !_main.IsFramingHeld && !_main.IsPhotographing,
            "The photograph action remains available after a focused keyboard release.");
        // Acceptance is synchronous; rendered capture and material insertion are covered by PhotoIntegrationTest.
        KeyInput(Key.Enter, true);
        Check(_main.IsPhotographing, "Enter begins the real photo flow from a retained frame without holding Space.");
    }

    private async Task<Frame> BeginFreshHold()
    {
        _main.CancelFraming();
        _camera.GlobalPosition = Vector2.Zero;
        _camera.ForceUpdateScroll();
        await PhysicsFrames(2);
        KeyInput(Key.Space, true);
        await PhysicsFrames(2);
        Frame frame = FindFrame();
        Check(_main.IsFramingHeld && _main.IsFramingActive && frame != null,
            "Space creates one active frame through the real Main input handler.");
        AssertInsideViewport("new frame");
        return frame;
    }

    private async Task<Frame> RetainFocusedFrame()
    {
        Frame frame = await BeginFreshHold();
        frame.GlobalPosition = _main.CurrentLevel.GetPatterns()[0].GlobalPosition;
        await PhysicsFrames(4);
        Check(frame.IsDetected, "The actual Level prototype becomes detected through physics overlaps.");
        KeyInput(Key.Space, false);
        await PhysicsFrames(2);
        Check(!_main.IsFramingHeld && _main.IsFramingActive && FindFrame() == frame && _photoButton.Visible,
            "A detected frame remains active and photographable after releasing Space.");
        return frame;
    }

    private Frame FindFrame() => _main.GetChildren().OfType<Frame>().SingleOrDefault(frame => !frame.IsQueuedForDeletion());
    private float CameraXBound() => _main.CurrentLevel.Background.GetCameraXBound(GetViewport().GetVisibleRect().Size.X / _camera.Zoom.X);
    private void KeyInput(Key key, bool pressed) => _main._Input(new InputEventKey { PhysicalKeycode = key, Pressed = pressed });
    private Rect2 ScreenBounds(Frame frame) => TransformRect(frame.WorldBounds, GetViewport().GetCanvasTransform());
    private float ViewportAspect()
    {
        Vector2 size = GetViewport().GetVisibleRect().Size;
        return size.X / size.Y;
    }

    private void AssertWindowAndViewportSize(Vector2I requested, string context)
    {
        Check(GetWindow().Size == requested, $"{context}: the window reaches the requested size {requested}.");
        float requestedAspect = requested.X / (float)requested.Y;
        Check(Math.Abs(ViewportAspect() - requestedAspect) < 0.002f,
            $"{context}: actual viewport aspect {ViewportAspect()} matches requested window aspect {requestedAspect}.");
    }

    private void AssertInsideViewport(string context)
    {
        Frame frame = FindFrame();
        Check(frame != null && _main.IsFramingActive, $"{context}: a valid frame exists.");
        Rect2 viewport = GetViewport().GetVisibleRect();
        Rect2 bounds = ScreenBounds(frame);
        Check(bounds.Position.X >= viewport.Position.X - Epsilon && bounds.Position.Y >= viewport.Position.Y - Epsilon
            && bounds.End.X <= viewport.End.X + Epsilon && bounds.End.Y <= viewport.End.Y + Epsilon,
            $"{context}: complete frame {bounds} stays inside viewport {viewport}.");
    }

    private async Task PhysicsUntil(Func<bool> condition, int maximumFrames, string context)
    {
        for (int index = 0; index < maximumFrames; index++)
        {
            await PhysicsFrames(1);
            AssertInsideViewport(context);
            if (condition()) return;
        }
        throw new TimeoutException($"{context} was not reached after {maximumFrames} physics frames; camera={_camera.GlobalPosition}.");
    }

    private async Task PhysicsFrames(int count)
    {
        for (int index = 0; index < count; index++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (GodotObject.IsInstanceValid(_camera)) _camera.ForceUpdateScroll();
        }
    }

    private static Rect2 TransformRect(Rect2 rect, Transform2D transform)
    {
        Rect2 transformed = new(transform * rect.Position, Vector2.Zero);
        transformed = transformed.Expand(transform * new Vector2(rect.End.X, rect.Position.Y));
        transformed = transformed.Expand(transform * rect.End);
        return transformed.Expand(transform * new Vector2(rect.Position.X, rect.End.Y));
    }

    private static bool Near(Vector2 actual, Vector2 expected) => actual.DistanceTo(expected) <= Epsilon;
    private static void ReleaseDirections()
    {
        foreach (string action in new[] { "ui_left", "ui_right", "ui_up", "ui_down" }) Input.ActionRelease(action);
    }
    private void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }
}
