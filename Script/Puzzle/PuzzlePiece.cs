using Godot;

/// <summary>A player's independent copy of a material, expressed in Puzzle coordinates.</summary>
public partial class PuzzlePiece : Node2D
{
    public Level SourceLevel { get; private set; }
    public MaterialEntry MaterialEntry { get; private set; }
    public string MaterialId => MaterialEntry?.MaterialId ?? "";
    public Texture2D Texture => _sprite?.Texture;
    public PuzzleBlock MatchedTarget { get; internal set; }
    public bool IsMatched => MatchedTarget != null;
    private Sprite2D _sprite;
    private bool _selected;

    public void Initialize(Level level, MaterialEntry material, Texture2D texture, Vector2 scale)
    {
        SourceLevel = level;
        MaterialEntry = material;
        Scale = scale;
        _sprite = new Sprite2D { Name = "Texture", Texture = texture };
        AddChild(_sprite);
    }

    public void SetSelected(bool selected)
    {
        _selected = selected;
        QueueRedraw();
    }

    public void ApplyTargetArtwork(PuzzleBlock target, Vector2 scale)
    {
        if (_sprite == null || target == null)
            return;
        _sprite.Texture = target.Texture ?? _sprite.Texture;
        _sprite.Offset = target.Offset;
        _sprite.Centered = target.Centered;
        _sprite.FlipH = target.FlipH;
        _sprite.FlipV = target.FlipV;
        Scale = scale;
        QueueRedraw();
    }

    public bool ContainsCanvasPoint(Vector2 canvasPosition) => _sprite != null
        && _sprite.GetRect().HasPoint(_sprite.GetGlobalTransformWithCanvas().AffineInverse() * canvasPosition);

    public override void _Draw()
    {
        if (_sprite == null || !_selected || IsMatched)
            return;
        float scale = Mathf.Max(0.001f, GetGlobalTransformWithCanvas().X.Length());
        DrawRect(_sprite.GetRect(), Colors.White, false, 2.5f / scale);
    }
}
