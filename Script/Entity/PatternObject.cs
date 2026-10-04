using Godot;
using System;

public partial class PatternObject : Sprite2D
{
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		if(Texture != null)
		{
			var collisionShape = GetNode<CollisionShape2D>("Area2D/CollisionShape2D");
			GD.Print( Texture != null ? "PatternObject: " + Name + ", texture size: " + Texture.GetSize() : "PatternObject: " + Name + ", texture is null");
			GD.Print( collisionShape != null ? "PatternObject: " + Name + ", collision shape is not null" : "PatternObject: " + Name + ", collision shape is null");
			if(Texture != null && collisionShape != null)
			{
				var textureSize = Texture.GetSize();
                collisionShape.Shape = new RectangleShape2D{Size = textureSize};
			}
		}
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
