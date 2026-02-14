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
///    deep underground (45+ blocks below surface). Depth-scaled threshold.
///
/// 3. NOODLE TUNNELS: Very thin connecting passages deep underground.
///
/// LAYER SEPARATORS: Periodic cosine-based Y pinch creates solid rock floors
/// every ~25 blocks, producing distinct cave layers (like Dwarf Fortress cavern
/// layers) instead of one continuous void from top to bottom. A 2D noise
/// offsets the pinch Y-position per column so floors undulate naturally.
/// All three systems are suppressed at pinch centers.
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
    private readonly FastNoiseLite _layerOffsetNoise;

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
    // Tuned for deep world (~120 blocks underground).
    // Previous values (0.85→0.55 over 40-100) caused 89% air at depth 100 — catastrophically hollow.
    // New values create distinct chambers (5-15% air at depth) that grow modestly with depth.
    private const float CavernFrequency = 0.018f;
    private const float CavernBaseThreshold = 0.90f;    // at min depth: very rare tiny pockets
    private const float CavernDeepThreshold = 0.82f;    // at full depth: grand chambers (~8-10% carve, ~30-35% air with all systems)
    private const int CavernMinDepth = 45;              // caverns only 45+ blocks below surface
    private const int CavernFullDepth = 120;            // slower ramp-up over 75 blocks (was 60)
    private const float CavernYSquash = 0.50f;          // less extreme horizontal stretch (was 0.35)

    // === Cave layer separators ===
    // Periodic Y-based pinch creates solid rock floors that separate caves into distinct layers.
    // Without this, cheese caverns form one continuous void from top to bottom.
    // The pinch uses a cosine wave on worldY: at pinch centers (cos ≈ 1), the threshold
    // is pushed toward 1.0 (almost no carving). Between pinch centers, caves open normally.
    // Layer period of 25 blocks = ~4-5 cave layers across 120 blocks of underground.
    // PinchWidth controls the solid floor thickness (higher = thinner floors).
    // PinchStrength controls how aggressively floors suppress carving (0-1, higher = more solid).
    private const float CaveLayerPeriod = 25.0f;        // blocks between layer floors
    private const float CaveLayerPinchStrength = 0.85f;  // how much pinch tightens the threshold (0=none, 1=full)
    private const float CaveLayerPinchWidth = 3.0f;      // sharpness: higher = thinner solid floors

    // === Noodle tunnel parameters ===
    // Very thin high-frequency tunnels that connect the other systems.
    private const float NoodleFrequency1 = 0.03f;
    private const float NoodleFrequency2 = 0.09f;
    private const float NoodleThreshold1 = 0.05f;  // very tight = very thin
    private const float NoodleThreshold2 = 0.05f;
    private const int NoodleMinDepth = 25;          // only deep underground

    // === Deep attenuation ===
    // Spaghetti and noodle tunnels thin out at extreme depth where cheese caverns dominate.
    // Prevents OR-logic from combining all three systems into near-total voidification.
    private const int DeepAttenuationStart = 60;   // start thinning spaghetti/noodles here
    private const int DeepAttenuationEnd = 100;    // fully attenuated (50% strength) at this depth

    // === Surface protection ===
    // Strong protection keeps the top layers solid. Caves live deep.
    private const int CaveMinDepth = 20;            // no caves within 20 blocks of surface — colonists must mine
    private const int CaveFadeRange = 15;           // gradual fade-in over 15 blocks below that

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

        // Layer offset noise: 2D noise that shifts the layer separator Y-position
        // per XZ column so cave floors undulate naturally (±5 blocks) instead of
        // being perfectly flat planes.
        _layerOffsetNoise = new FastNoiseLite();
        _layerOffsetNoise.Seed = seed + 1200;
        _layerOffsetNoise.NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth;
        _layerOffsetNoise.Frequency = 0.02f;
        _layerOffsetNoise.FractalType = FastNoiseLite.FractalTypeEnum.None;

        GD.Print($"CaveGenerator initialized: spaghetti(t={CaveThreshold1}/{CaveThreshold2}, " +
                 $"f={Cave1Frequency}/{Cave2Frequency}, 1-octave), " +
                 $"caverns(ridged, f={CavernFrequency}, t={CavernBaseThreshold}-{CavernDeepThreshold}, " +
                 $"depth={CavernMinDepth}-{CavernFullDepth}, ySquash={CavernYSquash}), " +
                 $"layers(period={CaveLayerPeriod}, pinch={CaveLayerPinchStrength}, width={CaveLayerPinchWidth}), " +
                 $"noodles(t={NoodleThreshold1}, f={NoodleFrequency1}/{NoodleFrequency2}), " +
                 $"deepAtten({DeepAttenuationStart}-{DeepAttenuationEnd}→50%), " +
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

                // Layer separators: periodic cosine pinch creates solid rock floors
                // between cave layers. 2D noise offsets the Y position per column
                // so floors undulate naturally (±5 blocks) instead of flat planes.
                float layerOffset = _layerOffsetNoise.GetNoise2D(worldX, worldZ) * 5f;
                float layerPhase = (worldY + layerOffset) / CaveLayerPeriod * Mathf.Pi * 2f;
                // cos = 1.0 at pinch centers (solid floors), cos = -1.0 between (open caves)
                float cosVal = Mathf.Cos(layerPhase);
                // Sharpen the pinch: raise to a power so only narrow bands near cos≈1 are affected
                // pinchFactor: 0 = no pinch (open cave), 1 = full pinch (solid floor)
                float pinchFactor = Mathf.Pow(Mathf.Max(cosVal, 0f), CaveLayerPinchWidth);
                // Push threshold toward 1.0 at pinch centers (suppresses all carving)
                cavernThresh = Mathf.Lerp(cavernThresh, 1.0f, pinchFactor * CaveLayerPinchStrength);

                float cavernY = worldY * CavernYSquash;
                float cavernVal = _cavernNoise.GetNoise3D(worldX, cavernY, worldZ);
                float cavernNorm = (cavernVal + 1f) * 0.5f;
                if (cavernNorm > cavernThresh)
                {
                    blocks[lx, ly, lz] = BlockType.Air;
                    continue;
                }
            }

            // === Deep attenuation factor ===
            // At extreme depth, spaghetti and noodle tunnels thin out to avoid
            // combining with cheese caverns for near-total voidification.
            // Shrinks thresholds to 50% at DeepAttenuationEnd depth.
            float deepAtten = 1.0f;
            if (depthBelow >= DeepAttenuationStart)
            {
                float t = Mathf.Clamp(
                    (float)(depthBelow - DeepAttenuationStart) / (DeepAttenuationEnd - DeepAttenuationStart),
                    0f, 1f);
                deepAtten = Mathf.Lerp(1.0f, 0.5f, t);
            }

            // === Layer separator suppression for spaghetti/noodle ===
            // In the deep zone where cheese caverns exist, strongly suppress spaghetti/noodle
            // at layer separator Y-levels. Without this, OR-logic lets thin tunnels punch
            // tiny holes through the solid floors, creating visible leaks.
            // Lerp to 0.1 = nearly zero carving at floor centers. Rare tunnels still
            // slip through at the edges of the pinch zone where pinchFactor < 1.
            float tunnelLayerSuppression = 1.0f;
            if (depthBelow >= CavernMinDepth)
            {
                float layerOffset = _layerOffsetNoise.GetNoise2D(worldX, worldZ) * 5f;
                float layerPhase = (worldY + layerOffset) / CaveLayerPeriod * Mathf.Pi * 2f;
                float cosVal = Mathf.Cos(layerPhase);
                float pinchFactor = Mathf.Pow(Mathf.Max(cosVal, 0f), CaveLayerPinchWidth);
                // Strong suppression: tunnels at 10% strength at pinch centers
                // Connecting passages form naturally at the pinch edges (pinchFactor 0.3-0.7)
                // where suppression is partial, not at the solid core
                tunnelLayerSuppression = Mathf.Lerp(1.0f, 0.1f, pinchFactor);
            }

            // === System 2: Noodle tunnels (deep connecting passages) ===
            if (depthBelow >= NoodleMinDepth)
            {
                float nt1 = NoodleThreshold1 * deepAtten * tunnelLayerSuppression;
                float nt2 = NoodleThreshold2 * deepAtten * tunnelLayerSuppression;
                float nn1 = _noodleNoise1.GetNoise3D(worldX, squashedY, worldZ);
                float nn2 = _noodleNoise2.GetNoise3D(worldX, squashedY, worldZ);
                if (Mathf.Abs(nn1) < nt1 && Mathf.Abs(nn2) < nt2)
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

            // Apply deep attenuation and layer suppression to spaghetti tunnels too
            float t1 = CaveThreshold1 * depthFactor * deepAtten * tunnelLayerSuppression;
            float t2 = CaveThreshold2 * depthFactor * deepAtten * tunnelLayerSuppression;

            float n1 = _caveNoise1.GetNoise3D(worldX, squashedY, worldZ);
            float n2 = _caveNoise2.GetNoise3D(worldX, squashedY, worldZ);

            if (Mathf.Abs(n1) < t1 && Mathf.Abs(n2) < t2)
            {
                blocks[lx, ly, lz] = BlockType.Air;
            }
        }
    }
}
