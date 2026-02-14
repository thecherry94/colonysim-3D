namespace ColonySim;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Godot;

/// <summary>
/// Diagnostic tool that samples the full terrain generation pipeline and produces
/// structural analysis of the underground — not just aggregate numbers, but
/// ASCII cross-section maps, vertical profile columns, cave size measurements,
/// and contiguous void analysis.
///
/// Key outputs:
///   1. HORIZONTAL SLICES: ASCII maps at specific depths showing air vs rock
///      (like Y-level slicing in-game, but as text). Shows cave shapes/sizes.
///   2. VERTICAL PROFILES: North-south cross-section columns showing what
///      the underground looks like when cut open. Shows cave heights, layer
///      boundaries, and how deep caves extend.
///   3. CAVE STRUCTURE ANALYSIS: Measures contiguous air regions to find
///      actual cave sizes (not just "% air at depth").
///   4. Statistics: Block distribution, cave density by depth, ore counts.
///
/// Runs on-demand via F3. Saves to diagnostics/ folder for comparison.
/// Thread-safe: creates its own TerrainGenerator instance.
/// </summary>
public static class WorldGenAnalyzer
{
    // Sample area: 4x4 chunks horizontally = 64x64 blocks
    private const int SampleChunksXZ = 4;
    private const int SampleBlocksXZ = SampleChunksXZ * Chunk.SIZE; // 64

    private const string DiagnosticsFolder = "diagnostics";

    /// <summary>
    /// Run full analysis centered on a world XZ position.
    /// </summary>
    public static string Analyze(int centerWorldX, int centerWorldZ, int seed, int yChunkLayers,
                                  string label = null)
    {
        GD.Print("=== WORLD GEN ANALYZER: Starting analysis... ===");
        var startTime = DateTime.UtcNow;

        var gen = new TerrainGenerator(seed);
        int totalHeight = yChunkLayers * Chunk.SIZE;

        // World-space bounds of our sample area
        int centerChunkX = FloorDiv(centerWorldX, Chunk.SIZE);
        int centerChunkZ = FloorDiv(centerWorldZ, Chunk.SIZE);
        int startChunkX = centerChunkX - SampleChunksXZ / 2;
        int startChunkZ = centerChunkZ - SampleChunksXZ / 2;
        int worldStartX = startChunkX * Chunk.SIZE;
        int worldStartZ = startChunkZ * Chunk.SIZE;

        // === Phase 1: Generate all blocks into a flat 3D array ===
        // Store as [x, y, z] indexed from (worldStartX, 0, worldStartZ)
        var world = new BlockType[SampleBlocksXZ, totalHeight, SampleBlocksXZ];
        var surfaceHeights = new int[SampleBlocksXZ, SampleBlocksXZ];
        var blocks = new BlockType[Chunk.SIZE, Chunk.SIZE, Chunk.SIZE];

        for (int cx = startChunkX; cx < startChunkX + SampleChunksXZ; cx++)
        for (int cz = startChunkZ; cz < startChunkZ + SampleChunksXZ; cz++)
        {
            // Surface heights for this chunk column
            int localBaseX = (cx - startChunkX) * Chunk.SIZE;
            int localBaseZ = (cz - startChunkZ) * Chunk.SIZE;
            for (int lx = 0; lx < Chunk.SIZE; lx++)
            for (int lz = 0; lz < Chunk.SIZE; lz++)
            {
                surfaceHeights[localBaseX + lx, localBaseZ + lz] =
                    gen.GetHeight(cx * Chunk.SIZE + lx, cz * Chunk.SIZE + lz);
            }

            // Generate each Y layer
            for (int cy = 0; cy < yChunkLayers; cy++)
            {
                Array.Clear(blocks);
                gen.GenerateChunkBlocks(blocks, new Vector3I(cx, cy, cz));

                for (int lx = 0; lx < Chunk.SIZE; lx++)
                for (int ly = 0; ly < Chunk.SIZE; ly++)
                for (int lz = 0; lz < Chunk.SIZE; lz++)
                {
                    world[localBaseX + lx, cy * Chunk.SIZE + ly, localBaseZ + lz] =
                        blocks[lx, ly, lz];
                }
            }
        }

        var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;

        // === Phase 2: Compute statistics ===
        var stats = ComputeStatistics(world, surfaceHeights, totalHeight);

        // === Phase 3: Build report ===
        var sb = new StringBuilder();

        sb.AppendLine("=== WORLD GEN ANALYSIS REPORT ===");
        sb.AppendLine($"Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        if (!string.IsNullOrEmpty(label))
            sb.AppendLine($"Label: {label}");
        sb.AppendLine($"Sample area: {SampleBlocksXZ}x{SampleBlocksXZ} blocks ({SampleChunksXZ}x{SampleChunksXZ} chunks)");
        sb.AppendLine($"World origin: ({worldStartX}, {worldStartZ})");
        sb.AppendLine($"Center: ({centerWorldX}, {centerWorldZ}), Seed: {seed}");
        sb.AppendLine($"World height: {totalHeight} blocks ({yChunkLayers} Y layers)");
        sb.AppendLine($"Generation time: {elapsed:F0}ms");

        // Surface height
        sb.AppendLine();
        sb.AppendLine("--- SURFACE HEIGHT ---");
        sb.AppendLine($"  Min: {stats.MinSurface}  Max: {stats.MaxSurface}  Avg: {stats.AvgSurface:F1}");
        sb.AppendLine($"  Underground depth range: {stats.MinSurface} to {stats.MaxSurface} blocks (surface Y to Y=0)");

        // Overall distribution
        sb.AppendLine();
        sb.AppendLine("--- OVERALL BLOCK DISTRIBUTION ---");
        sb.AppendLine($"  Total blocks: {stats.TotalBlocks:N0}");
        sb.AppendLine($"  Air:   {stats.TotalAir:N0} ({100.0 * stats.TotalAir / stats.TotalBlocks:F1}%)");
        sb.AppendLine($"  Water: {stats.TotalWater:N0} ({100.0 * stats.TotalWater / stats.TotalBlocks:F1}%)");
        sb.AppendLine($"  Solid: {stats.TotalSolid:N0} ({100.0 * stats.TotalSolid / stats.TotalBlocks:F1}%)");

        // Block type breakdown
        AppendBlockTypeCounts(sb, stats);

        // Cave density by depth (5-block buckets)
        AppendCaveDensity(sb, stats);

        // Ore distribution
        AppendOreDistribution(sb, stats);

        // Rock type by depth
        AppendRockByDepth(sb, stats);

        // === STRUCTURAL ANALYSIS (the useful part) ===

        // Horizontal cross-section slices at key depths
        sb.AppendLine();
        sb.AppendLine("=========================================================");
        sb.AppendLine("  STRUCTURAL ANALYSIS: HORIZONTAL CROSS-SECTION SLICES");
        sb.AppendLine("=========================================================");
        sb.AppendLine("Legend: . = solid rock  ' ' = air (cave)  ~ = water  o = ore  @ = surface");

        int[] sliceDepths = { 5, 10, 15, 20, 25, 30, 40, 50, 60, 80, 100, 120 };
        foreach (int depth in sliceDepths)
        {
            AppendHorizontalSlice(sb, world, surfaceHeights, depth, totalHeight);
        }

        // Vertical cross-section profiles (N-S and E-W cuts through center)
        sb.AppendLine();
        sb.AppendLine("=========================================================");
        sb.AppendLine("  STRUCTURAL ANALYSIS: VERTICAL CROSS-SECTIONS");
        sb.AppendLine("=========================================================");
        sb.AppendLine("Legend: . = solid  ' ' = air (cave)  ~ = water  ^ = surface  o = ore");
        sb.AppendLine("  Y-axis is depth below surface (0=surface, increases downward)");

        // N-S cut through center (X = middle of sample)
        AppendVerticalSlice(sb, world, surfaceHeights, totalHeight,
            SampleBlocksXZ / 2, true, "N-S (center X)");

        // E-W cut through center (Z = middle of sample)
        AppendVerticalSlice(sb, world, surfaceHeights, totalHeight,
            SampleBlocksXZ / 2, false, "E-W (center Z)");

        // Additional N-S cuts at 1/4 and 3/4
        AppendVerticalSlice(sb, world, surfaceHeights, totalHeight,
            SampleBlocksXZ / 4, true, "N-S (quarter X)");

        // Cave void size analysis
        sb.AppendLine();
        sb.AppendLine("=========================================================");
        sb.AppendLine("  CAVE VOID ANALYSIS");
        sb.AppendLine("=========================================================");
        AppendCaveVoidAnalysis(sb, world, surfaceHeights, totalHeight);

        // Vertical cave height analysis (how tall are caves at each depth)
        sb.AppendLine();
        sb.AppendLine("=========================================================");
        sb.AppendLine("  CAVE HEIGHT PROFILE (vertical air runs per column)");
        sb.AppendLine("=========================================================");
        AppendCaveHeightProfile(sb, world, surfaceHeights, totalHeight);

        // Parameter reference
        AppendParameterReference(sb);

        sb.AppendLine();
        sb.AppendLine("=== ANALYSIS COMPLETE ===");

        string report = sb.ToString();
        string filePath = SaveReport(report, seed, label);

        // Print summary to console (not the full report — it's huge)
        GD.Print($"=== WorldGenAnalyzer complete ({elapsed:F0}ms) ===");
        GD.Print($"  Surface: min={stats.MinSurface} max={stats.MaxSurface} avg={stats.AvgSurface:F1}");
        GD.Print($"  Underground air: {100.0 * stats.UndergroundAir / Math.Max(1, stats.UndergroundTotal):F1}%");
        GD.Print($"  Report saved to: {filePath}");

        return filePath;
    }

    // ========================================================================
    // Horizontal slice: 64x64 ASCII map at a specific depth below surface
    // ========================================================================
    private static void AppendHorizontalSlice(StringBuilder sb, BlockType[,,] world,
        int[,] surfaceHeights, int targetDepth, int totalHeight)
    {
        sb.AppendLine();
        sb.AppendLine($"--- DEPTH {targetDepth} blocks below surface ---");

        int airCount = 0;
        int totalCount = 0;
        var map = new char[SampleBlocksXZ, SampleBlocksXZ];

        for (int x = 0; x < SampleBlocksXZ; x++)
        for (int z = 0; z < SampleBlocksXZ; z++)
        {
            int surfY = surfaceHeights[x, z];
            int worldY = surfY - targetDepth;

            if (worldY < 0 || worldY >= totalHeight)
            {
                map[x, z] = '?';
                continue;
            }

            BlockType type = world[x, worldY, z];
            totalCount++;

            if (type == BlockType.Air)
            {
                map[x, z] = ' ';
                airCount++;
            }
            else if (type == BlockType.Water)
                map[x, z] = '~';
            else if (IsOre(type))
                map[x, z] = 'o';
            else
                map[x, z] = '.';
        }

        float airPct = totalCount > 0 ? 100f * airCount / totalCount : 0;
        sb.AppendLine($"  Air at this depth: {airPct:F1}% ({airCount}/{totalCount})");

        // Print the map (Z increases downward = south)
        // Downsample to 64 chars wide — print every block
        sb.Append("  +");
        for (int x = 0; x < SampleBlocksXZ; x++) sb.Append('-');
        sb.AppendLine("+");

        for (int z = 0; z < SampleBlocksXZ; z++)
        {
            sb.Append("  |");
            for (int x = 0; x < SampleBlocksXZ; x++)
                sb.Append(map[x, z]);
            sb.AppendLine("|");
        }

        sb.Append("  +");
        for (int x = 0; x < SampleBlocksXZ; x++) sb.Append('-');
        sb.AppendLine("+");
    }

    // ========================================================================
    // Vertical slice: cross-section showing depth profile
    // ========================================================================
    private static void AppendVerticalSlice(StringBuilder sb, BlockType[,,] world,
        int[,] surfaceHeights, int totalHeight, int fixedAxis, bool fixX, string title)
    {
        sb.AppendLine();
        sb.AppendLine($"--- Vertical slice: {title} ---");
        sb.AppendLine("  (horizontal = position, vertical = depth below surface, 0=surface at top)");

        // For each position along the slice, build a column relative to surface
        int maxDepth = 140; // max depth to show
        int sliceLen = SampleBlocksXZ;

        // Header
        sb.Append("  Depth ");
        for (int i = 0; i < sliceLen; i++)
        {
            if (i % 10 == 0)
                sb.Append((i / 10) % 10);
            else
                sb.Append(' ');
        }
        sb.AppendLine();

        for (int depth = -2; depth <= maxDepth; depth++)
        {
            sb.Append($"  {depth,4}  ");

            for (int pos = 0; pos < sliceLen; pos++)
            {
                int x = fixX ? fixedAxis : pos;
                int z = fixX ? pos : fixedAxis;
                int surfY = surfaceHeights[x, z];
                int worldY = surfY - depth;

                if (worldY < 0 || worldY >= totalHeight)
                {
                    sb.Append('?');
                    continue;
                }

                if (depth < 0)
                {
                    // Above surface
                    BlockType type = world[x, worldY, z];
                    sb.Append(type == BlockType.Air ? ' ' : '^');
                    continue;
                }

                if (depth == 0)
                {
                    sb.Append('^'); // Surface marker
                    continue;
                }

                BlockType block = world[x, worldY, z];
                if (block == BlockType.Air)
                    sb.Append(' ');
                else if (block == BlockType.Water)
                    sb.Append('~');
                else if (IsOre(block))
                    sb.Append('o');
                else
                    sb.Append('.');
            }
            sb.AppendLine();
        }
    }

    // ========================================================================
    // Cave void analysis: flood-fill to measure contiguous cave sizes
    // ========================================================================
    private static void AppendCaveVoidAnalysis(StringBuilder sb, BlockType[,,] world,
        int[,] surfaceHeights, int totalHeight)
    {
        // Only analyze underground air (below surface)
        // Use 2D flood fill on horizontal slices at specific depths to measure
        // how large individual cave openings are

        int[] analyzeDepths = { 10, 20, 30, 40, 50, 60, 80, 100 };

        sb.AppendLine("  Contiguous air region sizes at each depth (2D flood fill on horizontal slice):");
        sb.AppendLine($"  {"Depth",-8} {"Regions",-10} {"Largest",-10} {"Avg Size",-10} {"Median",-10} {"Total Air",-10}");

        foreach (int depth in analyzeDepths)
        {
            var regionSizes = FloodFillSlice(world, surfaceHeights, depth, totalHeight);

            if (regionSizes.Count == 0)
            {
                sb.AppendLine($"  {depth,-8} {"0",-10} {"-",-10} {"-",-10} {"-",-10} {"0",-10}");
                continue;
            }

            regionSizes.Sort((a, b) => b.CompareTo(a)); // Largest first
            int largest = regionSizes[0];
            int totalAir = 0;
            foreach (int s in regionSizes) totalAir += s;
            float avg = (float)totalAir / regionSizes.Count;
            int median = regionSizes[regionSizes.Count / 2];

            sb.AppendLine($"  {depth,-8} {regionSizes.Count,-10} {largest,-10} {avg,-10:F1} {median,-10} {totalAir,-10}");

            // Show top 5 largest regions
            int showCount = Math.Min(5, regionSizes.Count);
            sb.Append($"           Top {showCount}: ");
            for (int i = 0; i < showCount; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(regionSizes[i]);
            }
            sb.AppendLine(" blocks");
        }
    }

    /// <summary>
    /// 2D flood fill on a horizontal slice at a given depth below surface.
    /// Returns list of contiguous air region sizes.
    /// </summary>
    private static List<int> FloodFillSlice(BlockType[,,] world, int[,] surfaceHeights,
        int depth, int totalHeight)
    {
        var visited = new bool[SampleBlocksXZ, SampleBlocksXZ];
        var sizes = new List<int>();

        for (int x = 0; x < SampleBlocksXZ; x++)
        for (int z = 0; z < SampleBlocksXZ; z++)
        {
            if (visited[x, z]) continue;

            int surfY = surfaceHeights[x, z];
            int worldY = surfY - depth;
            if (worldY < 0 || worldY >= totalHeight) continue;

            if (world[x, worldY, z] != BlockType.Air) continue;

            // BFS flood fill
            int size = 0;
            var queue = new Queue<(int, int)>();
            queue.Enqueue((x, z));
            visited[x, z] = true;

            while (queue.Count > 0)
            {
                var (cx, cz) = queue.Dequeue();
                size++;

                // 4-connected neighbors
                int[] dx = { 1, -1, 0, 0 };
                int[] dz = { 0, 0, 1, -1 };
                for (int d = 0; d < 4; d++)
                {
                    int nx = cx + dx[d];
                    int nz = cz + dz[d];
                    if (nx < 0 || nx >= SampleBlocksXZ || nz < 0 || nz >= SampleBlocksXZ) continue;
                    if (visited[nx, nz]) continue;

                    int nSurfY = surfaceHeights[nx, nz];
                    int nWorldY = nSurfY - depth;
                    if (nWorldY < 0 || nWorldY >= totalHeight) continue;

                    if (world[nx, nWorldY, nz] == BlockType.Air)
                    {
                        visited[nx, nz] = true;
                        queue.Enqueue((nx, nz));
                    }
                }
            }

            sizes.Add(size);
        }

        return sizes;
    }

    // ========================================================================
    // Cave height profile: how tall are vertical air runs at each depth
    // ========================================================================
    private static void AppendCaveHeightProfile(StringBuilder sb, BlockType[,,] world,
        int[,] surfaceHeights, int totalHeight)
    {
        sb.AppendLine("  For each XZ column, scan underground for vertical air runs.");
        sb.AppendLine("  Shows how tall caves are at different depths.");
        sb.AppendLine();

        // Bucket by depth: track air run heights
        int bucketSize = 10;
        int maxBuckets = 15; // 0-150 depth
        var runHeights = new List<int>[maxBuckets];
        for (int i = 0; i < maxBuckets; i++)
            runHeights[i] = new List<int>();

        for (int x = 0; x < SampleBlocksXZ; x++)
        for (int z = 0; z < SampleBlocksXZ; z++)
        {
            int surfY = surfaceHeights[x, z];
            int currentRunHeight = 0;
            int runStartDepth = -1;

            // Scan from just below surface downward
            for (int depth = 1; depth <= Math.Min(surfY, 150); depth++)
            {
                int worldY = surfY - depth;
                if (worldY < 0 || worldY >= totalHeight) break;

                if (world[x, worldY, z] == BlockType.Air)
                {
                    if (currentRunHeight == 0) runStartDepth = depth;
                    currentRunHeight++;
                }
                else
                {
                    if (currentRunHeight > 0)
                    {
                        // Record this air run
                        int bucket = Math.Min(runStartDepth / bucketSize, maxBuckets - 1);
                        runHeights[bucket].Add(currentRunHeight);
                        currentRunHeight = 0;
                    }
                }
            }

            // Don't forget trailing run
            if (currentRunHeight > 0)
            {
                int bucket = Math.Min(runStartDepth / bucketSize, maxBuckets - 1);
                runHeights[bucket].Add(currentRunHeight);
            }
        }

        sb.AppendLine($"  {"Depth Range",-16} {"Runs",-8} {"Max H",-8} {"Avg H",-8} {"Med H",-8} {"1blk%",-8} {"5+blk%",-8} {"10+blk%",-8}");
        for (int b = 0; b < maxBuckets; b++)
        {
            var runs = runHeights[b];
            if (runs.Count == 0) continue;

            runs.Sort();
            int maxH = runs[runs.Count - 1];
            float avgH = 0;
            int oneBlock = 0, fivePlus = 0, tenPlus = 0;
            foreach (int h in runs)
            {
                avgH += h;
                if (h == 1) oneBlock++;
                if (h >= 5) fivePlus++;
                if (h >= 10) tenPlus++;
            }
            avgH /= runs.Count;
            int medH = runs[runs.Count / 2];

            int dStart = b * bucketSize;
            int dEnd = dStart + bucketSize - 1;
            float onePct = 100f * oneBlock / runs.Count;
            float fivePct = 100f * fivePlus / runs.Count;
            float tenPct = 100f * tenPlus / runs.Count;

            sb.AppendLine($"  {dStart,3}-{dEnd,-3} blocks  {runs.Count,-8} {maxH,-8} {avgH,-8:F1} {medH,-8} {onePct,-8:F0} {fivePct,-8:F0} {tenPct,-8:F0}");
        }
    }

    // ========================================================================
    // Statistics computation
    // ========================================================================
    private struct AnalysisStats
    {
        public int MinSurface, MaxSurface;
        public float AvgSurface;
        public long TotalBlocks, TotalAir, TotalWater, TotalSolid;
        public long UndergroundAir, UndergroundTotal;
        public Dictionary<BlockType, long> TotalByType;
        public long[] AirByBucket, SolidByBucket;
        public Dictionary<BlockType, long[]> OreByBucket;
        public Dictionary<int, Dictionary<BlockType, int>> BlocksByDepth;
        public int BucketSize, MaxBuckets;
    }

    private static AnalysisStats ComputeStatistics(BlockType[,,] world, int[,] surfaceHeights, int totalHeight)
    {
        int bucketSize = 5;
        int maxBuckets = (totalHeight / bucketSize) + 1;

        var stats = new AnalysisStats
        {
            MinSurface = int.MaxValue,
            MaxSurface = int.MinValue,
            TotalByType = new Dictionary<BlockType, long>(),
            AirByBucket = new long[maxBuckets],
            SolidByBucket = new long[maxBuckets],
            OreByBucket = new Dictionary<BlockType, long[]>(),
            BlocksByDepth = new Dictionary<int, Dictionary<BlockType, int>>(),
            BucketSize = bucketSize,
            MaxBuckets = maxBuckets,
        };

        long surfaceSum = 0;
        int surfaceCount = 0;

        for (int x = 0; x < SampleBlocksXZ; x++)
        for (int z = 0; z < SampleBlocksXZ; z++)
        {
            int surfY = surfaceHeights[x, z];
            stats.MinSurface = Math.Min(stats.MinSurface, surfY);
            stats.MaxSurface = Math.Max(stats.MaxSurface, surfY);
            surfaceSum += surfY;
            surfaceCount++;

            for (int y = 0; y < totalHeight; y++)
            {
                BlockType type = world[x, y, z];
                stats.TotalBlocks++;

                if (!stats.TotalByType.ContainsKey(type)) stats.TotalByType[type] = 0;
                stats.TotalByType[type]++;

                if (type == BlockType.Air) stats.TotalAir++;
                else if (type == BlockType.Water) stats.TotalWater++;
                else stats.TotalSolid++;

                int depthBelow = surfY - y;
                if (depthBelow > 0 && y < surfY)
                {
                    stats.UndergroundTotal++;
                    int bucket = Math.Min(depthBelow / bucketSize, maxBuckets - 1);

                    if (type == BlockType.Air)
                    {
                        stats.UndergroundAir++;
                        stats.AirByBucket[bucket]++;
                    }
                    else
                    {
                        stats.SolidByBucket[bucket]++;
                    }

                    if (IsOre(type))
                    {
                        if (!stats.OreByBucket.ContainsKey(type))
                            stats.OreByBucket[type] = new long[maxBuckets];
                        stats.OreByBucket[type][bucket]++;
                    }

                    if (depthBelow >= 0 && depthBelow % 5 == 0)
                    {
                        if (!stats.BlocksByDepth.ContainsKey(depthBelow))
                            stats.BlocksByDepth[depthBelow] = new Dictionary<BlockType, int>();
                        var dd = stats.BlocksByDepth[depthBelow];
                        if (!dd.ContainsKey(type)) dd[type] = 0;
                        dd[type]++;
                    }
                }
            }
        }

        stats.AvgSurface = surfaceCount > 0 ? (float)surfaceSum / surfaceCount : 0;
        return stats;
    }

    // ========================================================================
    // Report sections (aggregate stats)
    // ========================================================================
    private static void AppendBlockTypeCounts(StringBuilder sb, AnalysisStats stats)
    {
        sb.AppendLine();
        sb.AppendLine("--- BLOCK TYPE COUNTS ---");
        var sorted = new List<KeyValuePair<BlockType, long>>(stats.TotalByType);
        sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
        foreach (var kvp in sorted)
        {
            if (kvp.Value == 0) continue;
            float pct = 100.0f * kvp.Value / stats.TotalBlocks;
            float pctSolid = kvp.Key != BlockType.Air && kvp.Key != BlockType.Water && stats.TotalSolid > 0
                ? 100.0f * kvp.Value / stats.TotalSolid : 0;
            string sp = pctSolid > 0 ? $" ({pctSolid:F1}% of solid)" : "";
            sb.AppendLine($"  {kvp.Key,-15} {kvp.Value,10:N0}  ({pct:F2}%){sp}");
        }
    }

    private static void AppendCaveDensity(StringBuilder sb, AnalysisStats stats)
    {
        sb.AppendLine();
        sb.AppendLine("--- CAVE DENSITY BY DEPTH (air% of underground) ---");
        sb.AppendLine($"  {"Depth",-14} {"Air",8} {"Solid",10} {"Cave%",8}  Bar");
        for (int b = 0; b < stats.MaxBuckets; b++)
        {
            long air = stats.AirByBucket[b];
            long solid = stats.SolidByBucket[b];
            long total = air + solid;
            if (total == 0) continue;

            float pct = 100.0f * air / total;
            string bar = new string('#', Math.Min((int)(pct / 2), 40));
            int ds = b * stats.BucketSize;
            int de = ds + stats.BucketSize - 1;
            sb.AppendLine($"  {ds,3}-{de,-3}       {air,8:N0} {solid,10:N0} {pct,7:F1}%  {bar}");
        }
    }

    private static void AppendOreDistribution(StringBuilder sb, AnalysisStats stats)
    {
        sb.AppendLine();
        sb.AppendLine("--- ORE DISTRIBUTION BY DEPTH ---");
        if (stats.OreByBucket.Count == 0)
        {
            sb.AppendLine("  No ores found.");
            return;
        }
        foreach (var kvp in stats.OreByBucket)
        {
            sb.AppendLine($"  {kvp.Key}:");
            long total = 0;
            for (int b = 0; b < kvp.Value.Length; b++)
            {
                if (kvp.Value[b] == 0) continue;
                total += kvp.Value[b];
                int ds = b * stats.BucketSize;
                int de = ds + stats.BucketSize - 1;
                string bar = new string('#', Math.Min((int)(kvp.Value[b] / 10), 40));
                sb.AppendLine($"    depth {ds,3}-{de,-3}: {kvp.Value[b],6:N0}  {bar}");
            }
            sb.AppendLine($"    TOTAL: {total:N0}");
        }
    }

    private static void AppendRockByDepth(StringBuilder sb, AnalysisStats stats)
    {
        sb.AppendLine();
        sb.AppendLine("--- ROCK TYPE BY DEPTH ---");
        var depths = new List<int>(stats.BlocksByDepth.Keys);
        depths.Sort();
        foreach (int depth in depths)
        {
            if (depth < 0 || depth > 150) continue;
            var dict = stats.BlocksByDepth[depth];
            int totalAt = 0, airAt = 0;
            foreach (var kvp in dict)
            {
                totalAt += kvp.Value;
                if (kvp.Key == BlockType.Air) airAt += kvp.Value;
            }
            if (totalAt == 0) continue;

            var solidTypes = new List<string>();
            foreach (var kvp in dict)
            {
                if (kvp.Key == BlockType.Air || kvp.Key == BlockType.Water) continue;
                if (kvp.Value == 0) continue;
                int solidTotal = totalAt - airAt;
                float pct = solidTotal > 0 ? 100.0f * kvp.Value / solidTotal : 0;
                if (pct >= 1.0f) solidTypes.Add($"{kvp.Key}:{pct:F0}%");
            }

            string band = depth <= 3 ? "SOIL" : depth <= 40 ? "UPPER" : depth <= 100 ? "MID" : "DEEP";
            float airPct = 100.0f * airAt / totalAt;
            sb.AppendLine($"  Depth {depth,3}: [{band,-5}] air={airPct:F0}%  rocks: {string.Join(", ", solidTypes)}");
        }
    }

    private static void AppendParameterReference(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("--- CAVE SYSTEM PARAMETERS (reference) ---");
        sb.AppendLine($"  Spaghetti: freq=0.01/0.06, thresh=0.10/0.08, ySquash=0.5");
        sb.AppendLine($"  Caverns:   freq=0.018, ridged, thresh=Lerp(0.90,0.82), depth 45-120, ySquash=0.50");
        sb.AppendLine($"  Layers:    period=25, pinchStrength=0.85, pinchWidth=3.0, offsetNoise freq=0.02 ±5 blocks");
        sb.AppendLine($"  Noodles:   freq=0.03/0.09, thresh=0.05/0.05, minDepth=25");
        sb.AppendLine($"  DeepAtten: depth 60-100 → 50% threshold reduction for spaghetti+noodles");
        sb.AppendLine($"  TunnelLayerSuppress: 90% at pinch centers (connecting passages form at pinch edges)");
        sb.AppendLine($"  Surface:   minDepth=20, fadeRange=15");
        sb.AppendLine($"  Entrances: thresh=0.52, freq=0.008");
        sb.AppendLine($"  Floor:     bedrock Y<=2");
        sb.AppendLine();
        sb.AppendLine("--- GEOLOGY PARAMETERS (reference) ---");
        sb.AppendLine($"  Soil: 0-3, Upper: 4-40, Mid: 40-100, Deep: 100+ (±8 boundary)");
        sb.AppendLine($"  Province: freq=0.002 2D, Blob: freq=0.05 3D, thresh=0.45");
    }

    // ========================================================================
    // Utilities
    // ========================================================================
    private static string SaveReport(string report, int seed, string label)
    {
        string projectDir = ProjectSettings.GlobalizePath("res://");
        string diagDir = Path.Combine(projectDir, DiagnosticsFolder);
        if (!Directory.Exists(diagDir))
        {
            Directory.CreateDirectory(diagDir);
            GD.Print($"Created diagnostics folder: {diagDir}");
        }

        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        string labelPart = string.IsNullOrEmpty(label) ? "" : $"_{label.Replace(' ', '_')}";
        string fileName = $"worldgen_{seed}_{timestamp}{labelPart}.txt";
        string filePath = Path.Combine(diagDir, fileName);

        File.WriteAllText(filePath, report);
        return filePath;
    }

    private static bool IsOre(BlockType type) =>
        type == BlockType.CoalOre || type == BlockType.IronOre ||
        type == BlockType.CopperOre || type == BlockType.TinOre;

    private static int FloorDiv(int a, int b) =>
        a >= 0 ? a / b : (a - b + 1) / b;
}
