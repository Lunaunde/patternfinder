using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Notebook interaction surface. Authored targets stay in Level; player copies stay on their page.</summary>
public partial class PuzzleBoard : Control
{
    private sealed class Page
    {
        public Node2D Root;
        public Node2D PiecesRoot;
        public Control Finish;
        public readonly List<PuzzlePiece> Pieces = new();
    }

    [Export] public float PuzzleScale { get; set; } = 0.75f;
    public Level CurrentLevel { get; private set; }
    public IReadOnlyList<PuzzlePiece> Pieces => _page?.Pieces.AsReadOnly() ?? (IReadOnlyList<PuzzlePiece>)Array.Empty<PuzzlePiece>();
    public PuzzlePiece SelectedPiece { get; private set; }
    public bool IsPieceDragging => _dragged != null;
    public int MatchedCount => _page?.Pieces.Count(piece => piece.IsMatched) ?? 0;
    public int TargetCount => CurrentLevel?.Puzzle?.Targets.Count ?? 0;
    public bool IsFinished => _page != null && TargetCount > 0
        && CurrentLevel.Puzzle.Targets.All(target => _page.Pieces.Any(piece => piece.MatchedTarget == target));
    public Func<Vector2, bool> DeleteDropTarget { get; set; }
    public event Action Changed;
    public event Action<bool> DeleteHoverChanged;

    private readonly Dictionary<Level, Page> _pages = new();
    private Node2D _content;
    private Page _page;
    private int _pieceNumber;
    private int _pointer = -2;
    private PuzzlePiece _dragged;
    private Vector2 _grabOffset;
    private Vector2 _originalPosition;
    private float _originalRotation;
    private Transform2D _gestureTransform;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        // A piece follows the pointer outside the page while heading for the deletion area.
        ClipContents = false;
        _content = new Node2D { Name = "PuzzleContent" };
        AddChild(_content);
        Resized += UpdateLayout;
        GuiInput += OnBoardInput;
        GetViewport().SizeChanged += CancelDrag;
        UpdateLayout();
    }

    public override void _ExitTree() => GetViewport().SizeChanged -= CancelDrag;

    public override void _Notification(int what)
    {
        if (what == NotificationWMWindowFocusOut || (what == NotificationVisibilityChanged && !IsVisibleInTree()))
            CancelDrag();
    }

    private void UpdateLayout()
    {
        CancelDrag();
        if (_content == null)
            return;
        _content.Position = Size / 2;
        _content.Scale = Vector2.One * Mathf.Max(0.01f, PuzzleScale);
    }

    public void BindLevel(Level level)
    {
        CancelDrag();
        Select(null);
        if (_page != null)
            _page.Root.Hide();
        CurrentLevel = level;
        _page = null;
        if (level != null && _content != null)
        {
            if (!_pages.TryGetValue(level, out _page))
            {
                _page = new Page { Root = new Node2D { Name = $"LevelPage_{_pages.Count}" } };
                _content.AddChild(_page.Root);
                BuildBase(level.Puzzle, _page.Root);
                _page.PiecesRoot = new Node2D { Name = "Pieces" };
                _page.Root.AddChild(_page.PiecesRoot);
                if (level.Puzzle?.PuzzleFinish != null)
                {
                    _page.Finish = (Control)level.Puzzle.PuzzleFinish.Duplicate();
                    _page.Finish.Hide();
                    _page.Root.AddChild(_page.Finish);
                }
                _pages.Add(level, _page);
            }
            _page.Root.Show();
        }
        NotifyChanged();
    }

    private static Transform2D TargetTransform(Puzzle puzzle, Node2D target)
    {
        Transform2D transform = Transform2D.Identity;
        Node current = target;
        while (current != null && current != puzzle)
        {
            if (current is Node2D node)
                transform = node.Transform * transform;
            current = current.GetParent();
        }
        return transform;
    }

    private static void BuildBase(Puzzle puzzle, Node2D root)
    {
        if (puzzle == null)
            return;
        // Copy the container itself so every authored transform and explicit visibility
        // setting survives. The Level's hidden Puzzle parent is intentionally not copied.
        CopyBaseSubtree(puzzle.GetNodeOrNull<Node2D>("PuzzleBases"), root);
        // Older scenes may still place a single visual base directly under Puzzle.
        CopyBaseSubtree(puzzle.GetNodeOrNull<Node2D>("PuzzleBase"), root);
        // Targets are validation data. Player pieces alone render the target artwork during play.
    }

    private static void CopyBaseSubtree(Node2D authoredBase, Node2D root)
    {
        if (authoredBase != null && authoredBase is not EditorOnlyPreview && authoredBase is not PuzzleBlock)
        {
            var displayBase = (Node2D)authoredBase.Duplicate();
            RemoveEditorPreviews(displayBase);
            root.AddChild(displayBase);
        }
    }

    private static void RemoveEditorPreviews(Node root)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is EditorOnlyPreview || child is PuzzleBlock)
                child.Free();
            else
                RemoveEditorPreviews(child);
        }
    }

    public bool ContainsCanvasPoint(Vector2 canvasPosition) => IsVisibleInTree()
        && new Rect2(Vector2.Zero, Size).HasPoint(GetGlobalTransformWithCanvas().AffineInverse() * canvasPosition);

    public Vector2 CanvasToPuzzle(Vector2 canvasPosition) => _content.GetGlobalTransformWithCanvas().AffineInverse() * canvasPosition;
    public Vector2 PuzzleToCanvas(Vector2 puzzlePosition) => _content.GetGlobalTransformWithCanvas() * puzzlePosition;

    private void ResolvePieceArt(MaterialEntry entry, out Texture2D texture, out Vector2 scale)
    {
        // The collected variant defines its default artwork and size, independently of matching targets.
        texture = entry.Texture;
        scale = entry.Scale;
    }

    public Vector2 GetMaterialPreviewSize(MaterialEntry entry)
    {
        ResolvePieceArt(entry, out Texture2D texture, out Vector2 scale);
        return texture.GetSize() * scale.Abs() * _content.Scale;
    }

    public Texture2D GetMaterialPreviewTexture(MaterialEntry entry)
    {
        ResolvePieceArt(entry, out Texture2D texture, out _);
        return texture;
    }

    /// <summary>Only the current Level's actual inventory entries may generate a piece.</summary>
    public bool TryPlaceMaterial(Level sourceLevel, MaterialEntry entry, Vector2 canvasPosition, out PuzzlePiece piece)
    {
        piece = null;
        if (_page == null || _dragged != null || sourceLevel == null || !ReferenceEquals(sourceLevel, CurrentLevel)
            || entry == null || !sourceLevel.OwnsMaterial(entry.MaterialId)
            || !MaterialEntry.IsUsableScale(entry.Scale)
            || !sourceLevel.Inventory.Entries.Any(material => ReferenceEquals(material, entry))
            || !ContainsCanvasPoint(canvasPosition))
            return false;
        ResolvePieceArt(entry, out Texture2D texture, out Vector2 scale);
        piece = new PuzzlePiece { Name = $"Piece_{_pieceNumber++}", Position = CanvasToPuzzle(canvasPosition) };
        piece.Initialize(sourceLevel, entry, texture, scale);
        _page.PiecesRoot.AddChild(piece);
        _page.Pieces.Add(piece);
        Select(piece);
        MatchPiece(piece);
        NotifyChanged();
        return true;
    }

    private void Select(PuzzlePiece piece)
    {
        if (piece?.IsMatched == true)
            piece = null;
        SelectedPiece?.SetSelected(false);
        SelectedPiece = piece;
        piece?.SetSelected(true);
    }

    private void OnBoardInput(InputEvent input)
    {
        if (_pointer != -2 || input.Device == InputEvent.DeviceIdEmulation)
            return;
        if (input is InputEventMouseButton mouse && mouse.ButtonIndex == MouseButton.Left && mouse.Pressed)
        {
            BeginDrag(-1, GetGlobalTransformWithCanvas() * mouse.Position);
            AcceptEvent();
        }
        else if (input is InputEventScreenTouch touch && touch.Pressed && !touch.Canceled)
        {
            BeginDrag(touch.Index, GetGlobalTransformWithCanvas() * touch.Position);
            AcceptEvent();
        }
    }

    private void BeginDrag(int pointer, Vector2 canvasPosition)
    {
        PuzzlePiece piece = _page?.Pieces.LastOrDefault(candidate => !candidate.IsMatched
            && candidate.ContainsCanvasPoint(canvasPosition));
        Select(piece);
        if (piece != null)
        {
            _pointer = pointer;
            _dragged = piece;
            _gestureTransform = _content.GetGlobalTransformWithCanvas();
            _grabOffset = piece.Position - CanvasToPuzzle(canvasPosition);
            _originalPosition = piece.Position;
            _originalRotation = piece.Rotation;
            _page.Pieces.Remove(piece);
            _page.Pieces.Add(piece);
            _page.PiecesRoot.MoveChild(piece, -1);
            piece.ZIndex = 20;
        }
        NotifyChanged();
    }

    public override void _Input(InputEvent input)
    {
        if (!IsVisibleInTree() || input.Device == InputEvent.DeviceIdEmulation)
            return;
        if (_pointer == -2)
        {
            if (input is InputEventKey key && key.Pressed && !key.Echo
                && (key.PhysicalKeycode == Key.Delete || key.PhysicalKeycode == Key.Backspace) && DeleteSelected())
                GetViewport().SetInputAsHandled();
            return;
        }
        if (_content.GetGlobalTransformWithCanvas() != _gestureTransform)
        {
            CancelDrag();
            return;
        }
        bool handled = true;
        if (_pointer == -1 && input is InputEventMouseMotion motion)
            MoveDrag(motion.Position);
        else if (_pointer == -1 && input is InputEventMouseButton mouse && mouse.ButtonIndex == MouseButton.Left && !mouse.Pressed)
            FinishDrag(mouse.Position);
        else if (input is InputEventScreenDrag drag && drag.Index == _pointer)
            MoveDrag(drag.Position);
        else if (input is InputEventScreenTouch touch && touch.Index == _pointer && (!touch.Pressed || touch.Canceled))
        {
            if (touch.Canceled) CancelDrag();
            else FinishDrag(touch.Position);
        }
        else handled = false;
        if (handled)
            GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        if (_dragged != null && _content.GetGlobalTransformWithCanvas() != _gestureTransform)
            CancelDrag();
    }

    private void MoveDrag(Vector2 canvasPosition)
    {
        _dragged.Position = CanvasToPuzzle(canvasPosition) + _grabOffset;
        DeleteHoverChanged?.Invoke(DeleteDropTarget?.Invoke(canvasPosition) == true);
    }

    private void FinishDrag(Vector2 canvasPosition)
    {
        PuzzlePiece piece = _dragged;
        MoveDrag(canvasPosition);
        if (DeleteDropTarget?.Invoke(canvasPosition) == true)
        {
            ClearDrag();
            RemovePiece(piece);
        }
        else if (ContainsCanvasPoint(canvasPosition))
        {
            MatchPiece(piece);
            ClearDrag();
            NotifyChanged();
        }
        else CancelDrag();
    }

    private void MatchPiece(PuzzlePiece piece)
    {
        if (piece.IsMatched)
            return;
        if (ReferenceEquals(piece.SourceLevel, CurrentLevel) && CurrentLevel.Puzzle != null
            && CurrentLevel.Puzzle.TryFindMatch(piece.MaterialId, piece.Position, piece.Rotation,
                out PuzzleBlock target, candidate => !_page.Pieces.Any(other => other != piece && other.MatchedTarget == candidate)))
        {
            CurrentLevel.Puzzle.TryGetTargetPose(target, out Vector2 position, out float rotation);
            piece.Position = position;
            piece.Rotation = rotation;
            piece.ApplyTargetArtwork(target, TargetTransform(CurrentLevel.Puzzle, target).Scale);
            piece.MatchedTarget = target;
            if (SelectedPiece == piece)
                Select(null);
        }
        piece.QueueRedraw();
    }

    public void CancelDrag()
    {
        if (_dragged == null)
            return;
        _dragged.Position = _originalPosition;
        _dragged.Rotation = _originalRotation;
        _dragged.QueueRedraw();
        ClearDrag();
        NotifyChanged();
    }

    private void ClearDrag()
    {
        if (_dragged != null)
            _dragged.ZIndex = 0;
        _dragged = null;
        _pointer = -2;
        DeleteHoverChanged?.Invoke(false);
    }

    public bool DeleteSelected()
    {
        if (_dragged != null || SelectedPiece == null || SelectedPiece.IsMatched)
            return false;
        RemovePiece(SelectedPiece);
        return true;
    }

    private void RemovePiece(PuzzlePiece piece)
    {
        if (piece.IsMatched)
            return;
        if (piece == SelectedPiece)
            Select(null);
        _page.Pieces.Remove(piece);
        piece.Hide();
        piece.QueueFree();
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        bool finished = IsFinished;
        CurrentLevel?.Puzzle?.SetFinished(finished);
        if (_page?.Finish != null)
            _page.Finish.Visible = finished;
        Changed?.Invoke();
    }
}
