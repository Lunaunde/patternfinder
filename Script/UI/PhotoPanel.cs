using Godot;
using System;
using System.Threading.Tasks;

/// <summary>The common photograph animation and its new/duplicate/failure exits.</summary>
public partial class PhotoPanel : Control
{
    [Export(PropertyHint.Range, "0.05,3,0.01")]
    public double EntranceDuration { get; set; } = 0.45;
    [Export(PropertyHint.Range, "0,3,0.01")]
    public double PhotographHoldDuration { get; set; } = 0.3;
    [Export(PropertyHint.Range, "0.05,3,0.01")]
    public double RevealDuration { get; set; } = 0.65;
    [Export(PropertyHint.Range, "0,3,0.01")]
    public double MaterialHoldDuration { get; set; } = 0.3;
    [Export(PropertyHint.Range, "0.05,3,0.01")]
    public double FlyDuration { get; set; } = 0.55;
    [Export(PropertyHint.Range, "0.05,3,0.01")]
    public double DiscardDuration { get; set; } = 0.45;

    private static readonly Vector2 CardSize = new Vector2(545, 620);
    private const float BarHeight = 276;
    private Control _card;
    private TextureRect _photograph;
    private ShaderMaterial _revealMaterial;
    private Texture2D _failureTexture;
    private Tween _tween;
    private TaskCompletionSource<bool> _animationCompletion;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        ZIndex = 5;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        BuildCard();
        Reset();
    }

    private void BuildCard()
    {
        _card = new Control
        {
            Name = "PhotographCard",
            Size = CardSize,
            PivotOffset = CardSize / 2,
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_card);
        _revealMaterial = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://Assets/Shader/photo_reveal.gdshader")
        };
        Texture2D photoFrame = GD.Load<Texture2D>("res://Assets/Texture/gardern/拍立得4.png");
        Vector2 atlasSize = photoFrame.GetSize();
        _revealMaterial.SetShaderParameter("photo_frame_texture", photoFrame);
        _revealMaterial.SetShaderParameter("photo_frame_uv_rect", new Vector4(
            968 / atlasSize.X, 93 / atlasSize.Y, 402 / atlasSize.X, 514 / atlasSize.Y));
        float frameScale = CardSize.Y / 514;
        float frameWidth = 402 * frameScale;
        Vector2 framePosition = new((CardSize.X - frameWidth) / 2, 0);
        Rect2 window = new(framePosition + new Vector2(29, 35) * frameScale,
            new Vector2(342, 367) * frameScale);
        Rect2 photograph = new(window.Position + new Vector2(0, (window.Size.Y - window.Size.X) / 2),
            Vector2.One * window.Size.X);
        _revealMaterial.SetShaderParameter("photo_frame_rect", NormalizedRect(new Rect2(framePosition, new Vector2(frameWidth, CardSize.Y))));
        _revealMaterial.SetShaderParameter("photo_window_rect", NormalizedRect(window));
        _revealMaterial.SetShaderParameter("photo_image_rect", NormalizedRect(photograph));
        _revealMaterial.SetShaderParameter("material_frame_texture",
            GD.Load<Texture2D>("res://Assets/Texture/ui/garden_cardbar_card_normal@3x.png"));
        float materialWidth = CardSize.Y * 161 / 210;
        _revealMaterial.SetShaderParameter("material_frame_rect", NormalizedRect(new Rect2(
            new Vector2((CardSize.X - materialWidth) / 2, 0), new Vector2(materialWidth, CardSize.Y))));
        _photograph = new TextureRect
        {
            Name = "Photograph",
            Size = CardSize,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
            Material = _revealMaterial
        };
        _card.AddChild(_photograph);
    }

    private static Vector4 NormalizedRect(Rect2 rect) => new(
        rect.Position.X / CardSize.X, rect.Position.Y / CardSize.Y,
        rect.Size.X / CardSize.X, rect.Size.Y / CardSize.Y);

    public Task EnterAsync(Texture2D photograph)
    {
        if (photograph == null)
            throw new ArgumentNullException(nameof(photograph));
        EnsureReady();
        StopAnimation();
        _photograph.Texture = photograph;
        _revealMaterial.SetShaderParameter("reveal_radius", 0f);
        _card.Scale = Vector2.One;
        _card.Modulate = Colors.White;
        _card.Visible = true;
        Visible = true;
        var restingPosition = RestingPosition();
        _card.Position = new Vector2(restingPosition.X, -CardSize.Y - 30);
        return Animate(tween =>
        {
            tween.TweenProperty(_card, "position", restingPosition, EntranceDuration)
                .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
            tween.TweenInterval(PhotographHoldDuration);
        });
    }

    public Task RevealAsync(Texture2D materialTexture, bool success)
    {
        EnsureReady();
        if (success && materialTexture == null)
            throw new ArgumentNullException(nameof(materialTexture));
        if (!success)
            materialTexture = _failureTexture ??= CreateFailureTexture();

        var textureSize = materialTexture.GetSize();
        Vector2 windowSize = new(127, 115);
        float fit = Mathf.Min(windowSize.X / textureSize.X, windowSize.Y / textureSize.Y);
        Vector2 extent = textureSize * fit / windowSize;
        Vector4 uvRect = new(0, 0, 1, 1);
        Texture2D samplingTexture = materialTexture;
        if (materialTexture is AtlasTexture atlas && atlas.Atlas != null)
        {
            samplingTexture = atlas.Atlas;
            Vector2 atlasSize = samplingTexture.GetSize();
            uvRect = new Vector4(atlas.Region.Position.X / atlasSize.X, atlas.Region.Position.Y / atlasSize.Y,
                atlas.Region.Size.X / atlasSize.X, atlas.Region.Size.Y / atlasSize.Y);
        }
        _revealMaterial.SetShaderParameter("material_texture", samplingTexture);
        _revealMaterial.SetShaderParameter("material_uv_rect", uvRect);
        _revealMaterial.SetShaderParameter("material_uv_extent", extent);
        _revealMaterial.SetShaderParameter("recognition_success", success);
        _revealMaterial.SetShaderParameter("reveal_radius", 0f);
        return Animate(tween =>
        {
            tween.TweenMethod(Callable.From<float>(radius =>
                _revealMaterial.SetShaderParameter("reveal_radius", radius)), 0f, 1.2f, RevealDuration)
                .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            tween.TweenInterval(MaterialHoldDuration);
        });
    }

    public async Task FlyToAsync(Vector2 destinationCanvas)
    {
        EnsureReady();
        var destinationLocal = GetGlobalTransformWithCanvas().AffineInverse() * destinationCanvas;
        await Animate(tween =>
        {
            tween.SetParallel();
            tween.TweenProperty(_card, "position", destinationLocal - CardSize / 2, FlyDuration)
                .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
            tween.TweenProperty(_card, "scale", Vector2.One * 0.22f, FlyDuration)
                .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
        });
        if (GodotObject.IsInstanceValid(_card))
            _card.Visible = false;
    }

    public async Task DiscardAsync()
    {
        EnsureReady();
        await Animate(tween =>
        {
            tween.TweenProperty(_card, "position", new Vector2(_card.Position.X, PanelSize().Y + 30), DiscardDuration)
                .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
        });
        if (GodotObject.IsInstanceValid(_card))
            _card.Visible = false;
    }

    public void Reset()
    {
        StopAnimation();
        Visible = false;
        if (_card == null)
            return;
        _card.Visible = false;
        _card.Scale = Vector2.One;
        _card.Modulate = Colors.White;
        _photograph.Texture = null;
        _revealMaterial.SetShaderParameter("reveal_radius", 0f);
    }

    public override void _ExitTree()
    {
        StopAnimation();
    }

    private void EnsureReady()
    {
        if (_card == null || !IsInsideTree())
            throw new InvalidOperationException("Add PhotoPanel to the scene tree before animating it.");
    }

    private Vector2 PanelSize()
    {
        return Size.X > 0 && Size.Y > 0 ? Size : new Vector2(2430, 1080);
    }

    private Vector2 RestingPosition()
    {
        var panelSize = PanelSize();
        return new Vector2((panelSize.X - CardSize.X) / 2,
            Mathf.Max(20, (panelSize.Y - BarHeight - CardSize.Y) / 2));
    }

    private Task Animate(Action<Tween> configure)
    {
        StopAnimation();
        var completion = new TaskCompletionSource<bool>();
        _animationCompletion = completion;
        _tween = CreateTween();
        _tween.Finished += () => completion.TrySetResult(true);
        configure(_tween);
        return completion.Task;
    }

    private void StopAnimation()
    {
        if (_tween != null && GodotObject.IsInstanceValid(_tween))
            _tween.Kill();
        _tween = null;
        _animationCompletion?.TrySetCanceled();
        _animationCompletion = null;
    }

    private static Texture2D CreateFailureTexture()
    {
        const int size = 320;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        image.Fill(Colors.Transparent);
        // Deterministic freehand loops create a native UI failure symbol.
        for (int loop = 0; loop < 10; loop++)
        {
            Vector2 previous = Vector2.Zero;
            for (int sample = 0; sample <= 150; sample++)
            {
                float angle = sample / 150f * Mathf.Tau;
                float phase = loop * 0.71f;
                float radius = 66 + loop * 4 + Mathf.Sin(angle * 5 + phase) * 14;
                var point = new Vector2(Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius * (0.65f + loop * 0.025f)).Rotated(phase)
                    + new Vector2(size / 2f + Mathf.Sin(phase) * 18, size / 2f + Mathf.Cos(phase) * 12);
                if (sample > 0)
                    DrawStroke(image, previous, point);
                previous = point;
            }
        }
        return ImageTexture.CreateFromImage(image);
    }

    private static void DrawStroke(Image image, Vector2 start, Vector2 end)
    {
        int steps = Mathf.Max(1, Mathf.CeilToInt(start.DistanceTo(end)));
        var ink = new Color(0.13f, 0.12f, 0.1f);
        for (int step = 0; step <= steps; step++)
        {
            var point = start.Lerp(end, step / (float)steps);
            int centerX = Mathf.RoundToInt(point.X);
            int centerY = Mathf.RoundToInt(point.Y);
            for (int offsetY = -2; offsetY <= 2; offsetY++)
            for (int offsetX = -2; offsetX <= 2; offsetX++)
            {
                if (offsetX * offsetX + offsetY * offsetY > 5)
                    continue;
                int x = centerX + offsetX;
                int y = centerY + offsetY;
                if (x >= 0 && y >= 0 && x < image.GetWidth() && y < image.GetHeight())
                    image.SetPixel(x, y, ink);
            }
        }
    }
}
