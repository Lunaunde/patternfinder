using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// A scene-owned garden and puzzle. Material IDs are resolved only within this
/// level; another level may use the same IDs with entirely different textures.
/// </summary>
[GlobalClass]
public partial class Level : Node2D
{
    [Export] public string DisplayName { get; set; } = "关卡";
    [Export] public NodePath BackgroundPath { get; set; } = new("Background");
    [Export] public NodePath PatternsPath { get; set; } = new("Patterns");
    [Export] public NodePath PuzzlePath { get; set; } = new("Puzzle");

    public Background Background { get; private set; }
    public Puzzle Puzzle { get; private set; }
    public MaterialInventory Inventory { get; } = new();

    private readonly Dictionary<string, MaterialEntry> _materials = new(StringComparer.Ordinal);
    private bool _materialsInitialized;

    public override void _Ready()
    {
        Background = GetNodeOrNull<Background>(BackgroundPath);
        Puzzle = GetNodeOrNull<Puzzle>(PuzzlePath);
        Puzzle?.Hide();
        RefreshMaterials();
    }

    /// <summary>Return all configurable pattern objects under this level's Patterns container.</summary>
    public IReadOnlyList<PatternObject> GetPatterns()
    {
        List<PatternObject> patterns = new();
        Node container = GetNodeOrNull<Node>(PatternsPath);
        if (container != null)
            CollectPatterns(container, patterns);
        return patterns;
    }

    private static void CollectPatterns(Node parent, List<PatternObject> patterns)
    {
        // An embedded level retains its own material-ID scope.
        if (parent is Level)
            return;
        if (parent is PatternObject pattern)
            patterns.Add(pattern);
        foreach (Node child in parent.GetChildren())
            CollectPatterns(child, patterns);
    }

    /// <summary>
    /// Rebuild the level-local catalogue after editing or adding pattern objects.
    /// Duplicate IDs resolve to the first valid variant in scene order.
    /// </summary>
    public void RefreshMaterials()
    {
        _materials.Clear();
        foreach (PatternObject pattern in GetPatterns())
        {
            if (pattern.Variants == null)
                continue;
            foreach (PatternVariant variant in pattern.Variants)
            {
                if (!GodotObject.IsInstanceValid(variant)
                    || string.IsNullOrWhiteSpace(variant.MaterialId)
                    || !IsUsableTexture(variant.MaterialTexture)
                    || !MaterialEntry.IsUsableScale(variant.Scale)
                    || _materials.ContainsKey(variant.MaterialId))
                    continue;
                string displayName = string.IsNullOrWhiteSpace(variant.MaterialName)
                    ? variant.MaterialId : variant.MaterialName;
                _materials.Add(variant.MaterialId,
                    new MaterialEntry(variant.MaterialId, displayName, variant.MaterialTexture, variant.Scale));
            }
        }
        _materialsInitialized = true;
    }

    private static bool IsUsableTexture(Texture2D texture)
    {
        if (!GodotObject.IsInstanceValid(texture)
            || texture is AtlasTexture atlas && !GodotObject.IsInstanceValid(atlas.Atlas))
            return false;
        Vector2 size = texture.GetSize();
        return size.X > 0 && size.Y > 0;
    }

    /// <summary>Resolve a stable material snapshot from this level's patterns.</summary>
    public bool TryResolveMaterial(string materialId, out MaterialEntry material)
    {
        material = null;
        if (string.IsNullOrWhiteSpace(materialId))
            return false;
        if (!_materialsInitialized)
            RefreshMaterials();
        return _materials.TryGetValue(materialId, out material);
    }

    public bool OwnsMaterial(string materialId) => TryResolveMaterial(materialId, out _);

    /// <summary>Check both the local ID and its canonical texture, rather than a global ID table.</summary>
    public bool OwnsMaterial(MaterialEntry material) => material != null
        && TryResolveMaterial(material.MaterialId, out MaterialEntry local)
        && local.Texture == material.Texture;
}
