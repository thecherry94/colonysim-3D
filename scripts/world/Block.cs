using Godot;

namespace ColonySim.World;

/// <summary>
/// Defines all block types in the voxel world.
/// Air (0) is special - it represents empty space and is never rendered.
/// </summary>
public enum BlockType : byte
{
    Air = 0,
    Stone = 1,
    Dirt = 2,
    Grass = 3
}

/// <summary>
/// Provides block metadata and utilities.
/// </summary>
public static class BlockData
{
    /// <summary>
    /// Returns the color for a given block type.
    /// Used for simple colored rendering before textures are added.
    /// </summary>
    public static Color GetColor(BlockType type)
    {
        return type switch
        {
            BlockType.Air => Colors.Transparent,
            BlockType.Stone => new Color(0.5f, 0.5f, 0.5f),      // Gray
            BlockType.Dirt => new Color(0.55f, 0.35f, 0.2f),     // Brown
            BlockType.Grass => new Color(0.3f, 0.7f, 0.2f),      // Green
            _ => Colors.Magenta  // Error color - indicates missing definition
        };
    }

    /// <summary>
    /// Returns true if the block type is solid (opaque, blocks visibility).
    /// </summary>
    public static bool IsSolid(BlockType type)
    {
        return type != BlockType.Air;
    }

    /// <summary>
    /// Returns true if the block type is transparent (allows light/visibility through).
    /// </summary>
    public static bool IsTransparent(BlockType type)
    {
        return type == BlockType.Air;
    }
}
