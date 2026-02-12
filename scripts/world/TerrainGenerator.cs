namespace ColonySim;

using Godot;

/// <summary>
/// Generates terrain using FastNoiseLite height maps.
/// Produces seamless terrain across chunk boundaries using world coordinates.
/// Features: rolling hills, sand at water edges, shallow water pools.
/// </summary>
public class TerrainGenerator
{
    private readonly FastNoiseLite _heightNoise;
    private readonly FastNoiseLite _detailNoise;
    private readonly int _baseHeight;
    private const int WaterLevel = 4;

    public TerrainGenerator(int seed = 42, int baseHeight = 7)
    {
        _baseHeight = baseHeight;

        // Main terrain shape — broad rolling hills
        _heightNoise = new FastNoiseLite();
        _heightNoise.Seed = seed;
        _heightNoise.NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth;
        _heightNoise.Frequency = 0.015f;
        _heightNoise.FractalType = FastNoiseLite.FractalTypeEnum.Fbm;
        _heightNoise.FractalOctaves = 4;
        _heightNoise.FractalLacunarity = 2.0f;
        _heightNoise.FractalGain = 0.5f;

        // Small detail noise — roughness
        _detailNoise = new FastNoiseLite();
        _detailNoise.Seed = seed + 100;
        _detailNoise.NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth;
        _detailNoise.Frequency = 0.08f;
        _detailNoise.FractalType = FastNoiseLite.FractalTypeEnum.Fbm;
        _detailNoise.FractalOctaves = 2;

        GD.Print($"TerrainGenerator initialized: seed={seed}, baseHeight={baseHeight}, waterLevel={WaterLevel}");
    }

    /// <summary>
    /// Returns the surface height at the given world X/Z coordinate.
    /// </summary>
    public int GetHeight(int worldX, int worldZ)
    {
        float mainVal = _heightNoise.GetNoise2D(worldX, worldZ);
        float detailVal = _detailNoise.GetNoise2D(worldX, worldZ);
        int height = _baseHeight + Mathf.RoundToInt(mainVal * 7.0f + detailVal * 1.5f);
        return Mathf.Clamp(height, 1, Chunk.SIZE - 2);
    }

    /// <summary>
    /// Fill a chunk's block array based on noise-generated terrain.
    /// Layers: stone at depth, dirt below surface, grass on top.
    /// Sand at water-edge elevations, water fills below water level.
    /// </summary>
    public void GenerateChunkBlocks(BlockType[,,] blocks, Vector3I chunkCoord)
    {
        if (chunkCoord.Y != 0) return;

        for (int lx = 0; lx < Chunk.SIZE; lx++)
        for (int lz = 0; lz < Chunk.SIZE; lz++)
        {
            int worldX = chunkCoord.X * Chunk.SIZE + lx;
            int worldZ = chunkCoord.Z * Chunk.SIZE + lz;
            int height = GetHeight(worldX, worldZ);

            for (int ly = 0; ly <= Mathf.Max(height, WaterLevel) && ly < Chunk.SIZE; ly++)
            {
                if (ly > height)
                {
                    // Above terrain but at or below water level — fill with water
                    blocks[lx, ly, lz] = BlockType.Water;
                }
                else if (ly == height)
                {
                    // Surface block
                    if (height <= WaterLevel + 1)
                        blocks[lx, ly, lz] = BlockType.Sand; // Beach
                    else
                        blocks[lx, ly, lz] = BlockType.Grass;
                }
                else if (ly >= height - 2)
                {
                    // Sub-surface
                    if (height <= WaterLevel + 1)
                        blocks[lx, ly, lz] = BlockType.Sand;
                    else
                        blocks[lx, ly, lz] = BlockType.Dirt;
                }
                else
                {
                    blocks[lx, ly, lz] = BlockType.Stone;
                }
            }
        }
    }
}
