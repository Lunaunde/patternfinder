using Godot;

/// <summary>The garden card artwork with its baked-in pattern removed by the frame shader.</summary>
public partial class MaterialCard : Control
{
	private static readonly Rect2 ArtworkBounds = new(11, 23, 161, 210);
	private static readonly Rect2 PatternWindow = new(28, 39, 127, 115);
	private static readonly Rect2 NameWindow = new(29, 188, 125, 34);
	private MaterialEntry _entry;
	private TextureRect _frame;
	private TextureRect _pattern;
	private Label _nameLabel;
	private bool _built;
	public MaterialEntry Entry => _entry;

	public void SetEntry(MaterialEntry entry)
	{
		_entry = entry;
		UpdateVisual();
	}

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		ClipContents = true;
		_frame = new TextureRect
		{
			Name = "CardFrame", Texture = GD.Load<Texture2D>("res://Assets/Texture/ui/garden_cardbar_card_normal@3x.png"),
			MouseFilter = MouseFilterEnum.Ignore, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.Scale,
			Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Assets/Shader/material_card.gdshader") }
		};
		AddChild(_frame);
		_pattern = new TextureRect
		{
			Name = "Pattern", MouseFilter = MouseFilterEnum.Ignore, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
		};
		AddChild(_pattern);
		_nameLabel = new Label
		{
			Name = "MaterialName", MouseFilter = MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
		};
		_nameLabel.AddThemeColorOverride("font_color", new Color(0.38f, 0.35f, 0.25f));
		_nameLabel.AddThemeColorOverride("font_outline_color", new Color(0.66f, 0.90f, 0.77f));
		_nameLabel.AddThemeConstantOverride("outline_size", 2);
		AddChild(_nameLabel);
		_built = true;
		UpdateLayout();
		UpdateVisual();
	}

	public override void _Notification(int what)
	{
		if (what == NotificationResized && _built)
			UpdateLayout();
	}

	private void UpdateLayout()
	{
		Vector2 scale = new(Size.X / ArtworkBounds.Size.X, Size.Y / ArtworkBounds.Size.Y);
		// Keep the complete PNG on the TextureRect so the shader receives its original UVs.
		_frame.Position = -ArtworkBounds.Position * scale;
		_frame.Size = new Vector2(183, 261) * scale;
		_pattern.Position = (PatternWindow.Position - ArtworkBounds.Position) * scale;
		_pattern.Size = PatternWindow.Size * scale;
		_nameLabel.Position = (NameWindow.Position - ArtworkBounds.Position) * scale;
		_nameLabel.Size = NameWindow.Size * scale;
		_nameLabel.AddThemeFontSizeOverride("font_size", Mathf.Max(12, Mathf.RoundToInt(18 * scale.Y)));
	}

	private void UpdateVisual()
	{
		if (_built)
		{
			bool hasMaterial = _entry != null;
			_frame.Visible = hasMaterial;
			_pattern.Visible = hasMaterial;
			_nameLabel.Visible = hasMaterial;
			_frame.Modulate = Colors.White;
			_pattern.Texture = _entry?.Texture;
			_nameLabel.Text = _entry?.DisplayName ?? "";
			TooltipText = _entry?.DisplayName ?? "";
		}
	}
}
