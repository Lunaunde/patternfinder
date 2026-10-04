using Godot;
using System;

public partial class Background : Node2D
{
	private Sprite2D _texture;
	public float GetCameraXBound()
	{
		if(_texture.Texture == null)
		{
			GD.PrintErr("Background texture is null, cannot get bound.");
			return 0.0f;
		}
		return (_texture.Texture.GetSize().X * _texture.Scale.X - Config.WINDOW_WIDTH) / 2.0f;
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
		_texture.Texture = texture;
		ScaleToWindowByY();
	}
	public override void _Ready()
	{
		_texture = GetNode<Sprite2D>("Texture");
	}
}
