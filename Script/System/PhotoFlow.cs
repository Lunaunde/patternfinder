using Godot;
using System;
using System.Threading;
using System.Threading.Tasks;

public partial class PhotoFlow : Node
{
    public bool IsBusy { get; private set; }
    public event Action<bool> BusyChanged;

    private PhotoCapture _capture;
    private PhotoPanel _panel;
    private Bar _bar;
    private MaterialInventory _inventory;
    private ColorRect _shade;
    private Control _blocker;
    private readonly CancellationTokenSource _lifetime = new();

    public override void _ExitTree() => _lifetime.Cancel();

    public void Initialize(PhotoCapture capture, PhotoPanel panel, Bar bar,
        MaterialInventory inventory, ColorRect shade, Control blocker)
    {
        _capture = capture;
        _panel = panel;
        _bar = bar;
        _inventory = inventory;
        _shade = shade;
        _blocker = blocker;
    }

    public async Task PhotographAsync(Frame frame)
    {
        if (IsBusy || !GodotObject.IsInstanceValid(frame) || frame.IsQueuedForDeletion())
            return;

        // Freeze everything that depends on the short-lived Frame before the first await.
        Rect2 captureRect = frame.CaptureWorldRect;
        MaterialEntry selected = null;
        bool recognized = frame.TryGetCaptureTarget(out var target, out float insideRate)
            && insideRate >= Config.PHOTO_SUCCESS_RATE
            && target.TrySelectMaterial(_inventory, out selected);
        string id = selected?.MaterialId ?? "";
        string name = selected?.DisplayName ?? "";
        Texture2D material = selected?.Texture;
        Vector2 materialScale = selected?.Scale ?? Vector2.One;
        bool duplicate = recognized && _inventory.Contains(id);
        bool full = _inventory.Count >= MaterialInventory.Capacity;
        bool wasOpen = _bar.IsOpen;
        CancellationToken cancellation = _lifetime.Token;

        IsBusy = true;
        _blocker.Show();
        BusyChanged?.Invoke(true);
        try
        {
            Texture2D photograph = await _capture.CaptureAsync(captureRect).WaitAsync(cancellation);
            _shade.Color = new Color(0, 0, 0, 0);
            _shade.Show();
            await Task.WhenAll(FadeShadeAsync(0.48f, 0.18), _panel.EnterAsync(photograph)).WaitAsync(cancellation);
            await _panel.RevealAsync(material, recognized).WaitAsync(cancellation);

            if (recognized && !duplicate)
            {
                _bar.Show();
                await _bar.RevealForInsertionAsync().WaitAsync(cancellation);
                await _panel.FlyToAsync(_bar.GetInsertionPoint()).WaitAsync(cancellation);
                await _bar.PresentResultAsync(full, id, name, material, materialScale).WaitAsync(cancellation);
            }
            else
            {
                await _panel.DiscardAsync().WaitAsync(cancellation);
                _bar.SetOpen(wasOpen, false);
            }

            await FadeShadeAsync(0, 0.25).WaitAsync(cancellation);
        }
        catch (OperationCanceledException)
        {
            // Scene exit cancels presentation without committing another material.
        }
        catch (Exception exception)
        {
            GD.PushError($"Photo flow failed: {exception}");
            _bar.SetOpen(wasOpen, false);
        }
        finally
        {
            IsBusy = false;
            if (IsInsideTree() && !_lifetime.IsCancellationRequested)
            {
                _panel.Reset();
                _shade.Hide();
                _blocker.Hide();
                _bar.Show();
                BusyChanged?.Invoke(false);
            }
        }
    }

    private async Task FadeShadeAsync(float alpha, double duration)
    {
        var tween = CreateTween();
        tween.TweenProperty(_shade, "color:a", alpha, duration);
        await ToSignal(tween, Tween.SignalName.Finished);
    }
}
