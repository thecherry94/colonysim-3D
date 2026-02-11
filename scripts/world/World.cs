namespace ColonySim;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Manages chunks in a Dictionary keyed by chunk coordinate.
/// Provides world-space block access and coordinate conversion.
/// </summary>
public partial class World : Node3D
{
    private readonly Dictionary<Vector3I, Chunk> _chunks = new();

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
        GD.Print($"SetBlock: world ({worldBlock}) -> chunk ({chunkCoord}) local ({local}) = {type}");
    }

    /// <summary>
    /// Load a grid of chunks centered at the given chunk coordinate.
    /// Only loads Y=0 layer for now (single vertical layer).
    /// </summary>
    public void LoadChunkArea(Vector3I center, int radius)
    {
        for (int x = center.X - radius; x <= center.X + radius; x++)
        for (int z = center.Z - radius; z <= center.Z + radius; z++)
        {
            var coord = new Vector3I(x, 0, z);
            if (_chunks.ContainsKey(coord)) continue;
            LoadChunk(coord);
        }

        GD.Print($"LoadChunkArea: center=({center}), radius={radius}, total chunks={_chunks.Count}");

        // After all chunks loaded, regenerate all meshes for cross-chunk face culling
        RegenerateAllMeshes();
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
        chunk.Initialize(chunkCoord);
        _chunks[chunkCoord] = chunk;

        FillChunkTerrain(chunk, chunkCoord);

        GD.Print($"Loaded chunk at ({chunkCoord.X}, {chunkCoord.Y}, {chunkCoord.Z}): {chunk.CountSolidBlocks()} solid blocks");
    }

    /// <summary>
    /// Simple test terrain: sin/cos height variation with grass/dirt/stone layers.
    /// Uses world coordinates so terrain is seamless across chunk boundaries.
    /// </summary>
    private void FillChunkTerrain(Chunk chunk, Vector3I chunkCoord)
    {
        if (chunkCoord.Y != 0) return;

        var blocks = new BlockType[Chunk.SIZE, Chunk.SIZE, Chunk.SIZE];
        for (int lx = 0; lx < Chunk.SIZE; lx++)
        for (int lz = 0; lz < Chunk.SIZE; lz++)
        {
            int worldX = chunkCoord.X * Chunk.SIZE + lx;
            int worldZ = chunkCoord.Z * Chunk.SIZE + lz;

            // Gentle rolling terrain: base height 4, varies +/- 2
            int height = 4 + (int)(Mathf.Sin(worldX * 0.3f) + Mathf.Cos(worldZ * 0.3f));
            height = Mathf.Clamp(height, 1, Chunk.SIZE - 1);

            for (int ly = 0; ly <= height && ly < Chunk.SIZE; ly++)
            {
                if (ly == height)
                    blocks[lx, ly, lz] = BlockType.Grass;
                else if (ly >= height - 2)
                    blocks[lx, ly, lz] = BlockType.Dirt;
                else
                    blocks[lx, ly, lz] = BlockType.Stone;
            }
        }
        chunk.SetBlockData(blocks);
    }

    private void RegenerateAllMeshes()
    {
        foreach (var (coord, chunk) in _chunks)
            chunk.GenerateMesh(MakeNeighborCallback(coord));

        GD.Print($"Regenerated meshes for {_chunks.Count} chunks");
    }

    /// <summary>
    /// Creates a callback for ChunkMeshGenerator that resolves out-of-bounds
    /// local coordinates by converting to world coords and querying the world.
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
