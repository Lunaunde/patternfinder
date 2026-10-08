using Godot;

// The container fits the UI rectangle; contents keep the design coordinate system.
public partial class CenteredUI : Control
{
    private Control _designRoot;

    public override void _Ready()
    {
        _designRoot = GetNode<Control>("DesignRoot");
        Resized += UpdateLayout;
        UpdateLayout();
    }

    private void UpdateLayout()
    {
        if (_designRoot == null || Size.X <= 0 || Size.Y <= 0)
            return;
        var designSize = new Vector2(Config.WINDOW_WIDTH, Config.WINDOW_HEIGHT);
        float scale = Mathf.Min(Size.X / designSize.X, Size.Y / designSize.Y);
        _designRoot.Size = designSize;
        _designRoot.Scale = Vector2.One * scale;
        _designRoot.Position = (Size - designSize * scale) / 2;
    }
}
