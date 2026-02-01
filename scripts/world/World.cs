using Godot;
using System.Collections.Generic;

namespace ColonySim.World;

/// <summary>
/// Manages multiple chunks in a voxel world.
/// Handles chunk loading, positioning, and world-space block access.
/// [Tool] attribute enables editor preview.
/// </summary>
[Tool]
public partial class World : Node3D
{
    /// <summary>
    /// Dictionary of loaded chunks, keyed by chunk coordinates.
    /// </summary>
    private Dictionary<Vector3I, Chunk> _loadedChunks = new();

    public override void _Ready()
    {
        // Configure navigation edge connection for chunk boundaries
        // This allows NavigationRegion3D nodes to connect when edges are close enough
        Rid mapRid = GetWorld3D().NavigationMap;
        NavigationServer3D.MapSetEdgeConnectionMargin(mapRid, 1.0f);

        // Load 3x3 chunk area centered at (1, 0, 1)
        LoadChunkArea(new Vector3I(1, 0, 1), radius: 1);
    }

    /// <summary>
    /// Loads a chunk at the specified chunk coordinates.
    /// </summary>
    public void LoadChunk(Vector3I chunkCoord)
    {
        if (_loadedChunks.ContainsKey(chunkCoord))
            return;

        var chunk = new Chunk();
        chunk.Name = $"Chunk_{chunkCoord.X}_{chunkCoord.Y}_{chunkCoord.Z}";
        chunk.Position = ChunkToWorldPosition(chunkCoord);
        AddChild(chunk);
        _loadedChunks[chunkCoord] = chunk;

        // In editor, set owner so chunk appears in scene tree
        if (Engine.IsEditorHint())
        {
            chunk.Owner = GetTree().EditedSceneRoot;
        }

        // Generate terrain for this chunk
        GenerateChunkTerrain(chunk, chunkCoord);

        GD.Print($"Loaded chunk at {chunkCoord}");
    }

    /// <summary>
    /// Unloads a chunk at the specified chunk coordinates.
    /// </summary>
    public void UnloadChunk(Vector3I chunkCoord)
    {
        if (!_loadedChunks.TryGetValue(chunkCoord, out Chunk? chunk))
            return;

        chunk.QueueFree();
        _loadedChunks.Remove(chunkCoord);
    }

    /// <summary>
    /// Loads chunks in a square area around center.
    /// </summary>
    public void LoadChunkArea(Vector3I center, int radius)
    {
        for (int x = -radius; x <= radius; x++)
        {
            for (int z = -radius; z <= radius; z++)
            {
                LoadChunk(center + new Vector3I(x, 0, z));
            }
        }
    }

    /// <summary>
    /// Gets the chunk at the specified chunk coordinates.
    /// </summary>
    public Chunk? GetChunk(Vector3I chunkCoord)
    {
        return _loadedChunks.TryGetValue(chunkCoord, out Chunk? chunk) ? chunk : null;
    }

    /// <summary>
    /// Converts chunk coordinates to world position.
    /// </summary>
    public static Vector3 ChunkToWorldPosition(Vector3I chunkCoord)
    {
        return new Vector3(
            chunkCoord.X * Chunk.SIZE,
            chunkCoord.Y * Chunk.SIZE,
            chunkCoord.Z * Chunk.SIZE
        );
    }

    /// <summary>
    /// Converts world position to chunk coordinates.
    /// </summary>
    public static Vector3I WorldToChunkCoord(Vector3 worldPos)
    {
        return new Vector3I(
            Mathf.FloorToInt(worldPos.X / Chunk.SIZE),
            Mathf.FloorToInt(worldPos.Y / Chunk.SIZE),
            Mathf.FloorToInt(worldPos.Z / Chunk.SIZE)
        );
    }

    /// <summary>
    /// Converts world block position to chunk coordinate and local position within chunk.
    /// </summary>
    public static (Vector3I chunkCoord, Vector3I localPos) WorldToChunkAndLocal(Vector3I worldBlockPos)
    {
        Vector3I chunkCoord = new Vector3I(
            Mathf.FloorToInt((float)worldBlockPos.X / Chunk.SIZE),
            Mathf.FloorToInt((float)worldBlockPos.Y / Chunk.SIZE),
            Mathf.FloorToInt((float)worldBlockPos.Z / Chunk.SIZE)
        );

        // Handle negative coordinates properly with modulo
        Vector3I localPos = new Vector3I(
            ((worldBlockPos.X % Chunk.SIZE) + Chunk.SIZE) % Chunk.SIZE,
            ((worldBlockPos.Y % Chunk.SIZE) + Chunk.SIZE) % Chunk.SIZE,
            ((worldBlockPos.Z % Chunk.SIZE) + Chunk.SIZE) % Chunk.SIZE
        );

        return (chunkCoord, localPos);
    }

    /// <summary>
    /// Gets block type at world block position.
    /// </summary>
    public BlockType GetBlock(Vector3I worldBlockPos)
    {
        var (chunkCoord, localPos) = WorldToChunkAndLocal(worldBlockPos);
        var chunk = GetChunk(chunkCoord);
        if (chunk == null)
            return BlockType.Air;
        return chunk.GetBlock(localPos.X, localPos.Y, localPos.Z);
    }

    /// <summary>
    /// Sets block at world block position and regenerates affected chunk.
    /// </summary>
    public void SetBlock(Vector3I worldBlockPos, BlockType type)
    {
        var (chunkCoord, localPos) = WorldToChunkAndLocal(worldBlockPos);
        var chunk = GetChunk(chunkCoord);
        if (chunk == null)
            return;

        chunk.SetBlock(localPos.X, localPos.Y, localPos.Z, type);
        chunk.ForceRegenerateMesh();
    }

    /// <summary>
    /// Generates simple terrain for a chunk.
    /// </summary>
    private void GenerateChunkTerrain(Chunk chunk, Vector3I chunkCoord)
    {
        // Simple flat terrain: stone, dirt, grass layers
        for (int x = 0; x < Chunk.SIZE; x++)
        {
            for (int z = 0; z < Chunk.SIZE; z++)
            {
                // Layers 0-3: Stone
                for (int y = 0; y < 4; y++)
                    chunk.SetBlock(x, y, z, BlockType.Stone);

                // Layer 4: Dirt
                chunk.SetBlock(x, 4, z, BlockType.Dirt);

                // Layer 5: Grass
                chunk.SetBlock(x, 5, z, BlockType.Grass);
            }
        }

        // Add a hole in the center chunk only (for testing)
        if (chunkCoord == new Vector3I(1, 0, 1))
        {
            chunk.SetBlock(8, 5, 8, BlockType.Air);
            chunk.SetBlock(8, 4, 8, BlockType.Air);
            chunk.SetBlock(8, 3, 8, BlockType.Air);
        }

        // Force mesh regeneration after terrain generation
        chunk.ForceRegenerateMesh();
    }
}
