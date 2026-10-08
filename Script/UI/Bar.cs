using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>A horizontally browsable material drawer with drag-to-delete and insertion ordering.</summary>
public partial class Bar : Control
{
    private static readonly Vector2 OpenSize = new(1545, 276);
    private static readonly Vector2 FoldSize = new(1545, 105);
    private static readonly Vector2 CardSize = new(174, 226);
    private const float CardStep = 195;
    private const float GestureDistance = 14;
    private const double HoldDuration = 0.25;
    private static readonly Color CountColor = new(0.96f, 0.95f, 0.87f);
    private static readonly Color HighlightColor = new(1, 0.98f, 0.57f);

    private enum GestureMode { None, Pending, Scrolling, Dragging }
    private MaterialInventory _inventory;
    private Control _panel;
    private TextureRect _openBackground;
    private TextureRect _foldBackground;
    private Control _cardsRoot;
    private Control _cardContent;
    private TextureRect _deleteZone;
    private Label _deleteLabel;
    private ColorRect _insertionMarker;
    private MaterialCard _dragPreview;
    private TextureRect _patternPreview;
    private Label _countLabel;
    private Label _rangeLabel;
    private Button _toggleButton;
    private readonly Dictionary<string, MaterialCard> _cards = new(StringComparer.Ordinal);
    private Tween _layoutTween;
    private Tween _countTween;
    private Tween _cardsTween;
    private TaskCompletionSource<bool> _cardsCompletion;
    private bool _cardsAnimating;
    private bool _animateNextRefresh;
    private string _incomingId = "";
    private int _nextCardNumber;
    private bool _interactionEnabled = true;
    private bool _built;
    private float _scrollOffset;
    private GestureMode _gesture;
    private int _pointer = -2;
    private Vector2 _pressCanvas;
    private Vector2 _lastCanvas;
    private float _scrollAtPress;
    private double _holdElapsed;
    private string _pressedId = "";
    private string _draggedId = "";
    private Vector2 _grabOffset;
    private Transform2D _gestureTransform;

    public bool IsOpen { get; private set; }
    public float ScrollOffset => _scrollOffset;
    public float MaxScrollOffset => Mathf.Max(0, ((_inventory?.Count ?? 0) - MaterialInventory.PageSize) * CardStep);
    public bool IsCardDragging => _gesture == GestureMode.Dragging;
    public bool IsDeleteHovered { get; private set; }
    public string DraggedMaterialId => _draggedId;
    public bool IsNotebookMode { get; private set; }
    public Func<MaterialEntry, Vector2, bool> MaterialDropReceiver { get; set; }
    public Func<Vector2, bool> MaterialDropTarget { get; set; }
    public Func<MaterialEntry, Vector2> MaterialPreviewSize { get; set; }
    public Func<MaterialEntry, Texture2D> MaterialPreviewTexture { get; set; }

    public void SetNotebookMode(bool enabled)
    {
        CancelGesture();
        IsNotebookMode = enabled;
        _layoutTween?.Kill();
        _layoutTween = null;
        if (_built)
        {
            _countLabel.Position = enabled ? Vector2.Zero : new Vector2(28, -16);
            _countLabel.AddThemeFontSizeOverride("font_size", enabled ? 28 : 34);
            _cardsRoot.TooltipText = enabled
                ? ""
                : "左右滑动浏览；长按或向上拖出卡片，可删除或插入换位";
            _toggleButton.TooltipText = enabled ? "" : "展开或收起素材栏";
            PlacePanel();
        }
    }

    public void Initialize(MaterialInventory inventory)
    {
        if (_inventory != null)
            _inventory.Changed -= RefreshCards;
        if (_built)
            CancelGesture();
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _inventory.Changed += RefreshCards;
        if (_built)
            RefreshCards();
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        if (_inventory == null)
            Initialize(new MaterialInventory());
        BuildUi();
        RefreshCards();
        SetOpen(false, false);
        GetViewport().SizeChanged += CancelGesture;
    }

    public override void _ExitTree()
    {
        if (_inventory != null)
            _inventory.Changed -= RefreshCards;
        GetViewport().SizeChanged -= CancelGesture;
        CancelGesture();
        StopCardsAnimation();
        _layoutTween?.Kill();
        _countTween?.Kill();
    }

    public override void _Notification(int what)
    {
        if (!_built)
            return;
        if (what == NotificationWMWindowFocusOut)
            CancelGesture();
        else if (what == NotificationResized)
        {
            CancelGesture();
            PlacePanel();
        }
        else if (what == NotificationVisibilityChanged && !IsVisibleInTree())
            CancelGesture();
    }

    public void SetInteractionEnabled(bool enabled)
    {
        _interactionEnabled = enabled;
        if (!enabled)
            CancelGesture();
        if (_built)
            _toggleButton.Disabled = !enabled;
    }

    public void SetOpen(bool open, bool animate = true)
    {
        CancelGesture();
        IsOpen = open;
        if (!_built)
            return;
        _layoutTween?.Kill();
        _layoutTween = null;
        _openBackground.Visible = open;
        _foldBackground.Visible = !open;
        _cardsRoot.Visible = open;
        _rangeLabel.Visible = open;
        _countLabel.Visible = open;
        Vector2 destination = PanelPosition();
        PlaceDeleteZone();
        if (!animate || !IsInsideTree())
        {
            _panel.Position = destination;
            return;
        }
        _layoutTween = CreateTween();
        _layoutTween.TweenProperty(_panel, "position", destination, 0.28)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
    }

    /// <summary>Every new photograph prepares the leftmost slot, irrespective of the previous scroll.</summary>
    public async Task RevealForInsertionAsync()
    {
        if (!_built)
            return;
        SetScrollOffset(0);
        SetOpen(true, false);
        _panel.Position = new Vector2(_panel.Position.X, PanelPosition().Y + OpenSize.Y);
        SetOpen(true);
        if (_layoutTween != null && _layoutTween.IsRunning())
            await ToSignal(_layoutTween, Tween.SignalName.Finished);
    }

    /// <summary>Insert on the left and slide the existing cards one position to the right.</summary>
    public async Task PresentResultAsync(bool full, string id, string displayName, Texture2D texture, Vector2? scale = null)
    {
        if (!_built)
            return;
        CancelGesture();
        if (!IsOpen)
            SetOpen(true);
        if (_layoutTween != null && _layoutTween.IsRunning())
            await ToSignal(_layoutTween, Tween.SignalName.Finished);
        SetScrollOffset(0);
        _countTween?.Kill();
        _countLabel.Modulate = HighlightColor;
        if (full || (_inventory.Count >= MaterialInventory.Capacity && !_inventory.Contains(id)))
        {
            float originX = _countLabel.Position.X;
            _countTween = CreateTween();
            for (int i = 0; i < 6; i++)
                _countTween.TweenProperty(_countLabel, "position:x", originX + (i % 2 == 0 ? -10 : 10), 0.055);
            _countTween.TweenProperty(_countLabel, "position:x", originX, 0.055);
            await ToSignal(_countTween, Tween.SignalName.Finished);
            return;
        }

        _incomingId = id;
        _animateNextRefresh = true;
        bool inserted = _inventory.TryInsert(0, id, displayName, texture, scale);
        _incomingId = "";
        _animateNextRefresh = false;
        if (inserted && _cardsCompletion != null)
            await _cardsCompletion.Task;
    }

    /// <summary>Canvas position fully inside the opaque drawer body.</summary>
    public Vector2 GetInsertionPoint() => !_built ? Vector2.Zero
        : _panel.GetGlobalTransformWithCanvas() * new Vector2(163, 190);

    private void BuildUi()
    {
        _deleteZone = Background("res://Assets/Texture/ui/garden_bg_delete@3x.png", new Vector2(1731, 420));
        _deleteZone.Name = "DeleteZone";
        _deleteZone.Visible = false;
        AddChild(_deleteZone);
        _deleteLabel = new Label
        {
            Position = new Vector2(100, 155), Size = new Vector2(550, 80),
            MouseFilter = MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center, Text = "拖到白色区域，松手删除"
        };
        _deleteLabel.AddThemeFontSizeOverride("font_size", 28);
        _deleteLabel.AddThemeColorOverride("font_color", new Color(0.42f, 0.28f, 0.3f));
        _deleteZone.AddChild(_deleteLabel);

        _panel = new Control { Name = "MaterialDrawer", Size = OpenSize, MouseFilter = MouseFilterEnum.Ignore, ZIndex = 1 };
        AddChild(_panel);
        _openBackground = DrawerBackground("res://Assets/Texture/gardern/garden_cardbar_bg_open_extend@3x.png", "OpenBackground");
        _foldBackground = DrawerBackground("res://Assets/Texture/gardern/garden_cardbar_bg_close_extend@3x.png", "FoldBackground");
        _panel.AddChild(_openBackground);
        _panel.AddChild(_foldBackground);
        _cardsRoot = new Control
        {
            Name = "Cards", Position = new Vector2(76, 42), Size = new Vector2(1393, 234),
            MouseFilter = MouseFilterEnum.Stop, ClipContents = true,
            TooltipText = "左右滑动浏览；长按或向上拖出卡片，可删除或插入换位"
        };
        _panel.AddChild(_cardsRoot);
        _cardsRoot.GuiInput += OnCardsInput;
        _cardContent = new Control { Name = "CardContent", Size = new Vector2(2758, 234), MouseFilter = MouseFilterEnum.Ignore };
        _cardsRoot.AddChild(_cardContent);
        _insertionMarker = new ColorRect
        {
            Name = "InsertionMarker", Size = new Vector2(6, 226),
            Color = new Color(1, 0.96f, 0.55f), MouseFilter = MouseFilterEnum.Ignore, Visible = false, ZIndex = 2
        };
        _cardsRoot.AddChild(_insertionMarker);
        _countLabel = new Label
        {
            Name = "MaterialCount", Position = new Vector2(28, -16), Size = new Vector2(170, 50),
            MouseFilter = MouseFilterEnum.Ignore, Modulate = CountColor,
            VerticalAlignment = VerticalAlignment.Center
        };
        _countLabel.AddThemeFontSizeOverride("font_size", 34);
        _countLabel.AddThemeColorOverride("font_outline_color", new Color(0.23f, 0.2f, 0.16f));
        _countLabel.AddThemeConstantOverride("outline_size", 5);
        _panel.AddChild(_countLabel);
        _toggleButton = new Button
        {
            Name = "ToggleDrawer", Position = new Vector2(1365, 0), Size = new Vector2(113, 79),
            MouseFilter = MouseFilterEnum.Stop, TooltipText = "展开或收起素材栏"
        };
        foreach (string style in new[] { "normal", "hover", "pressed", "focus", "disabled" })
            _toggleButton.AddThemeStyleboxOverride(style, new StyleBoxEmpty());
        _toggleButton.Pressed += () => SetOpen(!IsOpen);
        _panel.AddChild(_toggleButton);
        _rangeLabel = new Label
        {
            Name = "MaterialRange", Position = new Vector2(640, 249), Size = new Vector2(265, 27),
            HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore
        };
        _rangeLabel.AddThemeFontSizeOverride("font_size", 20);
        _panel.AddChild(_rangeLabel);
        _dragPreview = new MaterialCard { Name = "DragPreview", Size = CardSize, Visible = false, ZIndex = 40 };
        AddChild(_dragPreview);
        _patternPreview = new TextureRect
        {
            Name = "PatternDragPreview", Visible = false, ZIndex = 40,
            MouseFilter = MouseFilterEnum.Ignore, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale
        };
        AddChild(_patternPreview);
        _built = true;
    }

    private static TextureRect Background(string path, Vector2 size) => new()
    {
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
        Texture = GD.Load<Texture2D>(path), Size = size, MouseFilter = MouseFilterEnum.Ignore
    };

    private static TextureRect DrawerBackground(string path, string name)
    {
        Texture2D texture = GD.Load<Texture2D>(path);
        // The extra artwork extends below the original drawer; it must not change its layout or compress the top.
        return new TextureRect
        {
            Name = name, MouseFilter = MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
            Texture = texture, Position = Vector2.Zero,
            Size = texture.GetSize() * (OpenSize.X / texture.GetWidth())
        };
    }

    private void RefreshCards()
    {
        if (!_built || _inventory == null)
            return;
        StopCardsAnimation();
        if (!string.IsNullOrEmpty(_draggedId) && !_inventory.Contains(_draggedId))
            CancelGesture();
        foreach (string id in _cards.Keys.Where(id => !_inventory.Contains(id)).ToArray())
        {
            MaterialCard removed = _cards[id];
            _cards.Remove(id);
            removed.Hide();
            removed.QueueFree();
        }
        bool animate = _animateNextRefresh && _inventory.Count > 0;
        Tween tween = animate ? CreateTween().SetParallel() : null;
        if (animate)
        {
            _cardsTween = tween;
            _cardsAnimating = true;
            var completion = new TaskCompletionSource<bool>();
            _cardsCompletion = completion;
            tween.Finished += () => { _cardsAnimating = false; completion.TrySetResult(true); };
        }
        for (int index = 0; index < _inventory.Count; index++)
        {
            MaterialEntry entry = _inventory.Entries[index];
            Vector2 destination = new(index * CardStep, 0);
            if (!_cards.TryGetValue(entry.MaterialId, out MaterialCard card))
            {
                card = new MaterialCard { Name = $"Card_{_nextCardNumber++}", Size = CardSize, Position = destination };
                _cardContent.AddChild(card);
                _cards.Add(entry.MaterialId, card);
                if (animate && entry.MaterialId == _incomingId)
                    card.Position = new Vector2(-CardStep, 0);
            }
            card.SetEntry(entry);
            card.Visible = entry.MaterialId != _draggedId;
            card.Modulate = Colors.White;
            if (animate)
                tween.TweenProperty(card, "position", destination, 0.36)
                    .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
            else
                card.Position = destination;
        }
        _countLabel.Text = $"{_inventory.Count}/{MaterialInventory.Capacity}";
        SetScrollOffset(_scrollOffset);
    }

    private void StopCardsAnimation()
    {
        _cardsTween?.Kill();
        _cardsTween = null;
        _cardsAnimating = false;
        _cardsCompletion?.TrySetResult(true);
        _cardsCompletion = null;
    }

    private void SetScrollOffset(float offset)
    {
        _scrollOffset = Mathf.Clamp(offset, 0, MaxScrollOffset);
        if (!_built)
            return;
        _cardContent.Position = new Vector2(-_scrollOffset, 0);
        int first = Mathf.Min(_inventory.Count, Mathf.FloorToInt(_scrollOffset / CardStep) + 1);
        int last = Mathf.Min(_inventory.Count, first + MaterialInventory.PageSize - 1);
        _rangeLabel.Text = _inventory.Count == 0 ? "0 / 14" : $"{first}–{last} / {_inventory.Count}";
    }

    private void OnCardsInput(InputEvent input)
    {
        // Handle the native pointer once, instead of capturing its emulated twin.
        if (input.Device == InputEvent.DeviceIdEmulation)
            return;
        if (!_interactionEnabled || !IsOpen || _pointer != -2 || _cardsAnimating
            || (_layoutTween?.IsRunning() ?? false))
            return;
        if (input is InputEventMouseButton mouse && mouse.ButtonIndex == MouseButton.Left && mouse.Pressed)
        {
            StartPointer(-1, _cardsRoot.GetGlobalTransformWithCanvas() * mouse.Position);
            _cardsRoot.AcceptEvent();
        }
        else if (input is InputEventScreenTouch touch && touch.Pressed && !touch.Canceled)
        {
            StartPointer(touch.Index, _cardsRoot.GetGlobalTransformWithCanvas() * touch.Position);
            _cardsRoot.AcceptEvent();
        }
    }

    private void StartPointer(int pointer, Vector2 canvasPosition)
    {
        _pointer = pointer;
        _gesture = GestureMode.Pending;
        _holdElapsed = 0;
        _pressCanvas = _lastCanvas = canvasPosition;
        _scrollAtPress = _scrollOffset;
        _gestureTransform = _cardsRoot.GetGlobalTransformWithCanvas();
        _pressedId = "";
        Vector2 local = _cardsRoot.GetGlobalTransformWithCanvas().AffineInverse() * canvasPosition;
        float contentX = local.X + _scrollOffset;
        int index = Mathf.FloorToInt(contentX / CardStep);
        if (index >= 0 && index < _inventory.Count && contentX - index * CardStep <= CardSize.X
            && local.Y >= 0 && local.Y <= CardSize.Y)
            _pressedId = _inventory.Entries[index].MaterialId;
    }

    public override void _Input(InputEvent input)
    {
        if (_pointer == -2 || input.Device == InputEvent.DeviceIdEmulation)
            return;
        if (_cardsRoot.GetGlobalTransformWithCanvas() != _gestureTransform)
        {
            CancelGesture();
            return;
        }
        bool handled = false;
        if (_pointer == -1 && input is InputEventMouseMotion motion)
        {
            MovePointer(motion.Position);
            handled = true;
        }
        else if (_pointer == -1 && input is InputEventMouseButton mouse
            && mouse.ButtonIndex == MouseButton.Left && !mouse.Pressed)
        {
            FinishPointer(mouse.Position);
            handled = true;
        }
        else if (input is InputEventScreenDrag drag && drag.Index == _pointer)
        {
            MovePointer(drag.Position);
            handled = true;
        }
        else if (input is InputEventScreenTouch touch && touch.Index == _pointer && (!touch.Pressed || touch.Canceled))
        {
            if (touch.Canceled)
                CancelGesture();
            else
                FinishPointer(touch.Position);
            handled = true;
        }
        if (handled)
            GetViewport().SetInputAsHandled();
    }

    private Vector2 ToLocalCanvas(Vector2 canvasPosition) => GetGlobalTransformWithCanvas().AffineInverse() * canvasPosition;

    private void MovePointer(Vector2 canvasPosition)
    {
        _lastCanvas = canvasPosition;
        Vector2 difference = ToLocalCanvas(canvasPosition) - ToLocalCanvas(_pressCanvas);
        if (_gesture == GestureMode.Pending)
        {
            if (!string.IsNullOrEmpty(_pressedId) && Mathf.Abs(difference.Y) >= GestureDistance
                && Mathf.Abs(difference.Y) > Mathf.Abs(difference.X) * 0.75f)
                BeginCardDrag();
            else if (Mathf.Abs(difference.X) >= GestureDistance)
                _gesture = GestureMode.Scrolling;
        }
        if (_gesture == GestureMode.Scrolling)
            SetScrollOffset(_scrollAtPress - difference.X);
        else if (IsCardDragging)
            UpdateCardDrag();
    }

    public override void _Process(double delta)
    {
        if (_pointer != -2 && _cardsRoot.GetGlobalTransformWithCanvas() != _gestureTransform)
        {
            CancelGesture();
            return;
        }
        if (_gesture == GestureMode.Pending && !string.IsNullOrEmpty(_pressedId))
        {
            _holdElapsed += delta;
            if (_holdElapsed >= HoldDuration)
                BeginCardDrag();
        }
        if (!IsCardDragging)
            return;
        Vector2 local = _cardsRoot.GetGlobalTransformWithCanvas().AffineInverse() * _lastCanvas;
        if (IsInCards(local))
        {
            float direction = local.X < 38 ? -1 : local.X > _cardsRoot.Size.X - 38 ? 1 : 0;
            if (direction != 0)
                SetScrollOffset(_scrollOffset + direction * 620 * (float)delta);
        }
        UpdateCardDrag();
    }

    private void BeginCardDrag()
    {
        if (!_cards.TryGetValue(_pressedId, out MaterialCard source) || !_inventory.Contains(_pressedId))
        {
            CancelGesture();
            return;
        }
        _gesture = GestureMode.Dragging;
        _draggedId = _pressedId;
        _grabOffset = ToLocalCanvas(_pressCanvas) - ToLocalCanvas(source.GetGlobalTransformWithCanvas() * Vector2.Zero);
        _dragPreview.SetEntry(source.Entry);
        _dragPreview.Show();
        source.Hide();
        _deleteZone.Visible = !IsNotebookMode;
        UpdateCardDrag();
    }

    private void UpdateCardDrag()
    {
        Vector2 local = ToLocalCanvas(_lastCanvas);
        _dragPreview.Position = local - _grabOffset;
        Vector2 cardsLocal = _cardsRoot.GetGlobalTransformWithCanvas().AffineInverse() * _lastCanvas;
        bool inCards = IsInCards(cardsLocal);
        bool extracting = IsNotebookMode && !inCards;
        _dragPreview.Visible = !extracting;
        _patternPreview.Visible = extracting;
        if (extracting && _cards.TryGetValue(_draggedId, out MaterialCard source))
        {
            source.Show();
            _patternPreview.Texture = MaterialPreviewTexture?.Invoke(source.Entry) ?? source.Entry.Texture;
            _patternPreview.FlipH = source.Entry.Scale.X < 0;
            _patternPreview.FlipV = source.Entry.Scale.Y < 0;
            Vector2 previewSize = MaterialPreviewSize?.Invoke(source.Entry) ?? new Vector2(180, 180);
            _patternPreview.Size = previewSize;
            _patternPreview.Position = local - previewSize / 2;
            _patternPreview.Modulate = MaterialDropTarget?.Invoke(_lastCanvas) == true
                ? Colors.White : new Color(1, 1, 1, 0.65f);
        }
        else if (_cards.TryGetValue(_draggedId, out MaterialCard insideSource))
            insideSource.Hide();
        IsDeleteHovered = !IsNotebookMode && !inCards && IsInDeleteArea(local);
        _deleteZone.Modulate = IsDeleteHovered ? new Color(1, 0.86f, 0.89f) : Colors.White;
        _deleteLabel.Text = IsDeleteHovered ? "松手删除" : "拖到白色区域，松手删除";
        _dragPreview.Modulate = IsDeleteHovered ? new Color(0.65f, 0.65f, 0.65f) : Colors.White;
        _insertionMarker.Visible = inCards;
        if (inCards)
        {
            int destination = InsertionIndex(cardsLocal.X + _scrollOffset);
            var remaining = _inventory.Entries.Where(entry => entry.MaterialId != _draggedId).ToArray();
            float boundary;
            if (remaining.Length == 0 || destination == 0)
                boundary = 0;
            else if (destination == remaining.Length)
                boundary = _cards[remaining[^1].MaterialId].Position.X + CardSize.X + (CardStep - CardSize.X) / 2;
            else
                boundary = (_cards[remaining[destination - 1].MaterialId].Position.X + CardSize.X
                    + _cards[remaining[destination].MaterialId].Position.X) / 2;
            _insertionMarker.Position = new Vector2(boundary - _scrollOffset, 0);
        }
    }

    private int InsertionIndex(float contentX)
    {
        int before = 0;
        foreach (MaterialEntry entry in _inventory.Entries)
            if (entry.MaterialId != _draggedId && contentX > _cards[entry.MaterialId].Position.X + CardSize.X / 2)
                before++;
        return before;
    }

    private bool IsInCards(Vector2 local) => new Rect2(Vector2.Zero, _cardsRoot.Size).HasPoint(local);

    private bool IsInDeleteArea(Vector2 local)
    {
        // Only the exposed white area is a delete target, never the drawer on top of it.
        Rect2 exposed = new(_deleteZone.Position + new Vector2(24, 22),
            new Vector2(_deleteZone.Size.X - 48, Mathf.Max(0, _panel.Position.Y - _deleteZone.Position.Y - 34)));
        return exposed.HasPoint(local);
    }

    private void FinishPointer(Vector2 canvasPosition)
    {
        _lastCanvas = canvasPosition;
        if (!IsCardDragging)
        {
            CancelGesture();
            return;
        }
        UpdateCardDrag();
        string id = _draggedId;
        Vector2 cardsLocal = _cardsRoot.GetGlobalTransformWithCanvas().AffineInverse() * canvasPosition;
        bool insert = IsInCards(cardsLocal);
        bool delete = IsDeleteHovered;
        int destination = insert ? InsertionIndex(cardsLocal.X + _scrollOffset) : -1;
        MaterialEntry extracted = IsNotebookMode && !insert && _cards.TryGetValue(id, out MaterialCard source)
            ? source.Entry : null;
        CancelGesture();
        if (extracted != null)
        {
            MaterialDropReceiver?.Invoke(extracted, canvasPosition);
            return;
        }
        _animateNextRefresh = true;
        if (insert)
            _inventory.TryMove(id, destination);
        else if (delete)
            _inventory.TryRemove(id);
        _animateNextRefresh = false;
    }

    private void CancelGesture()
    {
        if (!string.IsNullOrEmpty(_draggedId) && _cards.TryGetValue(_draggedId, out MaterialCard source))
            source.Show();
        _pointer = -2;
        _gesture = GestureMode.None;
        _holdElapsed = 0;
        _pressedId = _draggedId = "";
        IsDeleteHovered = false;
        if (!_built)
            return;
        _deleteZone.Hide();
        _deleteZone.Modulate = Colors.White;
        _insertionMarker.Hide();
        _dragPreview.Hide();
        _dragPreview.Modulate = Colors.White;
        _patternPreview.Hide();
    }

    private Vector2 PanelPosition()
    {
        Vector2 logicalSize = Size.X > 0 && Size.Y > 0 ? Size : new Vector2(2430, 1080);
        return new Vector2((logicalSize.X - OpenSize.X) / 2,
            logicalSize.Y - (IsOpen ? OpenSize.Y : FoldSize.Y));
    }

    private void PlaceDeleteZone() => _deleteZone.Position = new Vector2((Size.X - _deleteZone.Size.X) / 2, PanelPosition().Y - 280);

    private void PlacePanel()
    {
        _panel.Position = PanelPosition();
        PlaceDeleteZone();
    }
}
