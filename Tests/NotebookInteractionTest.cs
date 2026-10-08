using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>Exercises the actual notebook and shared card bar through native mouse/touch routing.</summary>
public partial class NotebookInteractionTest : Node
{
    private Main _main;
    private Bar _bar;
    private PuzzleBoard _board;
    private MaterialEntry _entry;
    private Vector2 _mouse;
    private bool _mouseDown;
    private int _checks;
    private bool _finished;
    private string _stage = "startup";
    private const string UiPath = "UILayer/AspectRatioContainer/UIRoot/DesignRoot";

    public override async void _Ready()
    {
        GetTree().CreateTimer(90).Timeout += () =>
        {
            if (!_finished) { GD.PushError($"Notebook test timed out: {_stage}"); GetTree().Quit(1); }
        };
        try
        {
            Check(DisplayServer.GetName() != "headless", "Notebook gestures use the actual window and renderer.");
            GetWindow().Mode = Window.ModeEnum.Windowed;
            await Resize(new Vector2I(1260, 560));
            _main = GetNode<Main>("Main");
            _bar = _main.GetNode<Bar>($"{UiPath}/MaterialBar");
            await PhotographFirstMaterial();
            foreach (string id in _main.CurrentLevel.GetPatterns().SelectMany(pattern => pattern.Variants).Select(variant => variant.MaterialId).Distinct())
            {
                Check(_main.CurrentLevel.TryResolveMaterial(id, out MaterialEntry entry), "The sample material is resolved inside its Level.");
                if (!_main.Inventory.Contains(entry.MaterialId))
                    Check(_main.Inventory.TryAdd(entry.MaterialId, entry.DisplayName, entry.Texture, entry.Scale), "The test collects the other real authored material.");
            }
            _entry = _main.Inventory.Entries[0];
            PatternVariant authored = _main.CurrentLevel.GetPatterns()[0].Variants.Single();
            Check(_entry.Texture == authored.MaterialTexture && _entry.Scale.DistanceTo(authored.Scale) < 0.0001f
                && _entry.Scale.DistanceTo(Vector2.One * 0.25f) < 0.0001f,
                "The actual photograph stores the authored motif texture and quarter-size scale in inventory.");
            var note = _main.GetNode<TextureButton>($"{UiPath}/NoteIcon");
            Vector2 notePoint = note.GetGlobalTransformWithCanvas() * (note.Size / 2);
            await Down(notePoint);
            await Up(notePoint);
            Check(_main.IsNotebookOpen, "The real garden notebook button opens the page.");
            _board = _main.Notebook.Board;
            CheckNotebookLayout();
            CheckRuntimeTargets();
            CheckVisibleBase();
            await Screenshot("chatgpt-notebook-base-visible");
            Check(_board.TargetCount > 0 && _board.Pieces.Count == 0, "Authored targets retain match data without being player pieces.");
            await CheckDrawerAndGardenState();
            await CheckCardCopies();
            await CheckMatchingAndDeletion();
            await CheckTouchAndCancellation();
            await CheckLevelScope();
            await CheckResizing();
            await Screenshot("chatgpt-notebook-honeysuckle");
            _finished = true;
            GD.Print($"Notebook interaction: {_checks} checks passed.");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            _finished = true;
            GD.PushError($"Notebook interaction failed during {_stage}: {exception}");
            try { await Screenshot("chatgpt-notebook-test-failure"); } catch { }
            GetTree().Quit(1);
        }
    }

    private async Task PhotographFirstMaterial()
    {
        _stage = "real photograph before notebook";
        Check(_main.Inventory.Count == 0, "The production Level starts with no debug inventory.");
        GetViewport().PushInput(new InputEventKey { PhysicalKeycode = Key.Space, Pressed = true }, true);
        await Frames(2);
        Frame frame = _main.GetChildren().OfType<Frame>().Single();
        frame.GlobalPosition = _main.CurrentLevel.GetPatterns()[0].GlobalPosition;
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        GetViewport().PushInput(new InputEventKey { PhysicalKeycode = Key.Enter, Pressed = true }, true);
        await Frames(1);
        Check(_main.IsPhotographing, "The real Enter binding begins the photo flow on the honeysuckle prototype.");
        GetViewport().PushInput(new InputEventKey { PhysicalKeycode = Key.Space, Pressed = false }, true);
        await Until(() => !_main.IsPhotographing && _main.Inventory.Count == 1, "The actual photo flow commits a collectible into this Level's inventory.");
    }

    private async Task CheckDrawerAndGardenState()
    {
        _stage = "bottom drawer, native controls, and garden state restoration";
        MaterialEntry[] originals = _main.Inventory.Entries.ToArray();
        Check(_bar.IsOpen, "Opening the notebook exposes the shared material drawer.");
        await ToggleDrawer();
        Check(!_bar.IsOpen && !_bar.GetNode<Control>("MaterialDrawer/Cards").Visible,
            "The real drawer button folds the notebook's bottom material strip.");
        CheckNotebookLayout();
        await CloseWithButton();
        Check(!_main.IsNotebookOpen && !_bar.IsNotebookMode && _bar.IsOpen,
            "Closing a notebook folded by the player restores the garden's previously open drawer.");

        await ToggleDrawer();
        Check(!_bar.IsOpen, "The garden's own material drawer can start folded.");
        Check(_main.OpenNotebook(), "The notebook reopens from a folded garden drawer.");
        await Frames(2);
        Check(_bar.IsOpen && _bar.IsNotebookMode, "Entering the notebook opens its bottom drawer without losing the garden state.");
        await ToggleDrawer();
        await ToggleDrawer();
        Check(_bar.IsOpen, "The notebook drawer can be folded and expanded repeatedly.");
        await CloseWithButton();
        Check(!_bar.IsOpen && !_bar.IsNotebookMode && !_main.IsNotebookOpen,
            "Returning to the garden restores its previously folded state despite the notebook's open drawer.");
        Check(_main.Inventory.Entries.SequenceEqual(originals), "Changing pages and folding the drawer preserves the same inventory objects and order.");
        Check(_main.OpenNotebook(), "The notebook opens again after restoring both garden drawer states.");
        await Frames(2);
        CheckNotebookLayout();
    }

    private async Task ToggleDrawer()
    {
        Button toggle = _bar.GetNode<Button>("MaterialDrawer/ToggleDrawer");
        bool previous = _bar.IsOpen;
        Vector2 point = toggle.GetGlobalTransformWithCanvas() * (toggle.Size / 2);
        await Down(point); await Up(point);
        Check(_bar.IsOpen != previous, "Native GUI input reaches the material drawer above the notebook.");
        float expectedY = _bar.Size.Y - (_bar.IsOpen ? 276 : 105);
        await Until(() => Math.Abs(_bar.GetNode<Control>("MaterialDrawer").Position.Y - expectedY) < 0.05f,
            "The drawer's fold animation reaches its bottom-aligned destination.");
        await Frames(2);
    }

    private async Task CloseWithButton()
    {
        Button close = _main.Notebook.GetNode<Button>("CloseButton");
        Check(close.Text.Length == 0 && Descendants(close).OfType<Line2D>().Count() == 2,
            "The notebook uses two native lines for its × close icon instead of a surrounding text action.");
        Vector2 point = close.GetGlobalTransformWithCanvas() * (close.Size / 2);
        await Down(point); await Up(point);
        Check(!_main.IsNotebookOpen, "The actual × button returns to the garden.");
    }

    private async Task CheckCardCopies()
    {
        _stage = "card extraction";
        Node blocks = _main.CurrentLevel.Puzzle.GetNode("PuzzleBlocks");
        var differentTarget = new PuzzleBlock
        {
            Name = "TestDifferentTargetArtwork", MaterialId = _entry.MaterialId,
            Position = new Vector2(480, 300), Scale = new Vector2(0.63f, 0.47f),
            Texture = MakeFixtureTexture()
        };
        blocks.AddChild(differentTarget);
        blocks.MoveChild(differentTarget, 0);
        _main.CurrentLevel.Puzzle.RefreshTargets();
        try
        {
            Check(_main.CurrentLevel.Puzzle.Targets[0] == differentTarget,
                "The test puts different artwork and scale first for this MaterialId without changing the product scene.");
            await CheckCardCopiesWithDifferentTarget();
        }
        finally
        {
            blocks.RemoveChild(differentTarget);
            differentTarget.Free();
            _main.CurrentLevel.Puzzle.RefreshTargets();
        }
    }

    private async Task CheckCardCopiesWithDifferentTarget()
    {
        MaterialEntry[] originals = _main.Inventory.Entries.ToArray();
        MaterialCard card = Card(_entry.MaterialId);
        Vector2 cardPosition = card.Position;
        Vector2 freePosition = new(0, -40);
        Vector2 free = _board.PuzzleToCanvas(freePosition);
        await BeginCard(_entry.MaterialId);
        await Move(free);
        TextureRect preview = _bar.GetNode<TextureRect>("PatternDragPreview");
        Check(card.Visible && preview.Visible,
            "Outside the strip the original card stays in its slot and only the motif follows the pointer.");
        Vector2 expectedSize = _entry.Texture.GetSize() * _entry.Scale.Abs() * _board.PuzzleScale;
        Check(preview.Texture == _entry.Texture && preview.Size.DistanceTo(expectedSize) < 0.01f
            && preview.StretchMode == TextureRect.StretchModeEnum.Scale
            && preview.FlipH == (_entry.Scale.X < 0) && preview.FlipV == (_entry.Scale.Y < 0),
            "The actual drag preview uses inventory artwork and configured scale instead of the first target's artwork or scale.");
        Check(_board.GetMaterialPreviewSize(_entry).DistanceTo(expectedSize) < 0.01f,
            "The board and visible drag preview agree on the configured motif dimensions.");
        Check(!_bar.GetNode<Control>("DeleteZone").Visible, "The garden delete overlay never covers the notebook page.");
        await Screenshot("chatgpt-notebook-pattern-drag");
        await Up(free);
        Check(_board.Pieces.Count == 1 && _board.Pieces[0].MaterialId == _entry.MaterialId,
            "Releasing on the page creates one material-matched puzzle piece.");
        CheckPieceVisible(_board.Pieces[0]);
        Check(!_board.Pieces[0].IsMatched && _board.Pieces[0].Texture == _entry.Texture
            && _board.Pieces[0].Scale.DistanceTo(_entry.Scale) < 0.0001f,
            "A loose piece keeps its inventory texture and quarter-size scale even when matching targets use different art.");
        Sprite2D sprite = _board.Pieces[0].GetNode<Sprite2D>("Texture");
        Check((sprite.GetRect().Size * _board.Pieces[0].Scale.Abs() * _board.PuzzleScale).DistanceTo(expectedSize) < 0.01f,
            "The loose piece's rendered dimensions match the motif preview before it is matched.");
        Check(_board.Pieces[0].Position.DistanceTo(freePosition) < 0.01f,
            "The generated piece appears at the actual release position in puzzle coordinates.");
        Check(card.Visible && card.Position == cardPosition && ReferenceEquals(card.Entry, _entry),
            "The source card returns to exactly the same slot and object.");
        Check(_main.Inventory.Entries.SequenceEqual(originals), "Extraction does not consume, reorder, or replace inventory entries.");
        await BeginCard(_entry.MaterialId);
        await Move(_board.PuzzleToCanvas(new Vector2(-350, 220)));
        await Up(_board.PuzzleToCanvas(new Vector2(-350, 220)));
        Check(_board.Pieces.Count == 2 && _board.Pieces.All(piece => piece.MaterialId == _entry.MaterialId),
            "One inventory card can generate multiple independent pieces.");
        int count = _board.Pieces.Count;
        await BeginCard(_entry.MaterialId);
        Vector2 invalid = _main.Notebook.GetGlobalTransformWithCanvas() * new Vector2(40, 120);
        Check(!_board.ContainsCanvasPoint(invalid), "The invalid release lies outside the larger notebook page.");
        await Move(invalid); await Up(invalid);
        Check(_board.Pieces.Count == count && card.Visible, "Dropping a card outside the page creates no piece and restores the card.");
    }

    private async Task CheckMatchingAndDeletion()
    {
        _stage = "piece editing and target matching";
        PuzzlePiece piece = _board.Pieces[0];
        PuzzleBlock right = _main.CurrentLevel.Puzzle.Targets.First(target => target.MaterialId == piece.MaterialId);
        Node blocks = _main.CurrentLevel.Puzzle.GetNode("PuzzleBlocks");
        var wrong = new PuzzleBlock
        {
            Name = "TestWrongMaterialTarget", MaterialId = "integration-wrong-id",
            Position = new Vector2(260, 80), Scale = _entry.Scale, Texture = _entry.Texture
        };
        blocks.AddChild(wrong);
        _main.CurrentLevel.Puzzle.RefreshTargets();
        try
        {
            await DragPiece(piece, wrong.Position);
            Check(!piece.IsMatched, "A coincident position cannot match a different MaterialId even in a single-motif product Level.");
        }
        finally
        {
            blocks.RemoveChild(wrong);
            wrong.Free();
            _main.CurrentLevel.Puzzle.RefreshTargets();
        }
        _main.CurrentLevel.Puzzle.TryGetTargetPose(right, out Vector2 rightPosition, out _);
        await DragPiece(piece, rightPosition + new Vector2(4, 0));
        Check(piece.MatchedTarget == right && piece.Position.DistanceTo(rightPosition) < 0.001f,
            "Correct Level-local MaterialId and tolerances match and align the piece to its authored target.");
        CheckPieceVisible(piece);
        Check(!right.IsVisibleInTree(), "A successful match does not reveal its authored target in the garden or notebook.");
        Check(_board.MatchedCount == 1, "Matching updates notebook progress.");
        Check(_board.TryPlaceMaterial(_main.CurrentLevel, _entry, _board.PuzzleToCanvas(rightPosition), out PuzzlePiece extra)
            && !extra.IsMatched, "An occupied target cannot be claimed by a second piece.");
        Check(_board.DeleteSelected() && _board.Pieces.Count == 2, "Deleting a duplicate leaves the original match intact.");
        await DragPiece(piece, new Vector2(340, -210));
        Check(!piece.IsMatched && _board.MatchedCount == 0, "Matched pieces remain movable as requested, freeing their target.");
        await Down(_board.PuzzleToCanvas(piece.Position));
        await Move(DeletePoint());
        Control deleteZone = _main.Notebook.GetNode<Control>("PieceDeleteZone");
        Check(_board.DeleteDropTarget(DeletePoint()) && deleteZone.Modulate != Colors.White,
            "Hovering the white deletion region changes its visual state without adding surrounding text.");
        Check(_main.Notebook.IsDeleteHovered
            && _main.Notebook.CurrentDeletePrompt == _main.Notebook.Content.DeleteHoverPrompt,
            "Deletion hover still updates its retained text data even though no deletion label is drawn.");
        Check(!Descendants(deleteZone).OfType<Label>().Any(), "The piece deletion region contains no text label.");
        await Screenshot("chatgpt-notebook-delete-hover");
        await Up(DeletePoint());
        Check(_board.Pieces.Count == 1 && _main.Inventory.Contains(_entry.MaterialId),
            "Dragging to the white region deletes only the piece, preserving its source material card.");
        Check(deleteZone.Modulate == Colors.White, "Committing a deletion restores the white region's resting visual state.");
        Check(!_main.Notebook.IsDeleteHovered
            && _main.Notebook.CurrentDeletePrompt == _main.Notebook.Content.DeletePrompt,
            "Committing deletion also resets the retained prompt and hover data.");
        PuzzlePiece remaining = _board.Pieces[0];
        await Down(_board.PuzzleToCanvas(remaining.Position)); await Up(_board.PuzzleToCanvas(remaining.Position));
        GetViewport().PushInput(new InputEventKey { PhysicalKeycode = Key.Delete, Pressed = true }, true);
        await Frames(2);
        Check(_board.Pieces.Count == 0 && _main.Inventory.Contains(_entry.MaterialId), "Delete removes the selected piece without deleting inventory.");
        Check(_board.TryPlaceMaterial(_main.CurrentLevel, _entry,
            _board.PuzzleToCanvas(new Vector2(0, -190)), out PuzzlePiece selected),
            "The selected-piece button fixture creates one visible player piece.");
        CheckPieceVisible(selected);
        Button deleteSelected = _main.Notebook.GetNode<Button>("PieceDeleteZone/DeleteSelectionButton");
        Check(deleteSelected.Text.Length == 0 && !deleteSelected.Disabled,
            "The existing selected-piece action is available through a text-free deletion icon.");
        Vector2 deleteButtonPoint = deleteSelected.GetGlobalTransformWithCanvas() * (deleteSelected.Size / 2);
        await Down(deleteButtonPoint); await Up(deleteButtonPoint);
        Check(_board.Pieces.Count == 0 && _main.Inventory.Contains(_entry.MaterialId),
            "The actual deletion icon removes only the selected piece while retaining its material card.");
    }

    private async Task CheckTouchAndCancellation()
    {
        _stage = "touch extraction and cancellation";
        Vector2 cardPoint = CardPoint(_entry.MaterialId);
        Touch(4, cardPoint, true); await Frames(2);
        await Until(() => _bar.IsCardDragging, "A held touch begins card extraction.");
        Vector2 pagePoint = _board.PuzzleToCanvas(new Vector2(0, -190));
        TouchMove(4, cardPoint, pagePoint); await Frames(2);
        Touch(8, new Vector2(20, 20), false); await Frames(1);
        Check(_bar.IsCardDragging && _board.Pieces.Count == 0, "An unrelated touch release cannot finish the card drag.");
        GetViewport().PushInput(new InputEventMouseButton
        {
            Device = (int)InputEvent.DeviceIdEmulation, ButtonIndex = MouseButton.Left, Position = pagePoint, Pressed = false
        }, true);
        Check(_bar.IsCardDragging, "An emulated mouse release does not duplicate or complete the touch gesture.");
        Touch(4, pagePoint, false, true); await Frames(2);
        Check(!_bar.IsCardDragging && _board.Pieces.Count == 0 && Card(_entry.MaterialId).Visible,
            "Canceled card touch restores the source and never creates a piece.");
        Touch(5, cardPoint, true); await Frames(1);
        await Until(() => _bar.IsCardDragging, "A subsequent touch can extract normally.");
        TouchMove(5, cardPoint, pagePoint); Touch(5, pagePoint, false); await Frames(2);
        Check(_board.Pieces.Count == 1, "A valid touch release creates exactly one piece.");
        PuzzlePiece piece = _board.Pieces[0];
        Vector2 original = piece.Position;
        Vector2 from = _board.PuzzleToCanvas(original);
        Touch(7, from, true); await Frames(1);
        Check(!_board.TryPlaceMaterial(_main.CurrentLevel, _entry, pagePoint, out _),
            "A second pointer cannot place a new piece while another piece is being moved.");
        TouchMove(7, from, DeletePoint()); await Frames(1);
        Touch(7, DeletePoint(), false, true); await Frames(2);
        Check(_board.Pieces.Count == 1 && piece.Position == original && !_board.IsPieceDragging,
            "Canceled piece touch restores its original position rather than deleting it.");
        await Down(_board.PuzzleToCanvas(original)); await Move(DeletePoint());
        _board.Notification((int)Control.NotificationWMWindowFocusOut);
        await Up(DeletePoint());
        Check(piece.Position == original && _board.Pieces.Count == 1, "Focus loss cancels a potential piece deletion.");
        await BeginCard(_entry.MaterialId); await Move(pagePoint);
        _main.CloseNotebook(); await Up(pagePoint);
        Check(_board.Pieces.Count == 1 && !_bar.IsCardDragging && !_bar.IsNotebookMode,
            "Closing during card extraction creates no extra piece and returns the bar to the garden.");
        Check(_main.OpenNotebook() && _board.Pieces.Count == 1 && piece.Position == original,
            "Reopening the same notebook page preserves its loose pieces and positions.");
    }

    private async Task CheckLevelScope()
    {
        _stage = "level-local identity";
        Level original = _main.CurrentLevel;
        Level other = GD.Load<PackedScene>("res://Scenes/levels/honeysuckle.tscn").Instantiate<Level>();
        Puzzle serializedPuzzle = other.GetNode<Puzzle>(other.PuzzlePath);
        serializedPuzzle.RefreshTargets();
        Check(serializedPuzzle.GetNode<Sprite2D>("NotebookPreview").Visible
            && serializedPuzzle.Targets.Count > 0 && serializedPuzzle.Targets.All(target => target.Visible),
            "The authored preview and targets retain their own visible flags regardless of the user's Puzzle parent visibility.");
        other.Visible = false;
        other.ProcessMode = ProcessModeEnum.Disabled;
        AddChild(other);
        await Frames(2);
        Check(other.Inventory.Count == 0 && !ReferenceEquals(original.Inventory, other.Inventory), "Each Level owns independent inventory.");
        Check(other.TryResolveMaterial(_entry.MaterialId, out MaterialEntry local), "The same textual ID is valid in another Level.");
        Check(other.Inventory.TryAdd(local.MaterialId, "Other level", local.Texture, local.Scale), "The other Level collects its own entry and configured scale.");
        MaterialEntry foreign = other.Inventory.Entries[0];
        Vector2 destination = _board.PuzzleToCanvas(new Vector2(300, 180));
        Check(!_board.TryPlaceMaterial(other, foreign, destination, out _), "A foreign Level cannot drop onto the current page.");
        Check(!_board.TryPlaceMaterial(original, foreign, destination, out _), "A matching textual ID cannot forge ownership of a foreign inventory entry.");
        int count = _board.Pieces.Count;
        _board.BindLevel(other);
        Check(_board.Pieces.Count == 0, "A different Level starts with its own empty puzzle page.");
        PuzzleBlock target = other.Puzzle.Targets.First(block => block.MaterialId == foreign.MaterialId);
        other.Puzzle.TryGetTargetPose(target, out Vector2 position, out _);
        Check(_board.TryPlaceMaterial(other, foreign, _board.PuzzleToCanvas(position), out PuzzlePiece otherPiece)
            && otherPiece.MatchedTarget == target && otherPiece.SourceLevel == other,
            "The other Level matches only its own PuzzleBlock instance.");
        CheckPieceVisible(otherPiece);
        _board.BindLevel(original);
        Check(_board.Pieces.Count == count && _board.Pieces.All(piece => piece.SourceLevel == original),
            "Returning to the original Level restores its own pieces without scope leakage.");
        CheckRuntimeTargets();
        CheckVisibleBase();
    }

    private async Task CheckResizing()
    {
        _stage = "viewport resize and coordinate conversion";
        PuzzlePiece piece = _board.Pieces[0];
        Vector2 original = piece.Position;
        await Down(_board.PuzzleToCanvas(original)); await Move(DeletePoint());
        await Resize(new Vector2I(1080, 810));
        await Up(DeletePoint());
        Check(!_board.IsPieceDragging && piece.Position == original, "Resizing during a piece drag cancels it safely.");
        CheckNotebookLayout();
        await DragPiece(piece, new Vector2(300, 210));
        Check(piece.Position.DistanceTo(new Vector2(300, 210)) < 0.01f, "Piece movement remains correct in a 4:3 window.");
        await Screenshot("chatgpt-notebook-4x3");
        await Resize(new Vector2I(1260, 540));
        CheckNotebookLayout();
        await DragPiece(piece, new Vector2(-300, 120));
        Check(piece.Position.DistanceTo(new Vector2(-300, 120)) < 0.01f,
            "Piece movement remains correct in a 21:9 window with the full-height notebook and bottom strip.");
        CheckPieceVisible(piece);
        await Screenshot("chatgpt-notebook-21x9");
        await Resize(new Vector2I(480, 800));
        CheckNotebookLayout();
        int count = _board.Pieces.Count;
        await BeginCard(_entry.MaterialId);
        Vector2 at = _board.PuzzleToCanvas(new Vector2(-320, -180));
        await Move(at); await Up(at);
        Check(_board.Pieces.Count == count + 1 && _board.Pieces[^1].Position.DistanceTo(new Vector2(-320, -180)) < 0.01f,
            "Card extraction uses the correct centered canvas transform in portrait orientation.");
        await Screenshot("chatgpt-notebook-portrait");
        await Resize(new Vector2I(1260, 560));
        CheckNotebookLayout();
    }

    private void CheckNotebookLayout()
    {
        Control notebook = _main.Notebook;
        Control book = notebook.GetNode<Control>("BookArtwork");
        Rect2 bounds = book.GetRect();
        Check(book.Size.Y >= notebook.Size.Y * 0.94f && bounds.Position.Y >= 0
            && bounds.End.Y <= notebook.Size.Y + 0.1f,
            "The notebook artwork fills nearly the design height while staying inside the screen.");
        Check(bounds.GetCenter().DistanceTo(notebook.Size / 2) < 0.1f,
            "The large notebook artwork is centered in both directions.");
        Check(_board.GetRect().Position.DistanceTo(bounds.Position) < 0.1f
            && _board.Size.DistanceTo(book.Size) < 0.1f,
            "The puzzle input surface follows the notebook artwork instead of its former smaller page.");
        Control drawer = _bar.GetNode<Control>("MaterialDrawer");
        Check(_bar.IsNotebookMode && Math.Abs(drawer.Position.Y + (_bar.IsOpen ? 276 : 105) - notebook.Size.Y) < 0.1f
            && drawer.Position.Y > notebook.Size.Y / 2,
            "The shared material drawer stays at the bottom in notebook mode.");
        Check(drawer.Position.DistanceTo(new Vector2((notebook.Size.X - 1545) / 2,
                notebook.Size.Y - (_bar.IsOpen ? 276 : 105))) < 0.1f
            && drawer.Size.DistanceTo(new Vector2(1545, 276)) < 0.1f
            && drawer.GetNode<Control>("Cards").Position.DistanceTo(new Vector2(76, 42)) < 0.1f
            && drawer.GetNode<Button>("ToggleDrawer").Position.DistanceTo(new Vector2(1365, 0)) < 0.1f,
            $"Extended artwork preserves the original positions: drawer {drawer.Position}/{drawer.Size}, "
                + $"cards {drawer.GetNode<Control>("Cards").Position}, toggle {drawer.GetNode<Button>("ToggleDrawer").Position}.");
        TextureRect background = drawer.GetNode<TextureRect>(_bar.IsOpen ? "OpenBackground" : "FoldBackground");
        Check(background.Visible && background.Position.Length() < 0.1f
            && background.Texture.ResourcePath.Contains("_extend@3x.png")
            && background.Size.DistanceTo(new Vector2(1545, 480)) < 0.1f
            && background.Size.DistanceTo(background.Texture.GetSize()) < 0.1f,
            "Only the background extends downward; its top artwork keeps its original scale and origin.");
        Check(_bar.ZIndex > notebook.ZIndex && _bar.GetIndex() > notebook.GetIndex(),
            "The drawer draws and receives native GUI input above the notebook shell.");
        Check(!Descendants(notebook).OfType<Label>().Any(),
            "The notebook shell no longer builds surrounding titles, instructions, counters, or deletion labels.");
        Vector2 actualCenter = book.GetGlobalTransformWithCanvas() * (book.Size / 2);
        Check(actualCenter.DistanceTo(GetViewport().GetVisibleRect().GetCenter()) < 0.2f,
            "The notebook remains centered in the actual viewport at this window ratio.");
        Transform2D transform = book.GetGlobalTransformWithCanvas();
        Check(Math.Abs(transform.X.Length() - transform.Y.Length()) < 0.0001f,
            "The large notebook uses uniform scaling across different window ratios.");
        CheckNotebookData();
    }

    private void CheckNotebookData()
    {
        NotebookUI notebook = _main.Notebook;
        Check(notebook.Content != null && new[]
        {
            notebook.Content.Title, notebook.Content.Instructions, notebook.Content.DeletePrompt,
            notebook.Content.DeleteHoverPrompt, notebook.Content.BackLabel, notebook.Content.DeleteSelectionLabel
        }.All(text => !string.IsNullOrWhiteSpace(text)),
            "Notebook titles, instructions, and action descriptions remain configurable data after their labels are removed.");
        Check(!string.IsNullOrWhiteSpace(notebook.ResolvedTitle)
            && notebook.PlacedCount == _board.Pieces.Count && notebook.MatchedCount == _board.MatchedCount
            && notebook.TargetCount == _board.TargetCount,
            "Retained notebook title and progress data remain synchronized with the player board.");
        Check(notebook.GetNode<Button>("PieceDeleteZone/DeleteSelectionButton") != null,
            "The visual-only deletion button preserves the existing selected-piece deletion action.");
    }

    private void CheckRuntimeTargets()
    {
        Puzzle puzzle = _main.CurrentLevel.Puzzle;
        Check(!puzzle.Visible && !puzzle.IsVisibleInTree(), "The live garden hides its Level-owned Puzzle configuration at runtime.");
        Sprite2D preview = puzzle.GetNodeOrNull<Sprite2D>("NotebookPreview");
        Check(preview != null && preview.Texture != null && !preview.Visible && !preview.IsVisibleInTree(),
            "The authored notebook positioning image exists as data but stays hidden at runtime.");
        Vector2 previewSize = preview.Texture.GetSize() * preview.Scale.Abs();
        Check(preview.Position == Vector2.Zero && previewSize.DistanceTo(_board.Size / _board.PuzzleScale) < 1
            && _board.PuzzleToCanvas(Vector2.Zero).DistanceTo(
                _main.Notebook.GetNode<Control>("BookArtwork").GetGlobalTransformWithCanvas()
                * (_main.Notebook.GetNode<Control>("BookArtwork").Size / 2)) < 0.1f,
            "The editor positioning image and runtime board share the same local extent and origin.");
        Check(puzzle.Targets.All(target => target.Texture != null && !target.Visible && !target.IsVisibleInTree()
            && puzzle.TryGetTargetPose(target, out _, out _)),
            "All editor targets retain their textures and target poses while being hidden at runtime.");
        Check(!Descendants(_board).Any(node => node is PuzzleBlock || node.Name.ToString().StartsWith("Guide_", StringComparison.Ordinal)
            || node.Name == "NotebookPreview"),
            "The player board contains no cloned target guide or editor notebook preview.");
    }

    private void CheckPieceVisible(PuzzlePiece piece)
    {
        Sprite2D sprite = piece.GetNode<Sprite2D>("Texture");
        Check(piece.Visible && piece.IsVisibleInTree() && sprite.Visible && sprite.IsVisibleInTree()
            && sprite.Texture != null && sprite.Modulate.A > 0.99f && piece.Modulate.A > 0.99f,
            "Actual player pieces remain visible at full opacity even though their authored targets are hidden.");
    }

    private void CheckVisibleBase()
    {
        Puzzle puzzle = _main.CurrentLevel.Puzzle;
        Node2D authoredGroup = puzzle.GetNode<Node2D>("PuzzleBases");
        Sprite2D authored = authoredGroup.GetNode<Sprite2D>("PuzzleBase");
        Node2D activePage = _board.GetNode<Node2D>("PuzzleContent").GetChildren().OfType<Node2D>().Single(page => page.Visible);
        Node2D displayGroup = activePage.GetNode<Node2D>("PuzzleBases");
        Sprite2D display = displayGroup.GetNode<Sprite2D>("PuzzleBase");
        Check(authored.Texture != null && display.Texture == authored.Texture
            && display.Visible && display.IsVisibleInTree() && displayGroup.Visible,
            "The actual PuzzleBases/PuzzleBase artwork is copied into the visible notebook despite the hidden garden Puzzle parent.");
        Check(!ReferenceEquals(authored, display) && SamePose(RelativePose(authored, puzzle), RelativePose(display, activePage)),
            "The visible base is an independent display copy with every authored position, rotation and scale preserved.");
        Check(display.Offset == authored.Offset && display.Centered == authored.Centered
            && display.FlipH == authored.FlipH && display.FlipV == authored.FlipV && display.Modulate == authored.Modulate,
            "The base copy also preserves sprite offsets, centering, flips and tint.");
    }

    private static Transform2D RelativePose(Node2D item, Node ancestor)
    {
        Transform2D pose = Transform2D.Identity;
        for (Node current = item; current != ancestor; current = current.GetParent())
        {
            if (current is Node2D node)
                pose = node.Transform * pose;
        }
        return pose;
    }

    private static bool SamePose(Transform2D first, Transform2D second) => first.Origin.DistanceTo(second.Origin) < 0.0001f
        && first.X.DistanceTo(second.X) < 0.0001f && first.Y.DistanceTo(second.Y) < 0.0001f;

    private static Texture2D MakeFixtureTexture()
    {
        using Image image = Image.CreateEmpty(56, 40, false, Image.Format.Rgba8);
        image.Fill(new Color(0.72f, 0.35f, 0.31f));
        return ImageTexture.CreateFromImage(image);
    }

    private static IEnumerable<Node> Descendants(Node parent)
    {
        foreach (Node child in parent.GetChildren())
        {
            yield return child;
            foreach (Node descendant in Descendants(child))
                yield return descendant;
        }
    }

    private MaterialCard Card(string id) => _bar.GetNode<Control>("MaterialDrawer/Cards/CardContent")
        .GetChildren().OfType<MaterialCard>().Single(card => card.Entry?.MaterialId == id);
    private Vector2 CardPoint(string id) { MaterialCard card = Card(id); return card.GetGlobalTransformWithCanvas() * (card.Size / 2); }
    private Vector2 DeletePoint() { Control zone = _main.Notebook.GetNode<Control>("PieceDeleteZone"); return zone.GetGlobalTransformWithCanvas() * (zone.Size / 2); }
    private async Task BeginCard(string id) { await Down(CardPoint(id)); await Until(() => _bar.IsCardDragging, "The native GUI route starts a held card drag."); }
    private async Task DragPiece(PuzzlePiece piece, Vector2 to) { await Down(_board.PuzzleToCanvas(piece.Position)); await Move(_board.PuzzleToCanvas(to)); await Up(_board.PuzzleToCanvas(to)); }
    private async Task Down(Vector2 point)
    {
        _mouse = point; _mouseDown = true;
        GetViewport().PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, ButtonMask = MouseButtonMask.Left, Pressed = true }, true);
        await Frames(2);
    }
    private async Task Move(Vector2 point)
    {
        Vector2 relative = point - _mouse; _mouse = point;
        GetViewport().PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point, Relative = relative, ButtonMask = _mouseDown ? MouseButtonMask.Left : (MouseButtonMask)0 }, true);
        await Frames(2);
    }
    private async Task Up(Vector2 point)
    {
        _mouse = point; _mouseDown = false;
        GetViewport().PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = false }, true);
        await Frames(2);
    }
    private void Touch(int index, Vector2 at, bool pressed, bool canceled = false) => GetViewport().PushInput(new InputEventScreenTouch { Index = index, Position = at, Pressed = pressed, Canceled = canceled }, true);
    private void TouchMove(int index, Vector2 from, Vector2 to) => GetViewport().PushInput(new InputEventScreenDrag { Index = index, Position = to, Relative = to - from }, true);
    private async Task Frames(int count) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private async Task Until(Func<bool> condition, string message)
    {
        ulong deadline = Time.GetTicksMsec() + 6500;
        while (!condition()) { if (Time.GetTicksMsec() >= deadline) throw new TimeoutException(message); await Frames(1); }
        Check(true, message);
    }
    private async Task Resize(Vector2I size)
    {
        GetWindow().Size = size;
        ulong deadline = Time.GetTicksMsec() + 3000;
        while (true)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using Image image = GetViewport().GetTexture().GetImage();
            if (GetWindow().Size == size && image.GetSize() == size) { await Frames(2); return; }
            if (Time.GetTicksMsec() >= deadline) throw new TimeoutException($"Viewport did not resize to {size}.");
        }
    }
    private async Task Screenshot(string name)
    {
        DirAccess.MakeDirRecursiveAbsolute("res://Output/shots");
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using Image image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"res://Output/shots/{name}.png") == Error.Ok, "The real notebook rendering is saved for inspection.");
    }
    private void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); _checks++; }
}
