using Godot;
using System;
using System.Threading.Tasks;

public partial class FrameDetectionTest : Node2D
{
    private int _checks;

    public override async void _Ready()
    {
        try
        {
            await RunChecks();
            GD.Print($"Frame detection: {_checks} checks passed.");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private async Task RunChecks()
    {
        var frameScene = GD.Load<PackedScene>("res://Scenes/frame.tscn");
        var patternScene = GD.Load<PackedScene>("res://Scenes/base/pattern_object.tscn");
        Image image = Image.CreateEmpty(200, 200, false, Image.Format.Rgba8);
        image.Fill(Colors.White);
        Texture2D texture = ImageTexture.CreateFromImage(image);

        PatternObject first = CreatePattern(patternScene, texture, Vector2.Zero);
        var frame = frameScene.Instantiate<Frame>();
        frame.Position = new Vector2(80, 0);
        AddChild(frame);
        await RefreshOverlaps();

        var firstArea = first.GetNode<PatternObjectArea>("Area2D");
        Check(Near(firstArea.FrameInsideRate(new Vector2(20, 0)), 0.8f), "Coverage uses the existing center-distance formula.");
        Check(frame.TryGetCaptureTarget(out var target, out float rate) && target == first && Near(rate, 0.2f), "Capture finds the highest current overlap.");
        Check(!frame.IsDetected && frame.Position == new Vector2(80, 0), "Capture queries do not change detection or position.");
        Check(Near(frame.CaptureWorldRect.Position.X, 80 - 244.5f) && Near(frame.CaptureWorldRect.Size.X, 489), "Capture bounds use the frame collision shape.");

        frame.Move(Vector2.Zero);
        Check(frame.IsDetected && frame.Position == new Vector2(80, 0), "Entering Detected preserves movement ordering.");

        PatternObject second = CreatePattern(patternScene, texture, new Vector2(120, 0));
        await RefreshOverlaps();
        frame.Move(Vector2.Zero);
        Check(frame.IsDetected && Near(frame.Position.X, 84.8f), "Detected switches to the best current overlap and applies its attraction.");
        Check(frame.TryGetCaptureTarget(out target, out rate) && target == second && Near(rate, 0.648f), "Capture recomputes coverage after movement.");

        frame.Position = new Vector2(119, 0);
        frame.Move(Vector2.Zero);
        Check(frame.Position == second.Position, "High coverage with weak input still snaps to the target.");
        frame.Move(new Vector2(0.8f, 0));
        Check(frame.Position == second.Position, "Input equal to the force threshold stays snapped.");
        frame.Move(new Vector2(0.81f, 0));
        Check(Near(frame.Position.X, 128.1f), "Input above the force threshold can leave the center.");

        second.QueueFree();
        await RefreshOverlaps();
        frame.Position = new Vector2(100, 0);
        frame.Move(Vector2.Zero);
        Check(frame.IsDetected && frame.Position == new Vector2(100, 0), "Detected remains active when coverage equals zero.");
        frame.Position = new Vector2(100.1f, 0);
        frame.Move(Vector2.Zero);
        Check(!frame.IsDetected, "Negative coverage exits Detected.");
        frame.Position = new Vector2(100, 0);
        frame.Move(Vector2.Zero);
        Check(!frame.IsDetected, "Coverage equal to zero does not enter Detected.");

        frame.Position = new Vector2(99, 0);
        frame.Move(Vector2.Zero);
        Check(frame.IsDetected, "Positive coverage enters Detected.");
        first.QueueFree();
        Check(!frame.TryGetCaptureTarget(out _, out _), "Queued targets are excluded from capture immediately.");
        frame.Move(Vector2.Right);
        Check(!frame.IsDetected && frame.Position == new Vector2(109, 0), "Removing all targets exits safely and preserves ordinary movement.");

        await RefreshOverlaps();
        Check(!frame.TryGetCaptureTarget(out _, out _), "Empty overlaps return no capture target.");
    }

    private PatternObject CreatePattern(PackedScene scene, Texture2D texture, Vector2 position)
    {
        var pattern = scene.Instantiate<PatternObject>();
        pattern.Texture = texture;
        pattern.Position = position;
        AddChild(pattern);
        return pattern;
    }

    private async Task RefreshOverlaps()
    {
        for (int index = 0; index < 3; index++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private static bool Near(float actual, float expected) => Math.Abs(actual - expected) < 0.0001f;

    private void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
        _checks++;
    }
}
