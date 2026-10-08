using Godot;

/// <summary>Artwork used to position scene content in the editor; it never renders during play.</summary>
[Tool]
[GlobalClass]
public partial class EditorOnlyPreview : Sprite2D
{
    public override void _EnterTree()
    {
        if (!Engine.IsEditorHint())
            Hide();
    }
}
