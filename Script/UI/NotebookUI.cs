using Godot;
using System;

/// <summary>The notebook shell and page surface; the Level owns content and the shared inventory.</summary>
public partial class NotebookUI : Control
{
    private static readonly Vector2 ReferenceBookSize = new(1320, 1028);
    private static readonly Color IconColor = new(0.29f, 0.27f, 0.23f);
    private const float PageMargin = 26;
    private const float SideSpace = 310;

    [Export] public Texture2D NotebookTexture { get; set; }
    [Export] public NotebookContent Content { get; set; } = new();

    public PuzzleBoard Board { get; private set; }
    public int PlacedCount => Board?.Pieces.Count ?? 0;
    public int MatchedCount => Board?.MatchedCount ?? 0;
    public int TargetCount => Board?.TargetCount ?? 0;
    public bool IsDeleteHovered { get; private set; }
    public string ResolvedTitle => Board?.CurrentLevel == null
        ? Content?.Title ?? "拼接笔记本"
        : $"{Board.CurrentLevel.DisplayName} · {Content?.Title ?? "拼接笔记本"}";
    public string CurrentDeletePrompt => IsDeleteHovered
        ? Content?.DeleteHoverPrompt ?? "松手删除碎片"
        : Content?.DeletePrompt ?? "拖到这里删除纹样碎片";
    public event Action CloseRequested;

    private TextureRect _book;
    private Panel _deleteZone;
    private Button _deleteButton;
    private Button _closeButton;
    private Node2D _closeGlyph;
    private Node2D _trashGlyph;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var shade = new ColorRect
        {
            Name = "Backdrop", Color = new Color(0.23f, 0.3f, 0.25f, 0.96f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(shade);
        shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _book = new TextureRect
        {
            Name = "BookArtwork", ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Texture = NotebookTexture, Size = ReferenceBookSize, MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_book);
        Board = new PuzzleBoard { Name = "PuzzleBoard", PuzzleScale = 1, Size = ReferenceBookSize };
        AddChild(Board);

        _deleteZone = new Panel { Name = "PieceDeleteZone", MouseFilter = MouseFilterEnum.Ignore };
        _deleteZone.AddThemeStyleboxOverride("panel", RoundedPanel(Colors.White, 18));
        AddChild(_deleteZone);
        _deleteButton = new Button
        {
            Name = "DeleteSelectionButton", Text = "", TooltipText = "", FocusMode = FocusModeEnum.None
        };
        foreach (string state in new[] { "normal", "hover", "pressed", "disabled", "focus" })
            _deleteButton.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        _deleteZone.AddChild(_deleteButton);
        _deleteButton.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _deleteButton.Pressed += () => Board.DeleteSelected();
        _trashGlyph = new Node2D { Name = "TrashGlyph" };
        _deleteButton.AddChild(_trashGlyph);
        AddStroke(_trashGlyph, "Lid", new Vector2(14, 22), new Vector2(66, 22));
        AddStroke(_trashGlyph, "Handle", new Vector2(32, 20), new Vector2(34, 14), new Vector2(46, 14), new Vector2(48, 20));
        AddStroke(_trashGlyph, "Bin", new Vector2(21, 27), new Vector2(25, 66), new Vector2(55, 66), new Vector2(59, 27));
        AddStroke(_trashGlyph, "LeftSlot", new Vector2(32, 34), new Vector2(34, 58));
        AddStroke(_trashGlyph, "RightSlot", new Vector2(48, 34), new Vector2(46, 58));

        _closeButton = new Button { Name = "CloseButton", Text = "", TooltipText = "", FocusMode = FocusModeEnum.None };
        _closeButton.AddThemeStyleboxOverride("normal", RoundedPanel(new Color(1, 0.99f, 0.96f), 38));
        _closeButton.AddThemeStyleboxOverride("hover", RoundedPanel(Colors.White, 38));
        _closeButton.AddThemeStyleboxOverride("pressed", RoundedPanel(new Color(0.88f, 0.89f, 0.84f), 38));
        _closeButton.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        AddChild(_closeButton);
        _closeButton.Pressed += () => CloseRequested?.Invoke();
        _closeGlyph = new Node2D { Name = "CloseGlyph" };
        _closeButton.AddChild(_closeGlyph);
        AddStroke(_closeGlyph, "ForwardStroke", new Vector2(25, 25), new Vector2(51, 51));
        AddStroke(_closeGlyph, "BackwardStroke", new Vector2(51, 25), new Vector2(25, 51));

        Board.DeleteDropTarget = point => IsVisibleInTree()
            && new Rect2(Vector2.Zero, _deleteZone.Size).HasPoint(_deleteZone.GetGlobalTransformWithCanvas().AffineInverse() * point);
        Board.DeleteHoverChanged += SetDeleteHovered;
        Board.Changed += UpdateSelection;
        Resized += UpdateLayout;
        UpdateLayout();
        UpdateSelection();
    }

    public void BindLevel(Level level) => Board.BindLevel(level);

    private void UpdateLayout()
    {
        if (_book == null || Board == null || _closeButton == null || _deleteZone == null)
            return;
        float scale = Mathf.Max(0.01f, Mathf.Min(
            Mathf.Max(1, Size.Y - PageMargin * 2) / ReferenceBookSize.Y,
            Mathf.Max(1, Size.X - PageMargin * 2) / (ReferenceBookSize.X + SideSpace * 2)));
        Vector2 bookSize = ReferenceBookSize * scale;
        Vector2 bookPosition = (Size - bookSize) / 2;
        _book.Position = bookPosition;
        _book.Size = bookSize;
        // Set the content scale before Size emits the Board's layout signal.
        Board.PuzzleScale = scale;
        Board.Position = bookPosition;
        Board.Size = bookSize;

        _closeButton.Position = new Vector2(bookPosition.X + bookSize.X + 24 * scale, bookPosition.Y);
        _closeButton.Size = Vector2.One * 76 * scale;
        _closeGlyph.Scale = Vector2.One * scale;
        _deleteZone.Position = new Vector2(bookPosition.X + bookSize.X + 44 * scale, Size.Y / 2 - 118 * scale);
        _deleteZone.Size = new Vector2(240, 236) * scale;
        _trashGlyph.Position = _deleteZone.Size / 2 - Vector2.One * 40 * scale;
        _trashGlyph.Scale = Vector2.One * scale;
    }

    private void SetDeleteHovered(bool hovered)
    {
        IsDeleteHovered = hovered;
        _deleteZone.Modulate = hovered ? new Color(1, 0.8f, 0.82f) : Colors.White;
    }

    private void UpdateSelection() => _deleteButton.Disabled = Board.SelectedPiece == null
        || Board.SelectedPiece.IsMatched || Board.IsPieceDragging;

    private static StyleBoxFlat RoundedPanel(Color color, int radius) => new()
    {
        BgColor = color, CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius,
        CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius
    };

    private static void AddStroke(Node parent, string name, params Vector2[] points)
    {
        parent.AddChild(new Line2D
        {
            Name = name, Points = points, Width = 4, DefaultColor = IconColor, Antialiased = true,
            BeginCapMode = Line2D.LineCapMode.Round, EndCapMode = Line2D.LineCapMode.Round,
            JointMode = Line2D.LineJointMode.Round
        });
    }
}
