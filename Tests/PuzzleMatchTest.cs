using Godot;
using System;
using System.Collections.Generic;

/// <summary>Exercises authored target coordinates, local/ shared tolerances, and exact material ID matching.</summary>
public partial class PuzzleMatchTest : Node
{
    private readonly List<Puzzle> _fixtures = new();
    private int _checks;

    public override void _Ready()
    {
        try
        {
            CheckIdentityAndTolerances();
            CheckTransformsAndAngles();
            CheckDuplicateTargetsAndInvalidValues();
            CheckSceneTemplates();
            CheckBaseCopies();
            GD.Print($"Puzzle matching: {_checks} checks passed.");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
        finally
        {
            foreach (Puzzle fixture in _fixtures)
                fixture.Free();
        }
    }

    private Puzzle Fixture(float positionTolerance = 20, float angleTolerance = 15)
    {
        var puzzle = new Puzzle
        {
            Definition = new PuzzleDefinition
            {
                PositionTolerance = positionTolerance, AngleToleranceDegrees = angleTolerance
            }
        };
        puzzle.AddChild(new Node2D { Name = "PuzzleBlocks" });
        _fixtures.Add(puzzle);
        return puzzle;
    }

    private static PuzzleBlock AddTarget(Puzzle puzzle, string id, Vector2 position, float rotation = 0)
    {
        var target = new PuzzleBlock { MaterialId = id, Position = position, Rotation = rotation };
        puzzle.GetNode("PuzzleBlocks").AddChild(target);
        puzzle.RefreshTargets();
        return target;
    }

    private void CheckIdentityAndTolerances()
    {
        Puzzle puzzle = Fixture();
        PuzzleBlock target = AddTarget(puzzle, "honeysuckle-unit", new Vector2(80, -40));
        Check(puzzle.TryFindMatch("honeysuckle-unit", target.Position, 0, out var match) && match == target,
            "Matching material ID and target pose resolve the authored block.");
        Check(!puzzle.TryFindMatch("Honeysuckle-unit", target.Position, 0, out _)
            && !puzzle.TryFindMatch("unknown", target.Position, 0, out _)
            && !puzzle.TryFindMatch(" ", target.Position, 0, out _),
            "Material IDs are exact, case-sensitive local identifiers; empty and foreign IDs fail.");
        Check(puzzle.Matches(target, target.MaterialId, target.Position + new Vector2(20, 0), 0),
            "The position tolerance includes its boundary.");
        Check(!puzzle.Matches(target, target.MaterialId, target.Position + new Vector2(20.1f, 0), 0),
            "A position outside the shared tolerance fails.");
        Check(puzzle.Matches(target, target.MaterialId, target.Position, Mathf.DegToRad(14.9f))
            && !puzzle.Matches(target, target.MaterialId, target.Position, Mathf.DegToRad(15.1f)),
            "The shared angle tolerance applies in degrees.");

        target.PositionTolerance = 3;
        target.AngleToleranceDegrees = 2;
        Check(puzzle.Matches(target, target.MaterialId, target.Position + new Vector2(10, 0), Mathf.DegToRad(5)),
            "A block using PuzzleDefinition ignores its own tolerance settings.");
        target.UsePuzzleDefinition = false;
        Check(!puzzle.Matches(target, target.MaterialId, target.Position + new Vector2(10, 0), 0)
            && !puzzle.Matches(target, target.MaterialId, target.Position, Mathf.DegToRad(5))
            && puzzle.Matches(target, target.MaterialId, target.Position + new Vector2(2, 0), Mathf.DegToRad(1)),
            "Disabling the shared definition activates the block's own position and angle tolerances.");
        puzzle.Definition = null;
        Check(puzzle.Matches(target, target.MaterialId, target.Position, 0),
            "A custom-tolerance block does not depend on a shared definition.");
        target.UsePuzzleDefinition = true;
        Check(!puzzle.Matches(target, target.MaterialId, target.Position, 0),
            "A shared-tolerance block without a definition fails safely.");
    }

    private void CheckTransformsAndAngles()
    {
        Puzzle puzzle = Fixture(1, 3);
        puzzle.Position = new Vector2(900, 300);
        puzzle.Rotation = Mathf.DegToRad(45);
        puzzle.Scale = new Vector2(2, 2);
        var parent = puzzle.GetNode<Node2D>("PuzzleBlocks");
        parent.Position = new Vector2(25, 50);
        parent.Rotation = Mathf.DegToRad(90);
        PuzzleBlock target = AddTarget(puzzle, "unit", new Vector2(20, 0), Mathf.DegToRad(89));
        Check(puzzle.TryGetTargetPose(target, out Vector2 pose, out float angle)
            && pose.DistanceTo(new Vector2(25, 70)) < 0.001f
            && Math.Abs(Mathf.RadToDeg(angle) - 179) < 0.001f,
            "Intermediary transforms are included while the owning Puzzle's world transform is excluded.");
        Check(puzzle.Matches(target, "unit", pose, Mathf.DegToRad(-179)),
            "Angles across the -180/180 seam use the shortest periodic difference.");
        Check(puzzle.Matches(target, "unit", pose, angle + Mathf.Tau * 2)
            && !puzzle.Matches(target, "unit", pose, Mathf.DegToRad(-175)),
            "Full turns match and angles outside the seam tolerance fail.");

        Puzzle other = Fixture();
        PuzzleBlock otherTarget = AddTarget(other, "unit", Vector2.Zero);
        Check(!puzzle.Matches(otherTarget, "unit", Vector2.Zero, 0)
            && !puzzle.TryGetTargetPose(otherTarget, out _, out _),
            "A target belonging to another Puzzle cannot participate in this Puzzle's matching.");
    }

    private void CheckDuplicateTargetsAndInvalidValues()
    {
        Puzzle puzzle = Fixture(100, 180);
        var view = puzzle.Targets;
        PuzzleBlock first = AddTarget(puzzle, "same", Vector2.Zero);
        PuzzleBlock second = AddTarget(puzzle, "same", new Vector2(40, 0));
        Check(ReferenceEquals(view, puzzle.Targets) && view.Count == 2,
            "Refreshing authored targets keeps the read-only view stable.");
        Check(puzzle.TryFindMatch("same", new Vector2(39, 0), 0, out var match) && match == second,
            "Repeated material IDs choose the nearest matching target.");
        Check(puzzle.TryFindMatch("same", new Vector2(39, 0), 0, out match, block => block != second)
            && match == first
            && !puzzle.TryFindMatch("same", Vector2.Zero, 0, out _, _ => false),
            "The board can filter occupied targets without moving occupancy into the target scene.");
        Check(!puzzle.TryFindMatch("same", new Vector2(float.NaN, 0), 0, out _)
            && !puzzle.TryFindMatch("same", Vector2.Zero, float.PositiveInfinity, out _),
            "Non-finite piece poses never match.");
        puzzle.Definition.PositionTolerance = float.PositiveInfinity;
        Check(!puzzle.TryFindMatch("same", Vector2.Zero, 0, out _), "An infinite shared tolerance is rejected.");
        puzzle.Definition.PositionTolerance = -1;
        Check(!puzzle.TryFindMatch("same", Vector2.Zero, 0, out _), "A negative shared tolerance is rejected.");
        first.UsePuzzleDefinition = false;
        first.PositionTolerance = 0;
        first.AngleToleranceDegrees = 0;
        Check(puzzle.TryFindMatch("same", Vector2.Zero, 0, out match) && match == first,
            "Zero custom tolerances accept the exact pose even if the shared definition is invalid.");
        first.AngleToleranceDegrees = float.NaN;
        Check(!puzzle.TryFindMatch("same", Vector2.Zero, 0, out _), "A non-finite custom tolerance is rejected.");

        Puzzle nested = Fixture();
        AddTarget(nested, "nested", Vector2.Zero);
        puzzle.GetNode("PuzzleBlocks").AddChild(nested);
        _fixtures.Remove(nested);
        puzzle.RefreshTargets();
        Check(puzzle.Targets.Count == 2 && !puzzle.TryFindMatch("nested", Vector2.Zero, 0, out _),
            "Nested Puzzle targets are excluded from the parent's set.");
    }

    private void CheckSceneTemplates()
    {
        Puzzle empty = GD.Load<PackedScene>("res://Scenes/base/puzzle.tscn").Instantiate<Puzzle>();
        _fixtures.Add(empty);
        empty.RefreshTargets();
        Check(empty.Targets.Count == 0 && empty.HasNode("PuzzleBases") && empty.HasNode("PuzzleBlocks"),
            "The base Puzzle template contains visual and target containers without level-specific content.");
        var preview = empty.GetNode<EditorOnlyPreview>("NotebookPreview");
        Texture2D previewTexture = preview.Texture;
        Check(preview.Visible && GodotObject.IsInstanceValid(previewTexture)
            && preview.Position == Vector2.Zero
            && Math.Abs(previewTexture.GetWidth() * preview.Scale.X - 1320) < 0.01,
            "The scene stores a visible notebook positioning preview at the same centered design size as the runtime book.");
        AddChild(empty);
        Check(!preview.Visible && preview.Texture == previewTexture
            && empty.GetNode<Node2D>("PuzzleBases").Visible,
            "Entering the runtime tree hides only editor positioning artwork without altering the visual base or texture.");
        Puzzle sample = GD.Load<PackedScene>("res://Scenes/levels/honeysucklePuzzle.tscn").Instantiate<Puzzle>();
        _fixtures.Add(sample);
        sample.RefreshTargets();
        Check(sample.Targets.Count >= 2, "The honeysuckle level has multiple editor-authored targets.");
        foreach (PuzzleBlock target in sample.Targets)
            Check(target.Visible && target.Modulate == Colors.White,
                "Authored targets store full-opacity artwork for scene editing.");
        AddChild(sample);
        foreach (PuzzleBlock target in sample.Targets)
        {
            Check(!string.IsNullOrWhiteSpace(target.MaterialId)
                && GodotObject.IsInstanceValid(target.Texture)
                && !target.Visible
                && sample.TryGetTargetPose(target, out var position, out float rotation)
                && sample.Matches(target, target.MaterialId, position, rotation),
                "Hidden runtime targets retain their material IDs, artwork, and matching authored poses.");
        }
        Check(!sample.GetNode<EditorOnlyPreview>("NotebookPreview").Visible,
            "The inherited notebook positioning preview also stays hidden in a standalone runtime Puzzle.");

        PuzzleBlock appearance = sample.Targets[0];
        appearance.Offset = new Vector2(12, -7);
        appearance.Centered = false;
        appearance.FlipH = true;
        appearance.FlipV = true;
        var piece = new PuzzlePiece();
        try
        {
            piece.Initialize(null, new MaterialEntry(appearance.MaterialId, "Preview source", appearance.Texture),
                appearance.Texture, new Vector2(-0.2f, 0.2f));
            piece.ApplyTargetArtwork(appearance, new Vector2(-0.2f, 0.2f));
            AddChild(piece);
            var sprite = piece.GetNode<Sprite2D>("Texture");
            Check(piece.IsVisibleInTree() && sprite.IsVisibleInTree() && piece.Texture == appearance.Texture,
                "Player-created artwork remains visible even when its authored source target is hidden.");
            Check(sprite.Offset == appearance.Offset && !sprite.Centered && sprite.FlipH && sprite.FlipV
                && piece.Transform.X.X < 0 && piece.Transform.Y.Y > 0,
                "Hidden targets still supply sprite offsets, centering, flips, and signed scale to player pieces.");
        }
        finally
        {
            piece.Free();
        }
    }

    private void CheckBaseCopies()
    {
        var level = new Level { Name = "BaseCopyLevel" };
        var legacyLevel = new Level { Name = "LegacyBaseCopyLevel" };
        var board = new PuzzleBoard { Name = "BaseCopyBoard", Size = new Vector2(1320, 1028), PuzzleScale = 1 };
        try
        {
            Puzzle source = Fixture();
            _fixtures.Remove(source);
            source.Name = "Puzzle";
            source.Position = new Vector2(450, 220);
            source.Rotation = 0.8f;
            level.AddChild(source);
            Image image = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8);
            image.Fill(Colors.Wheat);
            Texture2D texture = ImageTexture.CreateFromImage(image);

            var bases = new Node2D
            {
                Name = "PuzzleBases", Position = new Vector2(80, -40), Rotation = 0.3f,
                Scale = new Vector2(1.2f, 0.8f)
            };
            source.AddChild(bases);
            var nested = new Node2D
            {
                Name = "Nested", Position = new Vector2(-20, 40), Rotation = -0.2f,
                Scale = new Vector2(0.5f, 1.1f)
            };
            bases.AddChild(nested);
            var foreground = new Sprite2D
            {
                Name = "Foreground", Texture = texture, Position = new Vector2(35, -15),
                Rotation = 0.1f, Scale = new Vector2(0.7f, 0.9f), Offset = new Vector2(4, -2),
                Centered = false, FlipH = true
            };
            nested.AddChild(foreground);
            var hiddenBase = new Sprite2D { Name = "HiddenBase", Texture = texture, Visible = false };
            bases.AddChild(hiddenBase);
            var hiddenGroup = new Node2D { Name = "HiddenGroup", Visible = false };
            bases.AddChild(hiddenGroup);
            hiddenGroup.AddChild(new Sprite2D { Name = "InnerBase", Texture = texture });
            bases.AddChild(new EditorOnlyPreview { Name = "EditorPreview", Texture = texture });
            nested.AddChild(new PuzzleBlock { Name = "AuthoredTarget", Texture = texture, MaterialId = "unit" });
            AddChild(level);
            AddChild(board);
            Check(!source.Visible && foreground.Visible && !foreground.IsVisibleInTree(),
                "A Level hides the source Puzzle through its parent without changing the base's own visibility.");
            board.BindLevel(level);
            Node2D page = board.GetNode<Node2D>("PuzzleContent/LevelPage_0");
            Node2D copiedBases = page.GetNode<Node2D>("PuzzleBases");
            Sprite2D copiedForeground = copiedBases.GetNode<Sprite2D>("Nested/Foreground");
            Check(copiedBases.IsVisibleInTree() && copiedForeground.IsVisibleInTree()
                && copiedForeground.Texture == texture,
                "All visual bases under PuzzleBases render on the notebook despite the hidden source Puzzle.");
            Transform2D expected = bases.Transform * nested.Transform * foreground.Transform;
            Transform2D actual = page.GlobalTransform.AffineInverse() * copiedForeground.GlobalTransform;
            Check(SameTransform(actual, expected)
                && SameTransform(copiedBases.Transform, bases.Transform)
                && SameTransform(copiedBases.GetNode<Node2D>("Nested").Transform, nested.Transform),
                "Copying bases preserves every local transform and excludes the Puzzle's garden transform.");
            Check(copiedForeground.Offset == foreground.Offset && !copiedForeground.Centered && copiedForeground.FlipH,
                "A visual base retains its authored sprite artwork settings.");
            Check(!copiedBases.GetNode<Sprite2D>("HiddenBase").Visible
                && !copiedBases.GetNode<Node2D>("HiddenGroup").Visible
                && !copiedBases.GetNode<Sprite2D>("HiddenGroup/InnerBase").IsVisibleInTree(),
                "Explicitly hidden bases and hidden intermediate groups are not forced visible.");
            Check(!copiedBases.HasNode("EditorPreview") && !copiedBases.HasNode("Nested/AuthoredTarget")
                && !page.HasNode("NotebookPreview") && !page.HasNode("PuzzleBlocks"),
                "Notebook visual copies exclude editor positioning previews and target configuration.");

            Puzzle legacy = Fixture();
            _fixtures.Remove(legacy);
            legacy.Name = "Puzzle";
            legacyLevel.AddChild(legacy);
            var singleBase = new Sprite2D
            {
                Name = "PuzzleBase", Texture = texture, Position = new Vector2(-110, 25),
                Rotation = -0.4f, Scale = new Vector2(0.4f, 0.6f)
            };
            legacy.AddChild(singleBase);
            AddChild(legacyLevel);
            board.BindLevel(legacyLevel);
            Sprite2D copiedSingle = board.GetNode<Sprite2D>("PuzzleContent/LevelPage_1/PuzzleBase");
            Check(copiedSingle.IsVisibleInTree() && copiedSingle.Texture == texture
                && SameTransform(copiedSingle.Transform, singleBase.Transform),
                "Older scenes with a single root PuzzleBase remain visible with their original transform.");
        }
        finally
        {
            board.Free();
            legacyLevel.Free();
            level.Free();
        }
    }

    private static bool SameTransform(Transform2D actual, Transform2D expected) =>
        actual.X.DistanceTo(expected.X) < 0.001f && actual.Y.DistanceTo(expected.Y) < 0.001f
        && actual.Origin.DistanceTo(expected.Origin) < 0.001f;

    private void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
        _checks++;
    }
}
