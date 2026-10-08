using Godot;
using System;
using System.Threading.Tasks;

/// <summary>Captures the garden world without the viewfinder or CanvasLayer UI.</summary>
public partial class PhotoCapture : SubViewport
{
    [Export(PropertyHint.Range, "128,1024,1")]
    public int PhotographSize { get; set; } = 489;

    private Camera2D _camera;
    private bool _capturing;

    public void Initialize(Viewport sourceViewport)
    {
        if (sourceViewport == null)
            throw new ArgumentNullException(nameof(sourceViewport));

        World2D = sourceViewport.FindWorld2D();
        CanvasCullMask = 1;
        Disable3D = true;
        TransparentBg = false;
        RenderTargetUpdateMode = UpdateMode.Disabled;
        Size = new Vector2I(PhotographSize, PhotographSize);

        if (_camera == null)
        {
            _camera = new Camera2D
            {
                Name = "CaptureCamera",
                Enabled = true,
                PositionSmoothingEnabled = false
            };
            AddChild(_camera);
        }
    }

    public async Task<Texture2D> CaptureAsync(Rect2 worldRect)
    {
        if (_camera == null || !IsInsideTree())
            throw new InvalidOperationException("Initialize the capture viewport after adding it to the scene tree.");
        if (_capturing)
            throw new InvalidOperationException("A photograph is already being captured.");
        if (worldRect.Size.X <= 0 || worldRect.Size.Y <= 0)
            throw new ArgumentException("The photograph rectangle must have a positive size.", nameof(worldRect));

        _capturing = true;
        try
        {
            // Keep the render target allocated between photographs. The returned
            // ImageTexture owns a frozen image rather than this live viewport.
            _camera.Position = worldRect.GetCenter();
            _camera.Zoom = new Vector2(Size.X / worldRect.Size.X, Size.Y / worldRect.Size.Y);
            _camera.ForceUpdateScroll();
            RenderTargetUpdateMode = UpdateMode.Once;
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

            if (!GodotObject.IsInstanceValid(this) || !IsInsideTree())
                throw new OperationCanceledException("The capture viewport has left the scene tree.");

            var photograph = GetTexture().GetImage();
            if (photograph == null || photograph.IsEmpty())
                throw new InvalidOperationException("The garden photograph could not be rendered.");
            return ImageTexture.CreateFromImage(photograph);
        }
        finally
        {
            if (GodotObject.IsInstanceValid(this))
                RenderTargetUpdateMode = UpdateMode.Disabled;
            _capturing = false;
        }
    }
}
