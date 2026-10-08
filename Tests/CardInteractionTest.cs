using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>Exercises card gestures through native viewport input under a scaled HUD.</summary>
public partial class CardInteractionTest : Node
{
    private MaterialInventory _inventory;
    private CanvasLayer _fixture;
    private Control _hud;
    private Bar _bar;
    private int _cameraDragEvents;
    private float _cameraDragDelta;
    private VirtualJoystick _joystick;
    private int _joystickPresses;
    private int _joystickReleases;
    private Vector2 _mousePosition;
    private bool _mouseDown;
    private readonly Dictionary<int, Vector2> _touches = new();
    private int _checks;
    private bool _finished;
    private string _stage = "startup";

    public override async void _Ready()
    {
        GetTree().CreateTimer(60).Timeout += () =>
        {
            if (!_finished)
            {
                GD.PushError($"Card interaction timed out during {_stage}.");
                GetTree().Quit(1);
            }
        };
        try
        {
            Check(DisplayServer.GetName() != "headless", "Card gestures run in a real rendering window.");
            GetWindow().Mode = Window.ModeEnum.Windowed;
            await ResizeWindowAsync(new Vector2I(1080, 480));
            CheckInventoryOperations();
            await CheckMouseBrowsingAsync();
            await CheckTouchIsolationAsync();
            await CheckDeleteHoverAndRecollectionAsync();
            await CheckReorderingAsync();
            await CheckCrossViewportReorderingAsync();
            await CheckCancellationAsync();
            await CheckEmptyLibraryAsync();
            await CheckLeftInsertionAndCapacityAsync();
            _finished = true;
            GD.Print($"Card interaction: {_checks} checks passed.");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            _finished = true;
            GD.PushError($"Card interaction failed during {_stage}: {exception}");
            try { await ScreenshotAsync("chatgpt-card-test-failure"); }
            catch (Exception screenshotError) { GD.PushWarning(screenshotError.Message); }
            GetTree().Quit(1);
        }
    }

    private void CheckInventoryOperations()
    {
        _stage = "inventory identity operations";
        Texture2D texture = MakeTexture(0);
        var inventory = new MaterialInventory();
        Check(inventory.TryAdd("a", "A", texture) && inventory.TryAdd("b", "B", texture),
            "Two independent materials enter the inventory.");
        MaterialEntry originalA = inventory.Entries[0];
        MaterialEntry originalB = inventory.Entries[1];
        Check(inventory.TryInsert(0, "fresh", "Fresh", texture), "New materials can be inserted at the leftmost position.");
        Check(inventory.Entries.Select(entry => entry.MaterialId).SequenceEqual(new[] { "fresh", "a", "b" }),
            "Left insertion preserves the order of existing materials.");
        Check(inventory.TryMove("a", 2) && ReferenceEquals(inventory.Entries[2], originalA)
            && ReferenceEquals(inventory.Entries[1], originalB), "Moving preserves material objects and their art.");
        Check(inventory.TryRemove("b") && !inventory.Contains("b"), "Deletion releases the material's duplicate key.");
        Check(inventory.TryInsert(1, "b", "Recollected B", texture), "A deleted material can be collected again.");
        string[] beforeInvalid = inventory.Entries.Select(entry => entry.MaterialId).ToArray();
        Check(!inventory.TryMove("missing", 0) && !inventory.TryMove("a", -1)
            && !inventory.TryInsert(-1, "invalid", "Invalid", texture)
            && inventory.Entries.Select(entry => entry.MaterialId).SequenceEqual(beforeInvalid),
            "Invalid operations leave the inventory and order intact.");
    }

    private async Task BuildFixtureAsync(int count)
    {
        ReleaseAllPointers();
        if (_fixture != null)
        {
            _fixture.QueueFree();
            await ProcessFramesAsync(2);
        }
        _inventory = new MaterialInventory();
        for (int index = 0; index < count; index++)
            Check(_inventory.TryAdd(Id(index), NameFor(index), MakeTexture(index)), "Fixture material is accepted.");
        _fixture = new CanvasLayer { Name = "CardFixture" };
        AddChild(_fixture);
        _hud = new Control
        {
            Name = "ScaledHud", Size = new Vector2(2430, 1080), Position = new Vector2(23, 11),
            Scale = Vector2.One * 0.65f, MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _fixture.AddChild(_hud);
        _cameraDragEvents = 0;
        _cameraDragDelta = 0;
        var backgroundDrag = new DragDetection
        {
            Name = "BackgroundDrag", Size = _hud.Size, MouseFilter = Control.MouseFilterEnum.Stop
        };
        backgroundDrag.DragMovedX += delta => { _cameraDragEvents++; _cameraDragDelta += delta; };
        _hud.AddChild(backgroundDrag);
        _bar = new Bar { Name = "TestBar" };
        _bar.Initialize(_inventory);
        _hud.AddChild(_bar);
        _joystickPresses = 0;
        _joystickReleases = 0;
        _joystick = new VirtualJoystick
        {
            Name = "GestureJoystick", Position = new Vector2(100, 100), Size = new Vector2(300, 300),
            JoystickSize = 200, TipSize = 60, InitialOffsetRatio = new Vector2(0.5f, 0.5f)
        };
        _joystick.Pressed += () => _joystickPresses++;
        _joystick.Released += unused => _joystickReleases++;
        _hud.AddChild(_joystick);
        _bar.SetOpen(true, false);
        await ProcessFramesAsync(2);
        Check(Math.Abs(_hud.Scale.X - 0.65f) < 0.0001f && _hud.Position == new Vector2(23, 11),
            "Gesture fixture retains both HUD scaling and translation.");
        Check(Cards().ClipContents && Content().GetParent() == Cards(), "The scroll viewport clips a separate card-content control.");
        Check(Math.Abs(_bar.ScrollOffset) < 0.01f, "A fresh drawer starts at the leftmost material.");
        CheckVisibleCardsMatchInventory();
        Vector2 backgroundPoint = BlankPoint();
        PushMouseDown(backgroundPoint);
        PushMouseMove(backgroundPoint + new Vector2(65, 0));
        PushMouseUp(backgroundPoint + new Vector2(65, 0));
        await ProcessFramesAsync(2);
        Check(_cameraDragEvents == 1 && Math.Abs(_cameraDragDelta - 65) < 0.1f,
            "The real background DragDetection receives one canvas-correct motion as a positive input control.");
        _cameraDragEvents = 0;
        _cameraDragDelta = 0;
    }

    private async Task CheckMouseBrowsingAsync()
    {
        _stage = "mouse horizontal browsing";
        await BuildFixtureAsync(12);
        string[] before = Order();
        await QuickMouseSwipeAsync(CardsPoint(new Vector2(1180, 110)), CardsPoint(new Vector2(580, 110)));
        Check(_bar.ScrollOffset > 200 && !_bar.IsCardDragging,
            "A quick leftward mouse swipe scrolls the strip rather than dragging a material.");
        Check(Order().SequenceEqual(before), "Browsing does not change inventory order.");
        Check(Math.Abs(Content().Position.X + _bar.ScrollOffset) < 0.1f,
            "The content translation matches the observable scroll offset under a scaled HUD.");
        CheckVisibleCardsMatchInventory();
        await QuickMouseSwipeAsync(CardsPoint(new Vector2(250, 110)), CardsPoint(new Vector2(1550, 110)));
        Check(Math.Abs(_bar.ScrollOffset) < 0.1f && Order().SequenceEqual(before),
            "A rightward mouse swipe returns to the left edge and clamps without reordering.");
    }

    private async Task CheckTouchIsolationAsync()
    {
        _stage = "touch pointer isolation";
        await BuildFixtureAsync(8);
        string[] before = Order();
        Vector2 first = CardPoint(Id(0));
        await TouchDownAsync(3, first);
        await WaitUntilAsync(() => _bar.IsCardDragging, "Holding the primary touch starts a material drag.");
        Check(_bar.DraggedMaterialId == Id(0), "The touch gesture keeps the selected material ID.");
        Vector2 delete = DeletePoint();
        Vector2 joystickPoint = _joystick.GetGlobalTransformWithCanvas() * (_joystick.Size / 2);
        await TouchDownAsync(8, joystickPoint);
        Check(_joystickPresses == 1, "An unrelated touch can still press a real sibling joystick during a card drag.");
        await TouchMoveAsync(8, delete);
        Check(_bar.IsCardDragging && !_bar.IsDeleteHovered && _bar.DraggedMaterialId == Id(0),
            "An unrelated touch cannot move the selected card into the deletion zone.");
        await TouchUpAsync(8, delete);
        Check(_bar.IsCardDragging && _inventory.Count == 8,
            "Releasing the unrelated touch cannot finish or delete the primary drag.");
        Check(_joystickReleases == 1, "The strip leaves the unrelated release available to the native joystick.");
        await TouchMoveAsync(3, BlankPoint());
        await TouchUpAsync(3, BlankPoint());
        Check(!_bar.IsCardDragging && !_bar.IsDeleteHovered && Order().SequenceEqual(before),
            "The primary touch can cancel in ordinary blank space without deleting anything.");
        await TouchDownAsync(4, CardPoint(Id(0)));
        await WaitUntilAsync(() => _bar.IsCardDragging, "The touch-cancellation fixture starts a real drag.");
        await TouchMoveAsync(4, DeletePoint());
        Check(_bar.IsDeleteHovered, "The cancelled touch really reached the white deletion zone.");
        Vector2 simulatedReleasePoint = DeletePoint();
        GetViewport().PushInput(new InputEventMouseButton
        {
            Device = (int)InputEvent.DeviceIdEmulation, Position = simulatedReleasePoint,
            GlobalPosition = simulatedReleasePoint, ButtonIndex = MouseButton.Left,
            ButtonMask = (MouseButtonMask)0, Pressed = false
        }, true);
        await ProcessFramesAsync(1);
        Check(_bar.IsCardDragging && _bar.DraggedMaterialId == Id(0)
            && _inventory.Count == 8 && Order().SequenceEqual(before),
            "An emulated mouse release cannot steal the primary touch or commit deletion before touch cancellation.");
        await TouchUpAsync(4, DeletePoint(), cancelled: true);
        CheckCancelledState("A cancelled primary touch");
        Check(Order().SequenceEqual(before), "InputEventScreenTouch.Canceled cannot commit deletion or reordering.");
    }

    private async Task CheckDeleteHoverAndRecollectionAsync()
    {
        _stage = "delete hover, cancellation, and confirmation";
        await BuildFixtureAsync(8);
        string id = Id(0);
        string[] before = Order();
        MaterialEntry selected = Entry(id);
        await BeginMouseDragAsync(id);
        await MouseMoveAsync(DeletePoint());
        Check(_bar.IsDeleteHovered && FindControl("DeleteZone").Visible,
            "Entering the white deletion zone marks the current drag as deletable.");
        Check(_inventory.Count == 8 && _inventory.Contains(id), "Hovering alone does not delete a material.");
        await ScreenshotAsync("chatgpt-card-delete-hover");
        await MouseMoveAsync(BlankPoint());
        Check(!_bar.IsDeleteHovered, "Leaving the white zone cancels its deletion highlight.");
        await MouseUpAsync(BlankPoint());
        Check(Order().SequenceEqual(before) && ReferenceEquals(Entry(id), selected),
            "Dropping outside the zone returns the same material to its unchanged slot.");
        Check(!FindControl("DeleteZone").Visible && !_bar.IsCardDragging,
            "The delete zone disappears after a cancelled drag.");

        await BeginMouseDragAsync(id);
        await MouseMoveAsync(DeletePoint());
        await MouseUpAsync(DeletePoint());
        Check(_inventory.Count == 7 && !_inventory.Contains(id)
            && Order().SequenceEqual(before.Skip(1)), "Releasing inside the white zone deletes exactly the selected material.");
        Check(!_bar.IsCardDragging && !_bar.IsDeleteHovered && string.IsNullOrEmpty(_bar.DraggedMaterialId),
            "Confirmed deletion clears every observable gesture state.");
        await _bar.PresentResultAsync(false, id, selected.DisplayName, selected.Texture).WaitAsync(TimeSpan.FromSeconds(4));
        Check(_inventory.Count == 8 && Order().SequenceEqual(before),
            "Photographing a deleted material can collect it again at the left edge.");
        CheckVisibleCardsMatchInventory();
    }

    private async Task CheckReorderingAsync()
    {
        _stage = "inserting before and after other cards";
        await BuildFixtureAsync(6);
        string[] originalOrder = Order();
        MaterialEntry[] originals = _inventory.Entries.ToArray();
        await BeginMouseDragAsync(Id(4));
        Vector2 beforeSecond = GapBefore(Id(1));
        await MouseMoveAsync(beforeSecond);
        Check(FindControl("InsertionMarker").Visible && !_bar.IsDeleteHovered,
            "A card gap offers an insertion marker instead of deletion feedback.");
        await MouseUpAsync(beforeSecond);
        Check(Order().SequenceEqual(new[] { Id(0), Id(4), Id(1), Id(2), Id(3), Id(5) }),
            "A drop before another card inserts the dragged material at that position.");
        CheckOriginalEntries(originals);
        CheckVisibleCardsMatchInventory();

        await BeginMouseDragAsync(Id(4));
        Vector2 afterFourth = GapAfter(Id(3));
        await MouseMoveAsync(afterFourth);
        await MouseUpAsync(afterFourth);
        Check(Order().SequenceEqual(originalOrder), "A drop after another card adjusts the removed source index correctly.");
        CheckOriginalEntries(originals);
        CheckVisibleCardsMatchInventory();
        await ScreenshotAsync("chatgpt-card-reordered");

        // A vertical motion must extract a card immediately, without waiting for a long press.
        Vector2 start = CardPoint(Id(2));
        PushMouseDown(start);
        PushMouseMove(start + new Vector2(0, -90 * _hud.Scale.Y));
        Check(_bar.IsCardDragging && _bar.DraggedMaterialId == Id(2),
            "An unmistakable vertical movement starts dragging directly out of the strip.");
        await MouseMoveAsync(BlankPoint());
        await MouseUpAsync(BlankPoint());
        Check(Order().SequenceEqual(originalOrder) && _inventory.Count == 6,
            "An invalid outside drop never deletes or moves a material.");
    }

    private async Task CheckCancellationAsync()
    {
        _stage = "busy and focus-loss cancellation";
        await BuildFixtureAsync(6);
        string[] before = Order();
        await BeginMouseDragAsync(Id(2));
        await MouseMoveAsync(DeletePoint());
        Check(_bar.IsDeleteHovered, "The cancellation fixture is genuinely hovering the delete zone.");
        _bar.SetInteractionEnabled(false);
        CheckCancelledState("Disabling interaction");
        await MouseUpAsync(DeletePoint());
        Check(Order().SequenceEqual(before), "A release after busy cancellation cannot delete or reorder a material.");
        _bar.SetInteractionEnabled(true);

        await BeginMouseDragAsync(Id(2));
        await MouseMoveAsync(DeletePoint());
        _bar.Notification((int)Node.NotificationWMWindowFocusOut);
        CheckCancelledState("Losing window focus");
        await MouseUpAsync(DeletePoint());
        Check(Order().SequenceEqual(before), "A release after focus cancellation cannot commit a stale drop.");
        await BeginMouseDragAsync(Id(2));
        await MouseUpAsync(BlankPoint());
        Check(!_bar.IsCardDragging && Order().SequenceEqual(before), "A fresh gesture works again after both cancellation paths.");

        await BeginMouseDragAsync(Id(2));
        await MouseMoveAsync(DeletePoint());
        Vector2 originalPosition = _hud.Position;
        _hud.Position += new Vector2(37, -23);
        await ProcessFramesAsync(2);
        CheckCancelledState("Changing the ancestor canvas transform");
        await MouseUpAsync(DeletePoint());
        Check(Order().SequenceEqual(before), "A transform change cancels a pending deletion instead of applying stale coordinates.");
        _hud.Position = originalPosition;
        await ProcessFramesAsync(2);

        await BeginMouseDragAsync(Id(2));
        await MouseMoveAsync(DeletePoint());
        await ResizeWindowAsync(new Vector2I(1110, 510));
        await WaitUntilAsync(() => !_bar.IsCardDragging, "A native window resize cancels the active viewport gesture.");
        CheckCancelledState("Resizing the native viewport");
        await MouseUpAsync(DeletePoint());
        Check(Order().SequenceEqual(before), "A release after viewport resize cannot delete or reorder the cancelled material.");
        await ResizeWindowAsync(new Vector2I(1080, 480));
    }

    private async Task CheckCrossViewportReorderingAsync()
    {
        _stage = "dragging through the seven-card viewport in both directions";
        await BuildFixtureAsync(12);
        string[] originalOrder = Order();
        MaterialEntry[] originals = _inventory.Entries.ToArray();
        await BeginMouseDragAsync(Id(1));
        await MouseMoveAsync(CardsPoint(new Vector2(Cards().Size.X - 10, 113)));
        await WaitUntilAsync(() => Math.Abs(_bar.ScrollOffset - _bar.MaxScrollOffset) < 0.1f,
            "Holding a dragged card at the right edge scrolls all the way to the initially hidden materials.");
        Check(_bar.IsCardDragging && _bar.DraggedMaterialId == Id(1) && !_bar.IsDeleteHovered,
            "Edge browsing retains the original dragged material instead of deleting it.");
        Vector2 afterHidden = GapAfter(Id(10));
        Check(new Rect2(Vector2.Zero, Cards().Size).HasPoint(
                Cards().GetGlobalTransformWithCanvas().AffineInverse() * afterHidden),
            "The previously hidden destination is now inside the visible seven-card strip.");
        await MouseMoveAsync(afterHidden);
        await MouseUpAsync(afterHidden);
        string[] moved = originalOrder.Where(id => id != Id(1)).ToArray();
        var expected = moved.ToList();
        expected.Insert(10, Id(1));
        Check(Order().SequenceEqual(expected), "A drag across the viewport inserts after a formerly hidden material.");
        CheckOriginalEntries(originals);

        await BeginMouseDragAsync(Id(1));
        await MouseMoveAsync(CardsPoint(new Vector2(10, 113)));
        await WaitUntilAsync(() => Math.Abs(_bar.ScrollOffset) < 0.1f,
            "Holding at the left edge scrolls back through the hidden cards to the beginning.");
        Check(_bar.IsCardDragging && _bar.DraggedMaterialId == Id(1),
            "Reverse edge browsing keeps the same card attached to the pointer.");
        Vector2 beforeSecond = GapBefore(Id(2));
        await MouseMoveAsync(beforeSecond);
        await MouseUpAsync(beforeSecond);
        Check(Order().SequenceEqual(originalOrder), "Reverse scrolling and insertion restore the full original material order.");
        CheckOriginalEntries(originals);
        CheckVisibleCardsMatchInventory();
        await ScreenshotAsync("chatgpt-card-cross-viewport-reorder");
    }

    private async Task CheckEmptyLibraryAsync()
    {
        _stage = "deleting the final material and refilling the empty library";
        await BuildFixtureAsync(0);
        Check(!Content().GetChildren().OfType<MaterialCard>().Any(),
            "A fresh empty inventory creates no empty card nodes in the strip.");
        MaterialCard unassignedPreview = _bar.GetNode<MaterialCard>("DragPreview");
        Check(unassignedPreview.Entry == null
            && !unassignedPreview.GetNode<TextureRect>("CardFrame").Visible
            && !unassignedPreview.GetNode<TextureRect>("Pattern").Visible
            && !unassignedPreview.GetNode<Label>("MaterialName").Visible
            && unassignedPreview.GetNode<Label>("MaterialName").Text == "",
            "An unassigned material-card preview displays no frame, image, or placeholder number.");
        await BuildFixtureAsync(1);
        MaterialEntry last = _inventory.Entries[0];
        await BeginMouseDragAsync(last.MaterialId);
        await MouseMoveAsync(DeletePoint());
        await MouseUpAsync(DeletePoint());
        Check(_inventory.Count == 0 && !_inventory.Contains(last.MaterialId)
            && Math.Abs(_bar.ScrollOffset) < 0.01f, "Deleting the final card leaves an empty library at scroll zero.");
        CheckCancelledState("Deleting the final material");
        Check(!Content().GetChildren().OfType<MaterialCard>().Any(),
            "Deleting the final material leaves no card nodes or empty placeholders after it is freed.");
        await ScreenshotAsync("chatgpt-card-empty-library");
        await QuickMouseSwipeAsync(CardsPoint(new Vector2(500, 110)), CardsPoint(new Vector2(200, 110)));
        Check(!_bar.IsCardDragging && Math.Abs(_bar.ScrollOffset) < 0.01f,
            "An empty strip clamps horizontal browsing and cannot extract a nonexistent material.");
        await MouseDownAsync(CardsPoint(new Vector2(300, 110)));
        await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
        Check(!_bar.IsCardDragging && !FindControl("DeleteZone").Visible,
            "Holding the empty strip does not show a material preview or deletion zone.");
        await MouseUpAsync(BlankPoint());
        await _bar.PresentResultAsync(false, last.MaterialId, last.DisplayName, last.Texture).WaitAsync(TimeSpan.FromSeconds(4));
        Check(_inventory.Count == 1 && _inventory.Entries[0].MaterialId == last.MaterialId,
            "The empty library accepts a photograph of the deleted material again.");
        CheckVisibleCardsMatchInventory();
    }

    private async Task CheckLeftInsertionAndCapacityAsync()
    {
        _stage = "left insertion animation and capacity";
        await BuildFixtureAsync(10);
        await QuickMouseSwipeAsync(CardsPoint(new Vector2(1180, 110)), CardsPoint(new Vector2(780, 110)));
        Check(_bar.ScrollOffset > 100, "Insertion begins while the player is browsing farther right.");
        MaterialEntry[] originals = _inventory.Entries.ToArray();
        var originalCards = originals.ToDictionary(entry => entry.MaterialId, entry => CardFor(entry.MaterialId));
        string[] before = Order();
        Task receipt = _bar.PresentResultAsync(false, "incoming", "Incoming", MakeTexture(13));
        Check(!receipt.IsCompleted, "A new photograph receipt includes an awaited card animation.");
        await WaitUntilAsync(() => _inventory.Contains("incoming"), "The photo receipt commits a new material.");
        Check(_inventory.Entries[0].MaterialId == "incoming" && Math.Abs(_bar.ScrollOffset) < 0.1f,
            "A new receipt is placed at the far left and resets strip browsing.");
        Check(_inventory.Entries.Skip(1).Select(entry => entry.MaterialId).SequenceEqual(before),
            "Left insertion shifts existing materials right while preserving their relative order.");
        Check(originalCards.All(pair => ReferenceEquals(CardFor(pair.Key), pair.Value)),
            "The left-insertion animation reuses every existing material-card node.");
        Check(originalCards.Any(pair => Math.Abs(pair.Value.Position.X
            - Array.IndexOf(Order(), pair.Key) * 195) > 0.5f),
            "Existing cards are still moving toward their shifted positions while the receipt is pending.");
        await ScreenshotAsync("chatgpt-card-left-insertion");
        await receipt.WaitAsync(TimeSpan.FromSeconds(4));
        CheckOriginalEntries(originals);
        Check(originalCards.All(pair => ReferenceEquals(CardFor(pair.Key), pair.Value)),
            "Existing material-card node identities remain stable after the animation finishes.");
        CheckVisibleCardsMatchInventory();
        Check(_bar.GetNode<Label>("MaterialDrawer/MaterialCount").Text == "11/14",
            "The animated insertion and count display agree.");

        while (_inventory.Count < MaterialInventory.Capacity)
            Check(_inventory.TryAdd($"capacity-{_inventory.Count}", $"Capacity {_inventory.Count}", MakeTexture(_inventory.Count)),
                "The capacity fixture accepts valid materials up to fourteen.");
        string[] fullOrder = Order();
        await _bar.PresentResultAsync(true, "overflow", "Overflow", MakeTexture(0)).WaitAsync(TimeSpan.FromSeconds(4));
        Check(_inventory.Count == 14 && !_inventory.Contains("overflow") && Order().SequenceEqual(fullOrder),
            "A full-library presentation cannot create a fifteenth card or change their order.");
        MaterialEntry removed = _inventory.Entries[0];
        await BeginMouseDragAsync(removed.MaterialId);
        await MouseMoveAsync(DeletePoint());
        await MouseUpAsync(DeletePoint());
        Check(_inventory.Count == 13 && !_inventory.Contains(removed.MaterialId),
            "Deleting through the UI frees one slot at capacity.");
        await _bar.PresentResultAsync(false, removed.MaterialId, removed.DisplayName, removed.Texture).WaitAsync(TimeSpan.FromSeconds(4));
        Check(_inventory.Count == 14 && _inventory.Entries[0].MaterialId == removed.MaterialId
            && Order().SequenceEqual(fullOrder), "The freed slot accepts the same material again and remains capped at fourteen.");
        CheckVisibleCardsMatchInventory();
    }

    private async Task BeginMouseDragAsync(string id)
    {
        await MouseDownAsync(CardPoint(id));
        await WaitUntilAsync(() => _bar.IsCardDragging, "Holding a material starts dragging.");
        Check(_bar.DraggedMaterialId == id, "The drag exposes the intended material's stable ID.");
    }

    private async Task QuickMouseSwipeAsync(Vector2 start, Vector2 finish)
    {
        // Dispatch the quick gesture synchronously so renderer stalls cannot turn
        // test frame waits into a deliberate quarter-second long press.
        PushMouseDown(start);
        for (int step = 1; step <= 3; step++)
            PushMouseMove(start.Lerp(finish, step / 3f));
        PushMouseUp(finish);
        await ProcessFramesAsync(2);
        Check(_cameraDragEvents == 0, "Horizontal card browsing leaves the garden camera stationary.");
    }

    private async Task MouseDownAsync(Vector2 position)
    {
        PushMouseDown(position);
        await ProcessFramesAsync(1);
    }

    private void PushMouseDown(Vector2 position)
    {
        _mousePosition = position;
        _mouseDown = true;
        GetViewport().PushInput(new InputEventMouseButton
        {
            Position = position, GlobalPosition = position, ButtonIndex = MouseButton.Left,
            ButtonMask = MouseButtonMask.Left, Pressed = true
        }, true);
    }

    private async Task MouseMoveAsync(Vector2 position)
    {
        PushMouseMove(position);
        await ProcessFramesAsync(1);
    }

    private void PushMouseMove(Vector2 position)
    {
        Vector2 relative = position - _mousePosition;
        _mousePosition = position;
        GetViewport().PushInput(new InputEventMouseMotion
        {
            Position = position, GlobalPosition = position, Relative = relative,
            ButtonMask = _mouseDown ? MouseButtonMask.Left : (MouseButtonMask)0
        }, true);
    }

    private async Task MouseUpAsync(Vector2 position)
    {
        PushMouseUp(position);
        await ProcessFramesAsync(2);
        await WaitForCardsAtRestAsync();
        Check(_cameraDragEvents == 0, "The entire card drag, including movement outside the strip, leaves the garden camera stationary.");
    }

    private void PushMouseUp(Vector2 position)
    {
        _mousePosition = position;
        _mouseDown = false;
        GetViewport().PushInput(new InputEventMouseButton
        {
            Position = position, GlobalPosition = position, ButtonIndex = MouseButton.Left,
            ButtonMask = (MouseButtonMask)0, Pressed = false
        }, true);
    }

    private async Task TouchDownAsync(int index, Vector2 position)
    {
        _touches[index] = position;
        GetViewport().PushInput(new InputEventScreenTouch { Index = index, Position = position, Pressed = true }, true);
        await ProcessFramesAsync(1);
    }

    private async Task TouchMoveAsync(int index, Vector2 position)
    {
        Vector2 relative = position - _touches[index];
        _touches[index] = position;
        GetViewport().PushInput(new InputEventScreenDrag { Index = index, Position = position, Relative = relative }, true);
        await ProcessFramesAsync(1);
    }

    private async Task TouchUpAsync(int index, Vector2 position, bool cancelled = false)
    {
        _touches.Remove(index);
        GetViewport().PushInput(new InputEventScreenTouch
            { Index = index, Position = position, Pressed = false, Canceled = cancelled }, true);
        await ProcessFramesAsync(2);
        await WaitForCardsAtRestAsync();
        Check(_cameraDragEvents == 0, "Touch card gestures and unrelated joystick touches do not drag the garden background.");
    }

    private void ReleaseAllPointers()
    {
        if (_mouseDown)
        {
            GetViewport().PushInput(new InputEventMouseButton
            {
                Position = _mousePosition, GlobalPosition = _mousePosition,
                ButtonIndex = MouseButton.Left, Pressed = false
            }, true);
            _mouseDown = false;
        }
        foreach (var touch in _touches)
            GetViewport().PushInput(new InputEventScreenTouch { Index = touch.Key, Position = touch.Value, Pressed = false }, true);
        _touches.Clear();
    }

    private Control Cards() => _bar.GetNode<Control>("MaterialDrawer/Cards");
    private Control Content() => Cards().GetNode<Control>("CardContent");
    private string[] Order() => _inventory.Entries.Select(entry => entry.MaterialId).ToArray();
    private MaterialEntry Entry(string id) => _inventory.Entries.Single(entry => entry.MaterialId == id);
    private static string Id(int index) => $"entry-{index}";
    private static string NameFor(int index) => $"Entry {index:00}";
    private Vector2 CardsPoint(Vector2 local) => Cards().GetGlobalTransformWithCanvas() * local;
    private Vector2 BlankPoint() => _hud.GetGlobalTransformWithCanvas() * new Vector2(1200, 250);
    private Vector2 DeletePoint()
    {
        Control zone = FindControl("DeleteZone");
        return zone.GetGlobalTransformWithCanvas() * (zone.Size * new Vector2(0.5f, 0.3f));
    }

    private MaterialCard CardFor(string id)
    {
        return Content().GetChildren().OfType<MaterialCard>().Single(card => card.Entry?.MaterialId == id);
    }

    private Vector2 CardPoint(string id)
    {
        MaterialCard card = CardFor(id);
        Vector2 point = card.GetGlobalTransformWithCanvas() * (card.Size / 2);
        Vector2 local = Cards().GetGlobalTransformWithCanvas().AffineInverse() * point;
        Check(new Rect2(Vector2.Zero, Cards().Size).HasPoint(local), "The chosen input starts on a visible card within the clipped strip.");
        return point;
    }

    private Vector2 GapBefore(string id)
    {
        MaterialCard card = CardFor(id);
        return Content().GetGlobalTransformWithCanvas() * (card.Position + new Vector2(-10, card.Size.Y / 2));
    }

    private Vector2 GapAfter(string id)
    {
        MaterialCard card = CardFor(id);
        return Content().GetGlobalTransformWithCanvas() * (card.Position + new Vector2(card.Size.X + 10, card.Size.Y / 2));
    }

    private Control FindControl(string name) => FindNamedControl(_bar, name)
        ?? throw new InvalidOperationException($"Missing public gesture control {name}.");

    private static Control FindNamedControl(Node root, string name)
    {
        if (root.Name == name && root is Control control)
            return control;
        foreach (Node child in root.GetChildren())
        {
            Control result = FindNamedControl(child, name);
            if (result != null)
                return result;
        }
        return null;
    }

    private void CheckCancelledState(string trigger)
    {
        Check(!_bar.IsCardDragging && !_bar.IsDeleteHovered && string.IsNullOrEmpty(_bar.DraggedMaterialId),
            $"{trigger} clears the dragging identity and deletion hover.");
        Check(!FindControl("DeleteZone").Visible && !FindControl("InsertionMarker").Visible,
            $"{trigger} clears the visible deletion and insertion targets.");
    }

    private void CheckOriginalEntries(IEnumerable<MaterialEntry> originals)
    {
        foreach (MaterialEntry original in originals)
            Check(ReferenceEquals(Entry(original.MaterialId), original) && Entry(original.MaterialId).Texture == original.Texture,
                "Repositioning keeps the original material object and texture together.");
    }

    private void CheckVisibleCardsMatchInventory()
    {
        MaterialCard[] cards = Content().GetChildren().OfType<MaterialCard>().ToArray();
        Check(cards.Length == _inventory.Count && cards.All(card => card.Entry != null),
            "The strip contains exactly the collected cards, with no empty or surplus card nodes.");
        Check(cards.Select(card => card.Entry.MaterialId).ToHashSet(StringComparer.Ordinal)
            .SetEquals(_inventory.Entries.Select(entry => entry.MaterialId)),
            "Every card node belongs to the inventory's exact material ID set.");
        foreach (MaterialEntry entry in _inventory.Entries)
        {
            MaterialCard card = cards.Single(candidate => candidate.GetNode<Label>("MaterialName").Text == entry.DisplayName);
            Check(ReferenceEquals(card.Entry, entry) && card.GetNode<TextureRect>("Pattern").Texture == entry.Texture,
                "A displayed material's name and image retain the same inventory identity.");
            CheckMouseIgnoring(card);
        }
    }

    private void CheckMouseIgnoring(Node node)
    {
        if (node is Control control)
            Check(control.MouseFilter == Control.MouseFilterEnum.Ignore, "Card decoration leaves input ownership with the strip viewport.");
        foreach (Node child in node.GetChildren())
            CheckMouseIgnoring(child);
    }

    private async Task WaitUntilAsync(Func<bool> predicate, string message)
    {
        ulong deadline = Time.GetTicksMsec() + 2500;
        while (!predicate())
        {
            if (Time.GetTicksMsec() >= deadline)
                throw new TimeoutException(message);
            await ProcessFramesAsync(1);
        }
        Check(true, message);
    }

    private async Task WaitForCardsAtRestAsync()
    {
        await WaitUntilAsync(() => _inventory.Entries.Select((entry, index) =>
                Math.Abs(CardFor(entry.MaterialId).Position.X - index * 195) < 0.005f).All(atRest => atRest),
            "The native card animation returns every material to its intended slot before the next gesture.");
        await ProcessFramesAsync(2);
    }

    private async Task ProcessFramesAsync(int count)
    {
        for (int index = 0; index < count; index++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task RenderFramesAsync(int count)
    {
        for (int index = 0; index < count; index++)
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }

    private async Task ResizeWindowAsync(Vector2I requested)
    {
        GetWindow().Size = requested;
        ulong deadline = Time.GetTicksMsec() + 3000;
        while (true)
        {
            await RenderFramesAsync(1);
            using Image image = GetViewport().GetTexture().GetImage();
            if (GetWindow().Size == requested && image.GetSize() == requested)
            {
                Check(true, $"The native window and rendered viewport acknowledge {requested}.");
                return;
            }
            if (Time.GetTicksMsec() >= deadline)
                throw new TimeoutException($"Resize did not reach {requested}; window={GetWindow().Size}, "
                    + $"visible={GetViewport().GetVisibleRect()}, rendered={image.GetSize()}.");
        }
    }

    private async Task ScreenshotAsync(string name)
    {
        DirAccess.MakeDirRecursiveAbsolute("res://Output/shots");
        await RenderFramesAsync(1);
        using Image image = GetViewport().GetTexture().GetImage();
        Check(!image.IsEmpty() && image.SavePng($"res://Output/shots/{name}.png") == Error.Ok,
            "The real renderer saves the card gesture for visual inspection.");
    }

    private static Texture2D MakeTexture(int index)
    {
        Image image = Image.CreateEmpty(32, 32, false, Image.Format.Rgba8);
        image.Fill(Color.FromHsv((index * 0.071f) % 1, 0.45f, 0.82f));
        return ImageTexture.CreateFromImage(image);
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
        _checks++;
    }
}
