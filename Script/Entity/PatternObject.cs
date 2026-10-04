using Godot;
using System;

public partial class PatternObject : Node2D
{
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		var sprite = GetNode<Sprite2D>("Texture");
		if(sprite.Texture != null)
		{
			var collisionShape = GetNode<CollisionShape2D>("Area2D/CollisionShape2D");
			GD.Print( sprite.Texture != null ? "PatternObject: " + Name + ", texture size: " + sprite.Texture.GetSize() : "PatternObject: " + Name + ", texture is null");
			GD.Print( collisionShape != null ? "PatternObject: " + Name + ", collision shape is not null" : "PatternObject: " + Name + ", collision shape is null");
			if(sprite.Texture != null && collisionShape != null)
			{
				var textureSize = sprite.Texture.GetSize() * sprite.Scale;
                collisionShape.Shape = new RectangleShape2D{Size = textureSize};
			}
		}
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
