using Godot;
using System;

[Tool]
public partial class Background : Node2D
{
	private Sprite2D _texture;
	private Texture2D _backgroundTexture;
	[Export]
	public Texture2D BackgroundTexture
	{
		get => _backgroundTexture;
		set
		{
			_backgroundTexture = value;
			var sprite = GetNodeOrNull<Sprite2D>("Texture");
			if (sprite != null)
				sprite.Texture = value;
		}
	}
	public float GetCameraXBound(float viewportWidth = 2430)
	{
		if(_texture.Texture == null)
		{
			GD.PrintErr("Background texture is null, cannot get bound.");
			return 0.0f;
		}
		return Mathf.Max(0, (_texture.Texture.GetSize().X * _texture.Scale.X - viewportWidth) / 2.0f);
	}
	public void ScaleToWindowByY()
	{
		if(_texture.Texture == null)
		{
			GD.PrintErr("Background texture is null, cannot scale to window.");
			return;
		}
		var scaleY = Config.WINDOW_HEIGHT / _texture.Texture.GetSize().Y;
		_texture.Scale = new Vector2(scaleY, scaleY);
	}

	public void SetTexture(Texture2D texture)
	{
		BackgroundTexture = texture;
		ScaleToWindowByY();
	}
	public void ScaleToViewport(Vector2 viewportSize)
	{
		if(_texture.Texture == null)
			return;
		Vector2 textureSize = _texture.Texture.GetSize();
		float scale = Mathf.Max(viewportSize.X / textureSize.X, viewportSize.Y / textureSize.Y);
		_texture.Scale = Vector2.One * scale;
	}
	public override void _Ready()
	{
		_texture = GetNode<Sprite2D>("Texture");
		if (_backgroundTexture != null)
			_texture.Texture = _backgroundTexture;
	}
}
