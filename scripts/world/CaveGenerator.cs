namespace ColonySim;

using Godot;

/// <summary>
/// Generates cave networks using three layered systems combined with OR logic
/// (any system can independently carve a block):
///
/// 1. SPAGHETTI TUNNELS: Dual-threshold 1-octave noise. Two independent noise
///    fields with a 1:6 frequency ratio. 1-octave is critical — FBM fragments
///    the isosurface into disconnected pockets. Asymmetric thresholds.
///    Strong surface protection keeps caves deep; entrances punch through.
///
/// 2. CHEESE CAVERNS: Ridged-noise field for larger open chambers, only very
///    deep underground (25+ blocks below surface). Depth-scaled threshold.
///
/// 3. NOODLE TUNNELS: Very thin connecting passages deep underground.
///
/// Cave entrances are clearly visible holes in the surface — players see them
/// from above, but colonists must mine to reach the deeper networks.
/// Fully deterministic — same seed produces identical caves.
/// </summary>
public class CaveGenerator
{
    private readonly FastNoiseLite _caveNoise1;
    private readonly FastNoiseLite _caveNoise2;
    private readonly FastNoiseLite _cavernNoise;
    private readonly FastNoiseLite _noodleNoise1;
    private readonly FastNoiseLite _noodleNoise2;
    private readonly FastNoiseLite _entranceNoise;

    // === Spaghetti tunnel parameters ===
    // 1-octave noise: full [-1,1] range, smooth continuous isosurfaces.
    // Asymmetric thresholds: broad noise wider, fine noise tighter.
    // Carved volume ≈ (2*0.10) * (2*0.08) = 3.2% — connected but not overwhelming.
    private const float CaveThreshold1 = 0.10f;  // broad noise: defines cave region
    private const float CaveThreshold2 = 0.08f;  // fine noise: defines tunnel path

    // Frequency ratio 1:6 creates strongly elongated tunnel intersections.
    private const float Cave1Frequency = 0.01f;
    private const float Cave2Frequency = 0.06f;

    // Y-axis squash: < 1.0 makes tunnels prefer horizontal.
    private const float YSquash = 0.5f;

    // === Cheese cavern parameters ===
    // Ridged noise for connected chamber shapes. Only very deep underground.
    // Depth-scaled threshold: deeper = lower threshold = bigger caverns.
    private const float CavernFrequency = 0.018f;
    private const float CavernBaseThreshold = 0.85f;   // at min depth: very rare tiny pockets
    private const float CavernDeepThreshold = 0.62f;    // at full depth: moderate chambers
    private const int CavernMinDepth = 25;              // caverns only 25+ blocks below surface
    private const int CavernFullDepth = 50;             // depth where threshold reaches minimum
    private const float CavernYSquash = 0.35f;

    // === Noodle tunnel parameters ===
    // Very thin high-frequency tunnels that connect the other systems.
    private const float NoodleFrequency1 = 0.03f;
    private const float NoodleFrequency2 = 0.09f;
    private const float NoodleThreshold1 = 0.05f;  // very tight = very thin
    private const float NoodleThreshold2 = 0.05f;
    private const int NoodleMinDepth = 15;          // only deep underground

    // === Surface protection ===
    // Strong protection keeps the top layers solid. Caves live deep.
    private const int CaveMinDepth = 15;            // no caves within 15 blocks of surface — colonists must mine
    private const int CaveFadeRange = 10;           // gradual fade-in over 10 blocks below that

    // === Entrance generation ===
    // Entrances punch clearly visible holes through the surface protection.
    // Lower threshold = more frequent entrances = easier to spot from above.
    private const float EntranceThreshold = 0.52f;
    private const float EntranceFrequency = 0.008f; // lower freq = larger entrance regions

    // Floor protection: no carving at or below this Y level (bedrock).
    private const int CaveFloorLevel = 2;

    public CaveGenerator(int seed)
    {
        // Spaghetti noise 1: LOW frequency, 1 octave for smooth continuous isosurface
        _caveNoise1 = new FastNoiseLite();
        _caveNoise1.Seed = seed + 600;
        _caveNoise1.NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth;
        _caveNoise1.Frequency = Cave1Frequency;
        _caveNoise1.FractalType = FastNoiseLite.FractalTypeEnum.None;

        // Spaghetti noise 2: HIGH frequency, 1 octave
        _caveNoise2 = new FastNoiseLite();
        _caveNoise2.Seed = seed + 700;
        _caveNoise2.NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth;
        _caveNoise2.Frequency = Cave2Frequency;
        _caveNoise2.FractalType = FastNoiseLite.FractalTypeEnum.None;

        // Cheese cavern noise: ridged for connected chamber shapes
        _cavernNoise = new FastNoiseLite();
        _cavernNoise.Seed = seed + 900;
        _cavernNoise.NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth;
        _cavernNoise.Frequency = CavernFrequency;
        _cavernNoise.FractalType = FastNoiseLite.FractalTypeEnum.Ridged;
        _cavernNoise.FractalOctaves = 1;

        // Noodle noise 1: thin connecting tunnels, 1 octave
        _noodleNoise1 = new FastNoiseLite();
        _noodleNoise1.Seed = seed + 1000;
        _noodleNoise1.NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth;
        _noodleNoise1.Frequency = NoodleFrequency1;
        _noodleNoise1.FractalType = FastNoiseLite.FractalTypeEnum.None;

        // Noodle noise 2: thin connecting tunnels, 1 octave
        _noodleNoise2 = new FastNoiseLite();
        _noodleNoise2.Seed = seed + 1100;
        _noodleNoise2.NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth;
        _noodleNoise2.Frequency = NoodleFrequency2;
        _noodleNoise2.FractalType = FastNoiseLite.FractalTypeEnum.None;

        // Entrance noise: 2D noise for surface cave openings.
        // Low frequency = large entrance zones visible from above.
        _entranceNoise = new FastNoiseLite();
        _entranceNoise.Seed = seed + 800;
        _entranceNoise.NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth;
        _entranceNoise.Frequency = EntranceFrequency;
        _entranceNoise.FractalType = FastNoiseLite.FractalTypeEnum.None;

        GD.Print($"CaveGenerator initialized: spaghetti(t={CaveThreshold1}/{CaveThreshold2}, " +
                 $"f={Cave1Frequency}/{Cave2Frequency}, 1-octave), " +
                 $"caverns(ridged, f={CavernFrequency}, t={CavernBaseThreshold}-{CavernDeepThreshold}), " +
                 $"noodles(t={NoodleThreshold1}, f={NoodleFrequency1}/{NoodleFrequency2}), " +
                 $"surface(minDepth={CaveMinDepth}, fade={CaveFadeRange}), " +
                 $"entrances(thresh={EntranceThreshold}, freq={EntranceFrequency})");
    }

    /// <summary>
    /// Carve caves in a chunk's block array. Must be called AFTER terrain fill
    /// but BEFORE tree placement.
    ///
    /// Three cave systems combined with OR logic — if ANY system says carve,
    /// the block becomes Air. Entrances punch through surface protection to
    /// create clearly visible holes from above.
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
            BlockType current = blocks[lx, ly, lz];
            if (!BlockData.IsSolid(current)) continue;

            int worldX = chunkBaseX + lx;
            int worldY = chunkBaseY + ly;
            int worldZ = chunkBaseZ + lz;

            if (worldY <= CaveFloorLevel) continue;

            int surfaceY = surfaceHeights[lx * Chunk.SIZE + lz];
            int depthBelow = surfaceY - worldY;

            // Check for cave entrance (bypasses surface protection)
            bool isEntrance = false;
            if (depthBelow >= 1 && surfaceY > TerrainGenerator.WaterLevel + 3)
            {
                float entranceVal = _entranceNoise.GetNoise2D(worldX, worldZ);
                float entranceNorm = (entranceVal + 1f) * 0.5f;
                isEntrance = entranceNorm > EntranceThreshold;
            }

            float squashedY = worldY * YSquash;

            // === System 1: Cheese caverns (very deep underground only) ===
            if (depthBelow >= CavernMinDepth)
            {
                float depthT = Mathf.Clamp(
                    (float)(depthBelow - CavernMinDepth) / (CavernFullDepth - CavernMinDepth),
                    0f, 1f);
                float cavernThresh = Mathf.Lerp(CavernBaseThreshold, CavernDeepThreshold, depthT);

                float cavernY = worldY * CavernYSquash;
                float cavernVal = _cavernNoise.GetNoise3D(worldX, cavernY, worldZ);
                float cavernNorm = (cavernVal + 1f) * 0.5f;
                if (cavernNorm > cavernThresh)
                {
                    blocks[lx, ly, lz] = BlockType.Air;
                    continue;
                }
            }

            // === System 2: Noodle tunnels (deep connecting passages) ===
            if (depthBelow >= NoodleMinDepth)
            {
                float nn1 = _noodleNoise1.GetNoise3D(worldX, squashedY, worldZ);
                float nn2 = _noodleNoise2.GetNoise3D(worldX, squashedY, worldZ);
                if (Mathf.Abs(nn1) < NoodleThreshold1 && Mathf.Abs(nn2) < NoodleThreshold2)
                {
                    blocks[lx, ly, lz] = BlockType.Air;
                    continue;
                }
            }

            // === System 3: Spaghetti tunnels (main cave network) ===
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

            float n1 = _caveNoise1.GetNoise3D(worldX, squashedY, worldZ);
            float n2 = _caveNoise2.GetNoise3D(worldX, squashedY, worldZ);

            if (Mathf.Abs(n1) < t1 && Mathf.Abs(n2) < t2)
            {
                blocks[lx, ly, lz] = BlockType.Air;
            }
        }
    }
}
