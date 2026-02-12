namespace ColonySim;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Manages chunks in a Dictionary keyed by chunk coordinate.
/// Provides world-space block access and coordinate conversion.
/// Supports vertical chunk stacking (multiple Y layers).
/// </summary>
[Tool]
public partial class World : Node3D
{
    private readonly Dictionary<Vector3I, Chunk> _chunks = new();
    private TerrainGenerator _terrainGenerator;
    private int _yChunkLayers = 4;

    // Chunk streaming state
    private Vector2I? _lastCameraChunkXZ;
    private readonly Queue<Vector3I> _loadQueue = new();

    // Background chunk generation: terrain + blocks computed on thread pool,
    // Godot objects created on main thread from completed results.
    private readonly ConcurrentQueue<ChunkGenResult> _genResults = new();
    private readonly HashSet<Vector3I> _generating = new();  // coords currently being generated
    private readonly HashSet<Vector3I> _unloadedWhileGenerating = new();  // coords unloaded before gen completed
    private const int MaxConcurrentGens = 8;

    private struct ChunkGenResult
    {
        public Vector3I Coord;
        public BlockType[,,] Blocks;
        public bool IsEmpty;
    }

    // Queue of chunks that need mesh (re)generation on the main thread.
    // Includes newly loaded chunks + their neighbors for cross-chunk face culling.
    // Processed with a per-frame budget to avoid stutter.
    private readonly Queue<Vector3I> _meshQueue = new();
    private readonly HashSet<Vector3I> _meshQueueSet = new();  // dedup: avoid queueing same chunk twice
    private const int MaxMeshPerFrame = 4;

    // Modified chunk cache: stores block data for dirty chunks that were unloaded.
    // On reload, cached data is used instead of regenerating from noise.
    private readonly Dictionary<Vector3I, BlockType[,,]> _modifiedChunkCache = new();

    /// <summary>
    /// Set the terrain generator externally (from Main, which owns the seed).
    /// Must be called before LoadChunkArea().
    /// </summary>
    public void SetTerrainGenerator(TerrainGenerator gen)
    {
        _terrainGenerator = gen;
    }

    /// <summary>
    /// Set the number of vertical chunk layers (default 4 = 64 blocks tall).
    /// Must be called before LoadChunkArea().
    /// </summary>
    public void SetYChunkLayers(int layers)
    {
        _yChunkLayers = layers;
    }

    /// <summary>
    /// Returns the surface height at a world X/Z coordinate.
    /// Used for positioning camera, colonist spawn, etc.
    /// </summary>
    public int GetSurfaceHeight(int worldX, int worldZ)
    {
        if (_terrainGenerator == null) return 30;
        return _terrainGenerator.GetHeight(worldX, worldZ);
    }

    /// <summary>
    /// Returns the biome at a world X/Z coordinate.
    /// </summary>
    public BiomeType GetBiome(int worldX, int worldZ)
    {
        if (_terrainGenerator == null) return BiomeType.Grassland;
        return _terrainGenerator.GetBiome(worldX, worldZ);
    }

    /// <summary>
    /// Convert world block coordinate to chunk coordinate.
    /// Uses floor division (not truncation) so negative coords work correctly.
    /// </summary>
    public static Vector3I WorldToChunkCoord(Vector3I worldBlock)
    {
        return new Vector3I(
            FloorDiv(worldBlock.X, Chunk.SIZE),
            FloorDiv(worldBlock.Y, Chunk.SIZE),
            FloorDiv(worldBlock.Z, Chunk.SIZE)
        );
    }

    /// <summary>
    /// Convert world block coordinate to local chunk coordinate (0..15).
    /// Handles negative values with double-modulo pattern.
    /// </summary>
    public static Vector3I WorldToLocalCoord(Vector3I worldBlock)
    {
        return new Vector3I(
            ((worldBlock.X % Chunk.SIZE) + Chunk.SIZE) % Chunk.SIZE,
            ((worldBlock.Y % Chunk.SIZE) + Chunk.SIZE) % Chunk.SIZE,
            ((worldBlock.Z % Chunk.SIZE) + Chunk.SIZE) % Chunk.SIZE
        );
    }

    /// <summary>
    /// Get block type at a world block coordinate. Returns Air for unloaded chunks.
    /// </summary>
    public BlockType GetBlock(Vector3I worldBlock)
    {
        var chunkCoord = WorldToChunkCoord(worldBlock);
        if (!_chunks.TryGetValue(chunkCoord, out var chunk))
            return BlockType.Air;
        var local = WorldToLocalCoord(worldBlock);
        return chunk.GetBlock(local.X, local.Y, local.Z);
    }

    /// <summary>
    /// Set block type at a world block coordinate. No-op for unloaded chunks.
    /// Does NOT auto-regenerate mesh — caller must call RegenerateChunkMesh().
    /// </summary>
    public void SetBlock(Vector3I worldBlock, BlockType type)
    {
        var chunkCoord = WorldToChunkCoord(worldBlock);
        if (!_chunks.TryGetValue(chunkCoord, out var chunk))
            return;
        var local = WorldToLocalCoord(worldBlock);
        chunk.SetBlock(local.X, local.Y, local.Z, type);
    }

    /// <summary>
    /// Load a grid of chunks centered at the given chunk coordinate.
    /// Loads multiple Y layers (0 to _yChunkLayers-1) for vertical terrain.
    /// Terrain generator must be set via SetTerrainGenerator() before calling this.
    /// </summary>
    public void LoadChunkArea(Vector3I center, int radius)
    {
        _terrainGenerator ??= new TerrainGenerator();

        int horizontalCount = (2 * radius + 1) * (2 * radius + 1);
        int totalChunks = horizontalCount * _yChunkLayers;
        int loaded = 0;

        GD.Print($"LoadChunkArea: loading {totalChunks} chunks ({2 * radius + 1}x{2 * radius + 1} x {_yChunkLayers} Y layers)...");

        for (int x = center.X - radius; x <= center.X + radius; x++)
        for (int z = center.Z - radius; z <= center.Z + radius; z++)
        for (int y = 0; y < _yChunkLayers; y++)
        {
            var coord = new Vector3I(x, y, z);
            if (_chunks.ContainsKey(coord)) continue;
            LoadChunk(coord);
            loaded++;
            if (loaded % 100 == 0)
                GD.Print($"  Loading chunks: {loaded}/{totalChunks}...");
        }

        GD.Print($"LoadChunkArea: {loaded} chunks loaded");

        // After all chunks loaded, regenerate all meshes for cross-chunk face culling
        RegenerateAllMeshes();
    }

    /// <summary>
    /// Stream chunks around the camera position. Call every frame from Main._Process().
    /// Queues new chunks for loading and unloads distant ones.
    /// </summary>
    public void UpdateLoadedChunks(Vector2I cameraChunkXZ, int radius)
    {
        // Process any pending loads from the queue (budgeted per frame)
        ProcessLoadQueue();

        // Only recalculate desired chunks when camera moves to a new chunk
        if (_lastCameraChunkXZ.HasValue && _lastCameraChunkXZ.Value == cameraChunkXZ)
            return;

        _lastCameraChunkXZ = cameraChunkXZ;

        // Queue chunks that need loading
        for (int x = cameraChunkXZ.X - radius; x <= cameraChunkXZ.X + radius; x++)
        for (int z = cameraChunkXZ.Y - radius; z <= cameraChunkXZ.Y + radius; z++)
        for (int y = 0; y < _yChunkLayers; y++)
        {
            var coord = new Vector3I(x, y, z);
            if (!_chunks.ContainsKey(coord))
                _loadQueue.Enqueue(coord);
        }

        // Unload chunks outside radius + 2 (hysteresis to prevent thrashing)
        int unloadDist = radius + 2;
        var toUnload = new List<Vector3I>();
        foreach (var coord in _chunks.Keys)
        {
            int dx = Mathf.Abs(coord.X - cameraChunkXZ.X);
            int dz = Mathf.Abs(coord.Z - cameraChunkXZ.Y);
            if (dx > unloadDist || dz > unloadDist)
                toUnload.Add(coord);
        }

        foreach (var coord in toUnload)
            UnloadChunk(coord);

        // Cancel in-flight background generation for chunks that are now out of range
        var toCancel = new List<Vector3I>();
        foreach (var coord in _generating)
        {
            int dx = Mathf.Abs(coord.X - cameraChunkXZ.X);
            int dz = Mathf.Abs(coord.Z - cameraChunkXZ.Y);
            if (dx > unloadDist || dz > unloadDist)
                toCancel.Add(coord);
        }
        foreach (var coord in toCancel)
        {
            _generating.Remove(coord);
            _unloadedWhileGenerating.Add(coord);
        }

        int totalUnloaded = toUnload.Count + toCancel.Count;
        if (totalUnloaded > 0)
            GD.Print($"Unloaded {toUnload.Count} chunks, cancelled {toCancel.Count} generating");
    }

    private void ProcessLoadQueue()
    {
        // Phase 1: Apply completed background terrain generation results.
        // Only sets block data — no mesh generation here (deferred to Phase 2).
        int applied = 0;
        while (_genResults.TryDequeue(out var result))
        {
            _generating.Remove(result.Coord);

            // Skip if chunk was unloaded while generating (camera moved away)
            if (_unloadedWhileGenerating.Remove(result.Coord)) continue;
            if (_chunks.ContainsKey(result.Coord)) continue; // Already loaded

            var chunk = new Chunk();
            AddChild(chunk);
            if (Engine.IsEditorHint())
                chunk.Owner = GetTree().EditedSceneRoot;
            chunk.Initialize(result.Coord);
            chunk.SetBlockData(result.Blocks);
            _chunks[result.Coord] = chunk;
            applied++;

            // Queue this chunk + its 6 neighbors for mesh generation.
            // Neighbors need re-meshing for correct cross-chunk face culling.
            QueueMeshGeneration(result.Coord);
            QueueMeshGeneration(result.Coord + new Vector3I(1, 0, 0));
            QueueMeshGeneration(result.Coord + new Vector3I(-1, 0, 0));
            QueueMeshGeneration(result.Coord + new Vector3I(0, 1, 0));
            QueueMeshGeneration(result.Coord + new Vector3I(0, -1, 0));
            QueueMeshGeneration(result.Coord + new Vector3I(0, 0, 1));
            QueueMeshGeneration(result.Coord + new Vector3I(0, 0, -1));
        }

        if (applied > 0)
            GD.Print($"Applied {applied} terrain results ({_loadQueue.Count} queued, {_generating.Count} generating, {_meshQueue.Count} mesh pending)");

        // Phase 2: Budgeted mesh generation on main thread (correct neighbor data).
        // Greedy meshing is expensive — spread over frames to avoid stutter.
        int meshed = 0;
        while (_meshQueue.Count > 0 && meshed < MaxMeshPerFrame)
        {
            var coord = _meshQueue.Dequeue();
            _meshQueueSet.Remove(coord);
            if (_chunks.TryGetValue(coord, out var chunk))
            {
                chunk.GenerateMesh(MakeNeighborCallback(coord));
                meshed++;
            }
        }

        // Phase 3: Dispatch new chunks to background threads for terrain generation.
        // Only terrain blocks are computed off-thread; mesh gen stays on main thread.
        if (_loadQueue.Count == 0) return;

        int budget = _loadQueue.Count > 100 ? MaxConcurrentGens * 4 : MaxConcurrentGens;

        while (_loadQueue.Count > 0 && _generating.Count < budget)
        {
            var coord = _loadQueue.Dequeue();
            if (_chunks.ContainsKey(coord) || _generating.Contains(coord)) continue;

            // Check modified chunk cache first (must be done on main thread)
            if (_modifiedChunkCache.TryGetValue(coord, out var cachedBlocks))
            {
                _modifiedChunkCache.Remove(coord);
                var chunk = new Chunk();
                AddChild(chunk);
                if (Engine.IsEditorHint())
                    chunk.Owner = GetTree().EditedSceneRoot;
                chunk.Initialize(coord);
                chunk.SetBlockData(cachedBlocks);
                _chunks[coord] = chunk;
                QueueMeshGeneration(coord);
                GD.Print($"Restored modified chunk {coord} from cache");
                continue;
            }

            _generating.Add(coord);

            // Capture for closure
            var genCoord = coord;
            var terrainGen = _terrainGenerator;

            Task.Run(() =>
            {
                var blocks = new BlockType[Chunk.SIZE, Chunk.SIZE, Chunk.SIZE];
                terrainGen.GenerateChunkBlocks(blocks, genCoord);

                bool isEmpty = true;
                for (int x = 0; x < Chunk.SIZE && isEmpty; x++)
                for (int y = 0; y < Chunk.SIZE && isEmpty; y++)
                for (int z = 0; z < Chunk.SIZE && isEmpty; z++)
                {
                    if (blocks[x, y, z] != BlockType.Air)
                        isEmpty = false;
                }

                _genResults.Enqueue(new ChunkGenResult
                {
                    Coord = genCoord,
                    Blocks = blocks,
                    IsEmpty = isEmpty,
                });
            });
        }
    }

    /// <summary>
    /// Queue a chunk for mesh (re)generation if it exists and isn't already queued.
    /// </summary>
    private void QueueMeshGeneration(Vector3I coord)
    {
        if (_chunks.ContainsKey(coord) && _meshQueueSet.Add(coord))
            _meshQueue.Enqueue(coord);
    }

    private void UnloadChunk(Vector3I coord)
    {
        if (!_chunks.TryGetValue(coord, out var chunk)) return;

        if (chunk.IsDirty)
        {
            _modifiedChunkCache[coord] = chunk.GetBlockData();
            GD.Print($"Cached modified chunk {coord} ({_modifiedChunkCache.Count} cached total)");
        }

        _chunks.Remove(coord);
        _meshQueueSet.Remove(coord);  // Remove stale mesh queue entry
        RemoveChild(chunk);
        chunk.QueueFree();
    }

    public void RegenerateChunkMesh(Vector3I chunkCoord)
    {
        if (_chunks.TryGetValue(chunkCoord, out var chunk))
            chunk.GenerateMesh(MakeNeighborCallback(chunkCoord));
    }

    private void LoadChunk(Vector3I chunkCoord)
    {
        var chunk = new Chunk();
        AddChild(chunk);
        if (Engine.IsEditorHint())
            chunk.Owner = GetTree().EditedSceneRoot;
        chunk.Initialize(chunkCoord);
        _chunks[chunkCoord] = chunk;

        // Restore from cache if this chunk was previously modified, otherwise generate from noise
        if (_modifiedChunkCache.TryGetValue(chunkCoord, out var cachedBlocks))
        {
            chunk.SetBlockData(cachedBlocks);
            _modifiedChunkCache.Remove(chunkCoord);
            GD.Print($"Restored modified chunk {chunkCoord} from cache");
        }
        else
        {
            FillChunkTerrain(chunk, chunkCoord);
        }
    }

    private void FillChunkTerrain(Chunk chunk, Vector3I chunkCoord)
    {
        var blocks = new BlockType[Chunk.SIZE, Chunk.SIZE, Chunk.SIZE];
        _terrainGenerator.GenerateChunkBlocks(blocks, chunkCoord);
        chunk.SetBlockData(blocks);
    }

    private void RegenerateAllMeshes()
    {
        int meshCount = 0;
        int skippedCount = 0;
        foreach (var (coord, chunk) in _chunks)
        {
            chunk.GenerateMesh(MakeNeighborCallback(coord));
            if (chunk.IsEmpty())
                skippedCount++;
            else
                meshCount++;
        }

        GD.Print($"Meshes generated: {meshCount} active, {skippedCount} empty (skipped)");
    }

    /// <summary>
    /// Creates a callback for ChunkMeshGenerator that resolves out-of-bounds
    /// local coordinates by converting to world coords and querying the world.
    /// MUST be called on the main thread (reads _chunks dictionary).
    /// </summary>
    private Func<int, int, int, BlockType> MakeNeighborCallback(Vector3I chunkCoord)
    {
        return (int lx, int ly, int lz) =>
        {
            var worldBlock = new Vector3I(
                chunkCoord.X * Chunk.SIZE + lx,
                chunkCoord.Y * Chunk.SIZE + ly,
                chunkCoord.Z * Chunk.SIZE + lz
            );
            return GetBlock(worldBlock);
        };
    }

    /// <summary>
    /// Floor division that rounds toward negative infinity (not toward zero).
    /// </summary>
    private static int FloorDiv(int a, int b)
    {
        return a >= 0 ? a / b : (a - b + 1) / b;
    }
}
