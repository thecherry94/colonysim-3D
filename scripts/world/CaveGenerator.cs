namespace ColonySim;

using Godot;

/// <summary>
/// Generates cave networks using dual-threshold 3D noise ("spaghetti caves").
/// Two independent noise fields create winding tunnel-shaped voids where both
/// noise values are near their zero-crossing isosurface.
///
/// The key to spaghetti (not Swiss cheese) is:
/// 1. Tight thresholds (narrow carving band near zero-crossing)
/// 2. Very different frequencies between the two noise fields
/// 3. Y-axis squashing to make tunnels horizontal rather than spherical
///
/// Caves are suppressed near the surface and below water level.
/// Fully deterministic — same seed produces identical caves.
/// </summary>
public class CaveGenerator
{
    private readonly FastNoiseLite _caveNoise1;
    private readonly FastNoiseLite _caveNoise2;
    private readonly FastNoiseLite _entranceNoise;

    // Thresholds: blocks are carved where abs(noise) < threshold.
    // Tight values (0.07) create narrow tunnel intersections.
    // With two noise fields, carved volume ≈ (2*t1)*(2*t2) ≈ 2% of blocks.
    private const float CaveThreshold1 = 0.07f;
    private const float CaveThreshold2 = 0.07f;

    // Noise frequencies: VERY different creates elongated spaghetti tunnels.
    // If frequencies are similar, intersections are point-like (Swiss cheese).
    // noise1 at 0.02 creates broad slow-changing surfaces,
    // noise2 at 0.06 creates fine rapid-changing surfaces.
    // Their intersection is a thin winding tube.
    private const float Cave1Frequency = 0.02f;
    private const float Cave2Frequency = 0.06f;

    // Y-axis squash factor: < 1.0 makes tunnels more horizontal,
    // stretching cave noise vertically so tunnels prefer to run horizontally.
    private const float YSquash = 0.5f;

    // Surface protection: no caves within MinDepth blocks of surface.
    // Caves fade in over FadeRange blocks below MinDepth.
    private const int CaveMinDepth = 4;
    private const int CaveFadeRange = 8;

    // Entrance generation: where entrance noise exceeds this threshold,
    // surface protection is bypassed and caves can break through to the surface.
    private const float EntranceThreshold = 0.65f;
    private const float EntranceFrequency = 0.012f;

    // Floor protection: no carving at or below this Y level (bedrock).
    private const int CaveFloorLevel = 2;

    public CaveGenerator(int seed)
    {
        // Cave noise 1: LOW frequency for broad, slowly varying isosurface
        _caveNoise1 = new FastNoiseLite();
        _caveNoise1.Seed = seed + 600;
        _caveNoise1.NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth;
        _caveNoise1.Frequency = Cave1Frequency;
        _caveNoise1.FractalType = FastNoiseLite.FractalTypeEnum.Fbm;
        _caveNoise1.FractalOctaves = 2;
        _caveNoise1.FractalLacunarity = 2.0f;
        _caveNoise1.FractalGain = 0.5f;

        // Cave noise 2: HIGH frequency for fine, rapidly varying isosurface
        _caveNoise2 = new FastNoiseLite();
        _caveNoise2.Seed = seed + 700;
        _caveNoise2.NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth;
        _caveNoise2.Frequency = Cave2Frequency;
        _caveNoise2.FractalType = FastNoiseLite.FractalTypeEnum.Fbm;
        _caveNoise2.FractalOctaves = 2;
        _caveNoise2.FractalLacunarity = 2.0f;
        _caveNoise2.FractalGain = 0.5f;

        // Entrance noise: 2D noise that selectively punches cave openings through
        // the surface protection zone.
        _entranceNoise = new FastNoiseLite();
        _entranceNoise.Seed = seed + 800;
        _entranceNoise.NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth;
        _entranceNoise.Frequency = EntranceFrequency;
        _entranceNoise.FractalType = FastNoiseLite.FractalTypeEnum.None;

        GD.Print($"CaveGenerator initialized: threshold={CaveThreshold1}, " +
                 $"freq1={Cave1Frequency}, freq2={Cave2Frequency}, ySquash={YSquash}, " +
                 $"minDepth={CaveMinDepth}, entranceThreshold={EntranceThreshold}");
    }

    /// <summary>
    /// Carve caves in a chunk's block array. Must be called AFTER terrain fill
    /// but BEFORE tree placement.
    ///
    /// surfaceHeights: flat 16x16 array indexed as [lx * 16 + lz] containing
    /// the surface Y height for each column in the chunk.
    /// </summary>
    public void CarveCaves(BlockType[,,] blocks, Vector3I chunkCoord, int[] surfaceHeights)
    {
        int chunkBaseX = chunkCoord.X * Chunk.SIZE;
        int chunkBaseY = chunkCoord.Y * Chunk.SIZE;
        int chunkBaseZ = chunkCoord.Z * Chunk.SIZE;

        for (int lx = 0; lx < Chunk.SIZE; lx++)
        for (int ly = 0; ly < Chunk.SIZE; ly++)
        for (int lz = 0; lz < Chunk.SIZE; lz++)
        {
            // Only carve solid blocks (skip Air, Water, etc.)
            BlockType current = blocks[lx, ly, lz];
            if (!BlockData.IsSolid(current)) continue;

            int worldX = chunkBaseX + lx;
            int worldY = chunkBaseY + ly;
            int worldZ = chunkBaseZ + lz;

            // Floor protection: don't carve bedrock layer
            if (worldY <= CaveFloorLevel) continue;

            // Water protection: don't carve at or below water level
            if (worldY <= TerrainGenerator.WaterLevel) continue;

            // Surface protection: depth-scaled threshold
            int surfaceY = surfaceHeights[lx * Chunk.SIZE + lz];
            int depthBelow = surfaceY - worldY;

            // Check for cave entrance
            bool isEntrance = false;
            if (depthBelow >= 1 && surfaceY > TerrainGenerator.WaterLevel + 3)
            {
                float entranceVal = _entranceNoise.GetNoise2D(worldX, worldZ);
                float entranceNorm = (entranceVal + 1f) * 0.5f;
                isEntrance = entranceNorm > EntranceThreshold;
            }

            if (!isEntrance && depthBelow < CaveMinDepth) continue;

            float depthFactor;
            if (isEntrance)
            {
                depthFactor = 1.0f;
            }
            else
            {
                depthFactor = Mathf.Clamp(
                    (depthBelow - CaveMinDepth) / (float)CaveFadeRange, 0f, 1f);
            }

            float t1 = CaveThreshold1 * depthFactor;
            float t2 = CaveThreshold2 * depthFactor;

            // Y-axis squash: multiply Y coordinate to make tunnels prefer horizontal
            float squashedY = worldY * YSquash;

            // Dual-noise intersection: carve where both noise values are near zero
            float n1 = _caveNoise1.GetNoise3D(worldX, squashedY, worldZ);
            float n2 = _caveNoise2.GetNoise3D(worldX, squashedY, worldZ);

            if (Mathf.Abs(n1) < t1 && Mathf.Abs(n2) < t2)
            {
                blocks[lx, ly, lz] = BlockType.Air;
            }
        }
    }
}
