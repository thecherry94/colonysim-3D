using Godot;

namespace ColonySim.World;

/// <summary>
/// Generates procedural terrain using FastNoiseLite.
/// Uses 2D noise for heightmap-based terrain generation.
/// </summary>
public static class TerrainGenerator
{
    private static FastNoiseLite _noise = null!;

    /// <summary>
    /// Base height for terrain (minimum ground level).
    /// </summary>
    public const int BaseHeight = 2;

    /// <summary>
    /// Maximum height variation from noise.
    /// </summary>
    public const int HeightVariation = 10;

    /// <summary>
    /// Number of stone layers below surface.
    /// </summary>
    public const int StoneDepth = 3;

    static TerrainGenerator()
    {
        InitializeNoise();
    }

    private static void InitializeNoise()
    {
        _noise = new FastNoiseLite();
        _noise.NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth;
        _noise.Seed = 12345;  // Fixed seed for reproducibility
        _noise.Frequency = 0.05f;  // Smooth, rolling hills
        _noise.FractalType = FastNoiseLite.FractalTypeEnum.Fbm;
        _noise.FractalOctaves = 4;
        _noise.FractalLacunarity = 2.0f;
        _noise.FractalGain = 0.5f;
    }

    /// <summary>
    /// Gets the terrain height at a world (x, z) position.
    /// </summary>
    public static int GetHeight(int worldX, int worldZ)
    {
        // Noise returns -1 to 1, normalize to 0-1
        float noiseValue = _noise.GetNoise2D(worldX, worldZ);
        float normalized = (noiseValue + 1.0f) * 0.5f;

        // Scale to height range
        int height = BaseHeight + (int)(normalized * HeightVariation);
        return height;
    }

    /// <summary>
    /// Gets the block type at a world position based on height.
    /// </summary>
    public static BlockType GetBlockType(int worldX, int worldY, int worldZ)
    {
        int surfaceHeight = GetHeight(worldX, worldZ);

        if (worldY >= surfaceHeight)
            return BlockType.Air;
        else if (worldY == surfaceHeight - 1)
            return BlockType.Grass;
        else if (worldY >= surfaceHeight - StoneDepth)
            return BlockType.Dirt;
        else
            return BlockType.Stone;
    }
}
