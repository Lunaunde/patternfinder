using Godot;
using System;

public partial class Bar : Control
{
	TextureRect _unfoldBar;
	TextureRect _foldBar;
	TextureButton _unfoldButton;
	TextureButton _foldButton;
	public override void _Ready()
	{
		_unfoldBar = GetNode<TextureRect>("Unfold");
		_foldBar = GetNode<TextureRect>("Fold");
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
