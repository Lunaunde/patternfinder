using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>Run this scene with a real renderer to exercise the complete garden.</summary>
public partial class PhotoIntegrationTest : Node
{
    private const string UiPath = "UILayer/AspectRatioContainer/UIRoot/DesignRoot";
    private const string ScreenshotDirectory = "res://Output/shots";
    private const ulong DeadlineMilliseconds = 15000;

    private Main _main;
    private PatternObject _pattern;
    private Camera2D _camera;
    private PhotoCapture _capture;
    private PhotoPanel _photoPanel;
    private Bar _bar;
    private Control _designRoot;
    private Vector2I _verifiedRenderSize;
    private readonly Dictionary<string, MaterialEntry> _configuredCandidates = new(StringComparer.Ordinal);
    private MaterialEntry _firstCollected;
    private int _checks;
    private string _stage = "startup";

    public override async void _Ready()
    {
        try
        {
            await RunChecks();
            GD.Print($"Photo integration: {_checks} checks passed. Screenshots: {ScreenshotDirectory}");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"Photo integration failed during {_stage}: {exception}");
            try
            {
                await ScreenshotAsync("chatgpt-test-failure");
            }
            catch (Exception screenshotException)
            {
                GD.PushWarning($"Failure screenshot unavailable: {screenshotException.Message}");
            }
            GetTree().Quit(1);
        }
    }

    private async Task RunChecks()
    {
        Check(DisplayServer.GetName() != "headless", "This integration scene needs a real rendering window.");
        Check(DirAccess.MakeDirRecursiveAbsolute(ScreenshotDirectory) == Error.Ok, "Screenshot output directory can be created.");
        _main = GetNode<Main>("Main");
        _pattern = _main.CurrentLevel.GetPatterns()[0];
        _camera = _main.GetNode<Camera2D>("Camera2D");
        _capture = _main.GetNode<PhotoCapture>("PhotoCapture");
        _designRoot = _main.GetNode<Control>(UiPath);
        _photoPanel = _designRoot.GetNode<PhotoPanel>("PhotoPanel");
        _bar = _designRoot.GetNode<Bar>("MaterialBar");
        Check(_pattern.Variants.Count == 1 && _pattern.Variants[0].Scale.DistanceTo(Vector2.One * 0.25f) < 0.0001f,
            "The current authored honeysuckle supplies one motif with its configured quarter-size scale.");
        foreach (PatternVariant variant in _pattern.Variants)
            _configuredCandidates.Add(variant.MaterialId,
                new MaterialEntry(variant.MaterialId, variant.MaterialName, variant.MaterialTexture, variant.Scale));
        var extra = new MaterialEntry("integration-second-variant", "测试第二纹样", MakeFixtureTexture(), new Vector2(-0.18f, 0.31f));
        _configuredCandidates.Add(extra.MaterialId, extra);
        Check(_configuredCandidates.Count == 2 && _configuredCandidates.Values.All(entry => entry.Texture != null),
            "A test-only second candidate keeps multi-variant coverage without changing the single-motif product scene.");

        GetWindow().Mode = Window.ModeEnum.Windowed;
        await ResizeWindowAsync(new Vector2I(1080, 480));
        await PositionFrameOnPatternAsync();
        await CheckIndependentCaptureAsync();
        await ScreenshotAsync("chatgpt-01-framing");

        await CheckNewMaterialAndReleaseAsync();
        await CheckDuplicateAsync();
        await AdvanceToFourthRecognitionAsync();
        await CheckFailureAsync();
        await CheckFifthRecognitionAsync();
        await CheckFullInventoryAsync();
        SetFramingHeld(false);
        await PhysicsFramesAsync(2);
        Check(FindFrame() == null, "Releasing after all captures removes the viewfinder.");
        await CheckMainCardGesturesAsync();
        SetFramingHeld(false);
        await PhysicsFramesAsync(2);

        _stage = "window aspect ratios";
        foreach (var sample in new[]
        {
            (Size: new Vector2I(1080, 810), Name: "4x3"),
            (Size: new Vector2I(1260, 540), Name: "21x9"),
            (Size: new Vector2I(480, 800), Name: "portrait")
        })
        {
            await ResizeWindowAsync(sample.Size);
            CheckCenteredDesign(sample.Name);
            await ScreenshotAsync($"chatgpt-layout-{sample.Name}");
        }
    }

    private async Task CheckIndependentCaptureAsync()
    {
        _stage = "independent world capture";
        Frame frame = FindFrame();
        Check(frame != null, "Holding Space creates the real viewfinder.");
        Check(frame.TryGetCaptureTarget(out var target, out float rate) && target == _pattern && rate >= Config.PHOTO_SUCCESS_RATE,
            "The real viewfinder recognizes the configured plant at its center.");
        CheckLayerTwo(frame);
        Check(_capture.CanvasCullMask == 1, "The photograph only sees world visibility layer 1.");
        Check(_capture.World2D == GetViewport().FindWorld2D(), "The capture viewport shares the real garden World2D.");

        Rect2 rectangle = frame.CaptureWorldRect;
        Vector2 cameraPosition = _camera.GlobalPosition;
        Transform2D canvasTransform = GetViewport().CanvasTransform;
        frame.Hide();
        Texture2D baseline = await _capture.CaptureAsync(rectangle);
        Image baselineImage = baseline.GetImage();
        byte[] frozenData = baselineImage.GetData();
        Check(!baselineImage.IsEmpty() && baselineImage.GetWidth() == 489 && baselineImage.GetHeight() == 489,
            "The actual renderer produces a 489 by 489 frozen photograph.");
        Check(HasColourVariation(baselineImage), "The photograph contains garden image detail rather than a blank render target.");

        var overlayLayer = new CanvasLayer { Name = "IntegrationUiOverlay", Layer = 100 };
        AddChild(overlayLayer);
        var overlay = new ColorRect
        {
            Color = new Color(1, 0, 1),
            Size = GetViewport().GetVisibleRect().Size,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        overlayLayer.AddChild(overlay);
        frame.Show();
        Texture2D withFrameAndUi = await _capture.CaptureAsync(rectangle);
        Check(SamePixels(frozenData, withFrameAndUi.GetImage().GetData()),
            "Showing the actual Frame and a fullscreen CanvasLayer overlay does not contaminate the photograph.");
        overlayLayer.QueueFree();
        await ProcessFramesAsync(2);

        await _capture.CaptureAsync(new Rect2(rectangle.Position + new Vector2(700, 0), rectangle.Size));
        Check(SamePixels(frozenData, baseline.GetImage().GetData()), "A later capture does not overwrite the frozen first photograph.");
        Check(_camera.GlobalPosition.DistanceTo(cameraPosition) < 0.001f,
            "The independent photograph camera does not move the main camera.");
        Check(SameTransform(GetViewport().CanvasTransform, canvasTransform),
            "The independent photograph camera does not alter the main viewport canvas transform.");
        Check(GetViewport().GetCamera2D() == _camera && _capture.GetCamera2D() != _camera,
            "The main and capture viewports keep separate active cameras.");
        Check(baselineImage.SavePng($"{ScreenshotDirectory}/chatgpt-capture.png") == Error.Ok,
            "The frozen photograph can be saved for visual inspection.");
    }

    private async Task CheckNewMaterialAndReleaseAsync()
    {
        _stage = "successful new material and release while busy";
        Check(_main.Inventory.Count == 0, "The real Main starts with an empty inventory.");
        Frame frame = FindFrame();
        Vector2 cameraPosition = _camera.GlobalPosition;
        Vector2 framePosition = frame.GlobalPosition;
        _main.TakePhotograph();
        Check(_main.IsPhotographing, "Taking a photograph enters the busy state immediately.");
        Check(_pattern.SuccessfulRecognitionCount == 1, "One recognized photograph selects exactly one variant.");
        _main.TakePhotograph();
        Check(_main.Inventory.Count == 0 && _pattern.SuccessfulRecognitionCount == 1,
            "A second photograph while busy neither commits a material nor spends a guarantee attempt.");
        // Resources may be edited while presentation runs; this photograph already has its own values.
        foreach (PatternVariant variant in _pattern.Variants)
        {
            variant.MaterialId = "changed-after-shutter";
            variant.MaterialName = "Changed after shutter";
            variant.MaterialTexture = null;
            variant.Scale = new Vector2(6, 9);
        }
        _pattern.Variants = new Godot.Collections.Array<PatternVariant>();
        Check(!_designRoot.GetNode<TextureButton>("PhotoButton").Visible
            && !_designRoot.GetNode<Control>("NoteIcon").Visible
            && _main.GetNode<Control>("UILayer/InputBlocker").Visible,
            "Photograph presentation hides the buttons and enables the input blocker.");
        Check(!_bar.Visible && !frame.Visible
            && _designRoot.GetNode<VirtualJoystick>("VirtualJoystick").Modulate.A == 0,
            "Photograph presentation hides the drawer, viewfinder, and joystick visually.");

        _designRoot.GetNode<DragDetection>("DragDetection").EmitSignal(DragDetection.SignalName.DragMovedX, 100f);
        Input.ActionPress("ui_right", 1);
        await PhysicsFramesAsync(2);
        Input.ActionRelease("ui_right");
        Check(_camera.GlobalPosition.DistanceTo(cameraPosition) < 0.001f
            && frame.GlobalPosition.DistanceTo(framePosition) < 0.001f,
            "Camera dragging and directional input cannot move the garden or viewfinder while busy.");

        await WaitUntilAsync(() => PhotographCard().Visible && PhotographCard().Position.Y >= 90,
            "The real photograph reaches its central resting position.");
        await ScreenshotAsync("chatgpt-02-photograph");
        SetFramingHeld(false);
        await PhysicsFramesAsync(2);
        Check(!_main.IsFramingHeld && FindFrame() == null, "Space release during the animation removes the old Frame safely.");
        _main.TakePhotograph();
        Check(_main.IsPhotographing && _main.Inventory.Count == 0,
            "Another photograph remains rejected after the old Frame is released during presentation.");
        await WaitForRevealAsync();
        await ScreenshotAsync("chatgpt-03-reveal");
        await WaitUntilAsync(() => PhotographCard().Visible && PhotographCard().Scale.X < 0.95f,
            "A new material shrinks while flying toward the drawer.");
        await ScreenshotAsync("chatgpt-04-flying");
        await WaitForIdleAsync();
        Check(_main.Inventory.Count == 1 && _configuredCandidates.ContainsKey(_main.Inventory.Entries[0].MaterialId),
            "The real authored motif is stored once despite repeated attempts and configuration changes.");
        _firstCollected = _main.Inventory.Entries[0];
        MaterialEntry expected = _configuredCandidates[_firstCollected.MaterialId];
        Check(_firstCollected.DisplayName == expected.DisplayName && _firstCollected.Texture == expected.Texture
            && _firstCollected.Scale.DistanceTo(expected.Scale) < 0.0001f
            && _firstCollected.Scale.DistanceTo(Vector2.One * 0.25f) < 0.0001f,
            "The stored ID, name, art and quarter-size scale match the real candidate frozen before configuration edits.");
        Check(FindFrame() == null && !_designRoot.GetNode<TextureButton>("PhotoButton").Visible
            && _designRoot.GetNode<Control>("NoteIcon").Visible,
            "Finishing after a release restores observation mode without resurrecting the Frame.");
        Check(!_main.GetNode<Control>("UILayer/InputBlocker").Visible && _bar.Visible,
            "Finishing clears the blocker and restores the material drawer.");
        Check(CountLabel().Text == "1/14", "The drawer displays the updated inventory count.");
        await ScreenshotAsync("chatgpt-05-stored");
    }

    private async Task CheckDuplicateAsync()
    {
        _stage = "duplicate material";
        _pattern.Variants = new Godot.Collections.Array<PatternVariant> { Candidate(_firstCollected) };
        await PositionFrameOnPatternAsync();
        _main.TakePhotograph();
        Check(_main.IsPhotographing, "The same plant can be photographed again.");
        await WaitForRevealAsync();
        await ScreenshotAsync("chatgpt-06-duplicate");
        await WaitForIdleAsync();
        Check(_main.Inventory.Count == 1 && _pattern.SuccessfulRecognitionCount == 2,
            "A duplicate counts as recognition but does not increase inventory occupancy.");
        Check(_main.IsFramingHeld && FindFrame() != null && FindFrame().Visible
            && _designRoot.GetNode<TextureButton>("PhotoButton").Visible,
            "Keeping Space held through presentation returns directly to a usable viewfinder.");
    }

    private async Task AdvanceToFourthRecognitionAsync()
    {
        _stage = "ordinary recognition before guarantee";
        for (int attempt = 3; attempt <= 4; attempt++)
        {
            await PositionFrameOnPatternAsync();
            _main.TakePhotograph();
            await WaitForIdleAsync();
            Check(_pattern.SuccessfulRecognitionCount == attempt && _main.Inventory.Count == 1,
                $"Duplicate recognition {attempt} preserves the inventory and advances the prototype counter once.");
        }
    }

    private async Task CheckFailureAsync()
    {
        _stage = "failed recognition";
        long previousRecognitions = _pattern.SuccessfulRecognitionCount;
        Frame frame = FindFrame();
        frame.GlobalPosition = _pattern.GlobalPosition + new Vector2(1100, 350);
        await PhysicsFramesAsync(2);
        Check(!frame.TryGetCaptureTarget(out _, out _), "The failure photograph has no overlapping recognizable target.");
        _main.TakePhotograph();
        Check(_main.IsPhotographing, "An empty photograph still starts the presentation flow.");
        await WaitForRevealAsync();
        await ScreenshotAsync("chatgpt-07-failure");
        await WaitForIdleAsync();
        Check(_main.Inventory.Count == 1 && CountLabel().Text == "1/14"
            && _pattern.SuccessfulRecognitionCount == previousRecognitions,
            "The scribble failure never becomes inventory or spends the next guarantee attempt.");
    }

    private async Task CheckFifthRecognitionAsync()
    {
        _stage = "fifth recognition guarantee";
        _pattern.Variants = new Godot.Collections.Array<PatternVariant>(
            _configuredCandidates.Values.Select(Candidate));
        _main.CurrentLevel.RefreshMaterials();
        await PositionFrameOnPatternAsync();
        _main.TakePhotograph();
        await WaitUntilAsync(() => RevealRadius() >= 1.19f && PhotographCard().Visible,
            "The full card artwork is visible at the end of conversion.");
        await ScreenshotAsync("chatgpt-09-second-variant-revealed");
        await WaitForIdleAsync();
        Check(_main.Inventory.Count == 2 && _pattern.SuccessfulRecognitionCount == 5
            && _configuredCandidates.Keys.All(_main.Inventory.Contains),
            "The fifth recognized photo collects the second test candidate of the same plant.");
        Check(_main.Inventory.Entries.All(entry => entry.Scale.DistanceTo(_configuredCandidates[entry.MaterialId].Scale) < 0.0001f
            && entry.Texture == _configuredCandidates[entry.MaterialId].Texture),
            "The photo pipeline preserves both authored quarter scale and the test candidate's signed nonuniform scale.");
        await ScreenshotAsync("chatgpt-10-two-variants-stored");
    }

    private async Task CheckFullInventoryAsync()
    {
        _stage = "full inventory";
        while (_main.Inventory.Count < MaterialInventory.Capacity)
        {
            int index = _main.Inventory.Count;
            Check(_main.Inventory.TryAdd($"integration-fill-{index}", $"测试素材 {index}", _firstCollected.Texture, _firstCollected.Scale),
                "Distinct fixture materials fill the remaining inventory slots.");
        }
        Godot.Collections.Array<PatternVariant> originalCandidates = _pattern.Variants;
        _pattern.Variants = new Godot.Collections.Array<PatternVariant>
        {
            new() { MaterialId = "integration-overflow", MaterialName = "Overflow", MaterialTexture = _firstCollected.Texture, Scale = _firstCollected.Scale }
        };
        try
        {
            await PositionFrameOnPatternAsync();
            _main.TakePhotograph();
            Check(_main.IsPhotographing, "A newly identified material still animates when all fourteen slots are occupied.");
            await WaitUntilAsync(() => _bar.Visible && !PhotographCard().Visible && _main.IsPhotographing,
                "The full-library response occurs after the photograph reaches the drawer.");
            await ScreenshotAsync("chatgpt-08-full");
            await WaitForIdleAsync();
            Check(_main.Inventory.Count == 14 && !_main.Inventory.Contains("integration-overflow"),
                "The full-library response preserves fourteen entries and rejects the overflow material.");
            Check(CountLabel().Text == "14/14", "The full-library counter remains fourteen of fourteen.");
        }
        finally
        {
            _pattern.Variants = originalCandidates;
        }
    }

    private static PatternVariant Candidate(MaterialEntry entry) => new()
    {
        MaterialId = entry.MaterialId, MaterialName = entry.DisplayName, MaterialTexture = entry.Texture, Scale = entry.Scale
    };

    private static Texture2D MakeFixtureTexture()
    {
        using Image image = Image.CreateEmpty(56, 40, false, Image.Format.Rgba8);
        image.Fill(new Color(0.24f, 0.57f, 0.36f));
        return ImageTexture.CreateFromImage(image);
    }

    private async Task CheckMainCardGesturesAsync()
    {
        _stage = "card dragging in the real garden";
        _bar.SetOpen(true, false);
        Vector2 cameraPosition = _camera.GlobalPosition;
        MaterialEntry selected = _main.Inventory.Entries[0];
        string[] originalOrder = _main.Inventory.Entries.Select(entry => entry.MaterialId).ToArray();
        Control zone = _bar.GetNode<Control>("DeleteZone");
        Vector2 delete = zone.GetGlobalTransformWithCanvas() * (zone.Size * new Vector2(0.5f, 0.3f));
        Vector2 blank = _designRoot.GetGlobalTransformWithCanvas() * new Vector2(1200, 220);
        Vector2 start = await BeginCardGestureAsync(selected.MaterialId);
        PushCardMotion(start, delete);
        await ProcessFramesAsync(2);
        Check(_bar.IsDeleteHovered && _main.Inventory.Count == 14,
            "The real garden shows a white deletion target without removing the held card prematurely.");
        await ScreenshotAsync("chatgpt-garden-delete-hover");
        PushCardMotion(delete, blank);
        PushCardButton(blank, false);
        await ProcessFramesAsync(2);
        Check(!_bar.IsCardDragging && !zone.Visible
            && _main.Inventory.Entries.Select(entry => entry.MaterialId).SequenceEqual(originalOrder)
            && _camera.GlobalPosition.DistanceTo(cameraPosition) < 0.001f,
            "Dropping in garden blank space restores the card and never drags the background camera.");

        start = await BeginCardGestureAsync(selected.MaterialId);
        MaterialCard second = InventoryCard(originalOrder[1]);
        Vector2 gap = second.GetGlobalTransformWithCanvas() * new Vector2(second.Size.X + 10, second.Size.Y / 2);
        PushCardMotion(start, gap);
        await ProcessFramesAsync(1);
        Check(_bar.GetNode<Control>("MaterialDrawer/Cards/InsertionMarker").Visible,
            "The real garden presents an insertion marker between cards.");
        await ScreenshotAsync("chatgpt-garden-insertion-marker");
        PushCardButton(gap, false);
        await WaitUntilAsync(() => _main.Inventory.Entries[1] == selected
            && Math.Abs(InventoryCard(selected.MaterialId).Position.X - 195) < 0.01f,
            "The real garden settles the held card after its neighbor.");
        Check(_main.Inventory.Count == 14 && _main.Inventory.Entries[0].MaterialId == originalOrder[1]
            && _main.Inventory.Entries.Skip(2).Select(entry => entry.MaterialId).SequenceEqual(originalOrder.Skip(2)),
            "Insertion ordering changes only the dragged card and preserves all other relative positions.");

        start = await BeginCardGestureAsync(selected.MaterialId);
        PushCardMotion(start, delete);
        PushCardButton(delete, false);
        await ProcessFramesAsync(2);
        Check(_main.Inventory.Count == 13 && !_main.Inventory.Contains(selected.MaterialId)
            && CountLabel().Text == "13/14", "Deleting in the real garden releases the selected material and its capacity slot.");
        await WaitUntilAsync(() => _main.Inventory.Entries.Select((entry, index) =>
            Math.Abs(InventoryCard(entry.MaterialId).Position.X - index * 195) < 0.01f).All(ready => ready),
            "Remaining real garden cards close the removed gap.");

        _pattern.Variants = new Godot.Collections.Array<PatternVariant> { Candidate(selected) };
        await PositionFrameOnPatternAsync();
        start = await BeginCardGestureAsync(_main.Inventory.Entries[0].MaterialId);
        PushCardMotion(start, delete);
        Check(_bar.IsDeleteHovered, "The photo-busy fixture is really hovering another held card over deletion.");
        _main.TakePhotograph();
        Check(_main.IsPhotographing && !_bar.IsCardDragging && !zone.Visible,
            "Starting a real photograph cancels an existing card drag and its deletion target.");
        PushCardButton(delete, false);
        await WaitForIdleAsync();
        Check(_main.Inventory.Count == 14 && _main.Inventory.Entries[0].MaterialId == selected.MaterialId
            && _main.Inventory.Entries.Skip(1).Select(entry => entry.MaterialId).SequenceEqual(originalOrder.Skip(1)),
            "A deleted motif can be photographed again at the left edge, with a stale pointer release unable to delete another card.");
        Check(_main.Inventory.Entries[0].Scale.DistanceTo(selected.Scale) < 0.0001f,
            "Deleting and rephotographing a motif preserves its signed scale metadata.");
        await ScreenshotAsync("chatgpt-garden-recollected-left");
    }

    private MaterialCard InventoryCard(string id) => _bar.GetNode<Control>("MaterialDrawer/Cards/CardContent")
        .GetChildren().OfType<MaterialCard>().Single(card => card.Entry?.MaterialId == id);

    private async Task<Vector2> BeginCardGestureAsync(string id)
    {
        // Position tolerance may be met just before the native Tween emits Finished.
        await ProcessFramesAsync(2);
        MaterialCard card = InventoryCard(id);
        Vector2 point = card.GetGlobalTransformWithCanvas() * (card.Size / 2);
        PushCardButton(point, true);
        await WaitUntilAsync(() => _bar.IsCardDragging && _bar.DraggedMaterialId == id,
            "A native mouse long press extracts the expected real garden card.");
        return point;
    }

    private void PushCardButton(Vector2 position, bool pressed) => GetViewport().PushInput(new InputEventMouseButton
    {
        Position = position, GlobalPosition = position, ButtonIndex = MouseButton.Left,
        ButtonMask = pressed ? MouseButtonMask.Left : (MouseButtonMask)0, Pressed = pressed
    }, true);

    private void PushCardMotion(Vector2 from, Vector2 to) => GetViewport().PushInput(new InputEventMouseMotion
    {
        Position = to, GlobalPosition = to, Relative = to - from, ButtonMask = MouseButtonMask.Left
    }, true);

    private async Task PositionFrameOnPatternAsync()
    {
        SetFramingHeld(true);
        Frame frame = FindFrame();
        Check(frame != null, "Framing input supplies a Frame for the next photograph.");
        frame.GlobalPosition = _pattern.GlobalPosition;
        await PhysicsFramesAsync(2);
        Check(frame.TryGetCaptureTarget(out var target, out float rate) && target == _pattern && rate >= Config.PHOTO_SUCCESS_RATE,
            "The prepared viewfinder has successful coverage before taking a photograph.");
    }

    private void SetFramingHeld(bool held)
    {
        _main._Input(new InputEventKey { PhysicalKeycode = Key.Space, Pressed = held });
    }

    private Frame FindFrame()
    {
        foreach (Node child in _main.GetChildren())
            if (child is Frame frame && !frame.IsQueuedForDeletion())
                return frame;
        return null;
    }

    private Control PhotographCard() => _photoPanel.GetNode<Control>("PhotographCard");
    private Label CountLabel() => _bar.GetNode<Label>("MaterialDrawer/MaterialCount");

    private float RevealRadius()
    {
        var photograph = _photoPanel.GetNode<TextureRect>("PhotographCard/Photograph");
        return ((ShaderMaterial)photograph.Material).GetShaderParameter("reveal_radius").AsSingle();
    }

    private Task WaitForRevealAsync() => WaitUntilAsync(
        () => _photoPanel.Visible && PhotographCard().Visible && RevealRadius() >= 0.35f,
        "The circular conversion reaches a visible midpoint.");

    private Task WaitForIdleAsync() => WaitUntilAsync(() => !_main.IsPhotographing,
        "The real photograph flow finishes within fifteen seconds.");

    private async Task WaitUntilAsync(Func<bool> predicate, string message)
    {
        ulong deadline = Time.GetTicksMsec() + DeadlineMilliseconds;
        while (!predicate())
        {
            if (Time.GetTicksMsec() >= deadline)
                throw new TimeoutException(message);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        Check(true, message);
    }

    private async Task ResizeWindowAsync(Vector2I size)
    {
        Window window = GetWindow();
        window.Size = size;
        ulong deadline = Time.GetTicksMsec() + 3000;
        int matchingFrames = 0;
        Vector2I previousTextureSize = new Vector2I(-1, -1);
        string diagnostics = "No frame has rendered since the resize request.";
        while (Time.GetTicksMsec() < deadline)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using Image rendered = GetViewport().GetTexture().GetImage();
            Vector2I textureSize = rendered.GetSize();
            diagnostics = $"requested={size}, windowSize={window.Size}, visibleRect={GetViewport().GetVisibleRect()}, "
                + $"textureSize={textureSize}, contentScaleMode={window.ContentScaleMode}, "
                + $"contentScaleAspect={window.ContentScaleAspect}, contentScaleSize={window.ContentScaleSize}";
            if (textureSize != previousTextureSize)
            {
                GD.Print($"Photo integration resize: {diagnostics}");
                previousTextureSize = textureSize;
            }
            matchingFrames = window.Size == size && textureSize == size ? matchingFrames + 1 : 0;
            if (matchingFrames >= 2)
            {
                _verifiedRenderSize = size;
                GD.Print($"Photo integration resize acknowledged: {diagnostics}");
                Check(true, $"The actual native viewport has rendered the requested {size} dimensions twice.");
                return;
            }
        }
        throw new TimeoutException($"The viewport did not render the requested window size within three seconds. {diagnostics}. "
            + "KeepHeight/KeepWidth may letterbox the native viewport instead of rendering the complete window; "
            + "the aspect-ratio checks cannot use that cropped viewport as evidence of full-window adaptation.");
    }

    private void CheckCenteredDesign(string aspect)
    {
        Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
        using Image rendered = GetViewport().GetTexture().GetImage();
        Check(GetWindow().Size == _verifiedRenderSize && rendered.GetSize() == _verifiedRenderSize,
            $"The {aspect} assertions use the acknowledged window and native rendering dimensions.");
        float requestedAspect = _verifiedRenderSize.X / (float)_verifiedRenderSize.Y;
        Check(Math.Abs(viewportSize.X / viewportSize.Y - requestedAspect) < 0.0025f,
            $"The visible viewport really has the requested {aspect} aspect ratio rather than the previous or letterboxed ratio.");
        var container = _main.GetNode<AspectRatioContainer>("UILayer/AspectRatioContainer");
        Vector2 containerCenter = container.GetGlobalTransformWithCanvas() * (container.Size / 2);
        Transform2D designTransform = _designRoot.GetGlobalTransformWithCanvas();
        Vector2 designCenter = designTransform * (_designRoot.Size / 2);
        Check(containerCenter.DistanceTo(viewportSize / 2) < 1.5f,
            $"The outer aspect container is centered in the {aspect} viewport.");
        Check(designCenter.DistanceTo(viewportSize / 2) < 1.5f,
            $"The complete design rectangle is centered in the {aspect} viewport.");
        Check(Math.Abs(designTransform.X.Length() - designTransform.Y.Length()) < 0.0001f,
            $"The {aspect} layout scales both design axes equally.");
        Vector2 topLeft = designTransform * Vector2.Zero;
        Vector2 bottomRight = designTransform * _designRoot.Size;
        Check(topLeft.X >= -1.5f && topLeft.Y >= -1.5f
            && bottomRight.X <= viewportSize.X + 1.5f && bottomRight.Y <= viewportSize.Y + 1.5f,
            $"The complete design rectangle fits inside the {aspect} viewport.");
        Check(_designRoot.Size.DistanceTo(new Vector2(2430, 1080)) < 0.001f,
            $"The {aspect} layout keeps the original design coordinate system.");
    }

    private async Task ScreenshotAsync(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image screenshot = GetViewport().GetTexture().GetImage();
        Check(!screenshot.IsEmpty(), $"The native viewport screenshot {name} has pixels.");
        if (name != "chatgpt-test-failure")
            Check(screenshot.GetSize() == _verifiedRenderSize,
                $"The native screenshot {name} retains the verified {_verifiedRenderSize} rendering dimensions.");
        Check(screenshot.SavePng($"{ScreenshotDirectory}/{name}.png") == Error.Ok,
            $"The native viewport screenshot {name} is saved.");
    }

    private async Task PhysicsFramesAsync(int count)
    {
        for (int index = 0; index < count; index++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private async Task ProcessFramesAsync(int count)
    {
        for (int index = 0; index < count; index++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void CheckLayerTwo(Node node)
    {
        if (node is CanvasItem item)
            Check((item.VisibilityLayer & 1) == 0 && (item.VisibilityLayer & 2) != 0,
                $"Frame canvas item {node.Name} is excluded from world layer 1.");
        foreach (Node child in node.GetChildren())
            CheckLayerTwo(child);
    }

    private static bool HasColourVariation(Image image)
    {
        Color first = image.GetPixel(0, 0);
        for (int y = 0; y < image.GetHeight(); y += 31)
        for (int x = 0; x < image.GetWidth(); x += 31)
        {
            Color sample = image.GetPixel(x, y);
            if (Math.Abs(sample.R - first.R) + Math.Abs(sample.G - first.G) + Math.Abs(sample.B - first.B) > 0.04f)
                return true;
        }
        return false;
    }

    private static bool SamePixels(byte[] first, byte[] second) => first.AsSpan().SequenceEqual(second);

    private static bool SameTransform(Transform2D first, Transform2D second) =>
        first.Origin.DistanceTo(second.Origin) < 0.001f
        && first.X.DistanceTo(second.X) < 0.001f && first.Y.DistanceTo(second.Y) < 0.001f;

    private void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
        _checks++;
    }
}
