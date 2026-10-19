using System.Diagnostics;

namespace ColonySim.Core;

/// <summary>
/// Chunked voxel storage with discovery bits, surface heights and dirty-chunk tracking.
/// <para>
/// Threading contract: mutating the world (<see cref="SetBlock"/>, <see cref="SetDiscovered"/>,
/// <see cref="SetSurfaceHeight"/>), <see cref="ConsumeDirtyChunks"/> and <see cref="MarkAllDirty"/> belong to the
/// main thread. Reads (<see cref="GetBlock"/>, <see cref="IsDiscovered"/>, <see cref="GetSurfaceHeight"/>,
/// <see cref="IsChunkUniform"/>) are safe from any thread concurrently with each other and with main-thread writes
/// (a reader racing a write may see either value; the renderer re-meshes the dirtied chunk afterwards). The one
/// exception is bulk building, e.g. generation: writes with <c>markDirty: false</c> to <em>distinct chunks</em> may run
/// in parallel, since chunks share no mutable state and the dirty set is not touched.
/// </para>
/// </summary>
public sealed class VoxelWorld
{
    public const int ChunkSize = 32;
    public const int Height = 256;

    readonly Chunk[] _chunks;
    readonly int[] _surface;
    readonly HashSet<Int3> _dirty = new();

    public int SizeX { get; }
    public int SizeY => Height;
    public int SizeZ { get; }
    public int ChunksX { get; }
    public int ChunksY => Height / ChunkSize;
    public int ChunksZ { get; }

    /// <summary>
    /// Increments on every tracked change (a block or discovery write with <c>markDirty</c>): the main-thread edits a
    /// save would need to keep. Untracked bulk writes and <see cref="MarkAllDirty"/> leave it alone.
    /// </summary>
    public int EditVersion { get; private set; }

    public VoxelWorld(int sizeX, int sizeZ)
    {
        if (sizeX <= 0 || sizeX % ChunkSize != 0) throw new ArgumentException("sizeX must be a positive multiple of 32", nameof(sizeX));
        if (sizeZ <= 0 || sizeZ % ChunkSize != 0) throw new ArgumentException("sizeZ must be a positive multiple of 32", nameof(sizeZ));
        SizeX = sizeX; SizeZ = sizeZ;
        ChunksX = sizeX / ChunkSize; ChunksZ = sizeZ / ChunkSize;
        // Chunk objects are allocated up front (cheap, uniform); only their arrays are lazy and per-chunk,
        // so concurrent writes to different chunks share no mutable state.
        _chunks = new Chunk[ChunksX * ChunksY * ChunksZ];
        for (int i = 0; i < _chunks.Length; i++) _chunks[i] = new Chunk();
        _surface = new int[sizeX * sizeZ];
    }

    public static Int3 ChunkOf(int x, int y, int z) => new(x >> 5, y >> 5, z >> 5);

    public bool InBounds(int x, int y, int z) => (uint)x < (uint)SizeX && (uint)y < (uint)Height && (uint)z < (uint)SizeZ;

    int ChunkIndex(int cx, int cy, int cz) => (cy * ChunksZ + cz) * ChunksX + cx;

    Chunk ChunkAt(int x, int y, int z, out int local) {
        local = Chunk.Index(x & 31, y & 31, z & 31);
        return _chunks[ChunkIndex(x >> 5, y >> 5, z >> 5)];
    }

    public ushort GetBlock(int x, int y, int z)
    {
        if (!InBounds(x, y, z)) return Blocks.Air;
        return ChunkAt(x, y, z, out int i).Get(i);
    }

    public void SetBlock(int x, int y, int z, ushort id, bool markDirty = true)
    {
        if (!InBounds(x, y, z)) return;
        ChunkAt(x, y, z, out int i).Set(i, id);
        if (markDirty) MarkDirtyAround(x, y, z);
    }

    public bool IsDiscovered(int x, int y, int z)
    {
        if (!InBounds(x, y, z)) return false;
        return ChunkAt(x, y, z, out int i).IsDiscovered(i);
    }

    /// <summary>Block and discovery bit with one chunk lookup (the fog hot path); the cell must be in bounds.</summary>
    internal ushort GetBlockInBounds(int x, int y, int z, out bool discovered)
    {
        Debug.Assert(InBounds(x, y, z), $"({x}, {y}, {z}) is out of bounds");
        var c = ChunkAt(x, y, z, out int i);
        discovered = c.IsDiscovered(i);
        return c.Get(i);
    }

    public void SetDiscovered(int x, int y, int z, bool markDirty = true)
    {
        if (!InBounds(x, y, z)) return;
        ChunkAt(x, y, z, out int i).SetDiscovered(i);
        if (markDirty) MarkDirtyAround(x, y, z);
    }

    /// <summary>Chunk by chunk coordinates, for bulk persistence (bypasses dirty tracking).</summary>
    internal Chunk ChunkByCoords(int cx, int cy, int cz) => _chunks[ChunkIndex(cx, cy, cz)];

    /// <summary>
    /// The <em>generated</em> surface height of column (x, z): the top of the terrain as generated (the fog material
    /// and the terrain layers are keyed to it). It is not the current top block: digging or building doesn't change it.
    /// </summary>
    public int GetSurfaceHeight(int x, int z) => _surface[z * SizeX + x];

    /// <summary>Sets the generated surface height (generation, loading); see <see cref="GetSurfaceHeight"/>.</summary>
    public void SetSurfaceHeight(int x, int z, int y) => _surface[z * SizeX + x] = y;

    public bool IsChunkUniform(Int3 chunk, out ushort id)
    {
        var c = _chunks[ChunkIndex(chunk.X, chunk.Y, chunk.Z)];
        id = c.UniformId;
        return c.IsUniform;
    }

    /// <summary>Moves the chunks dirtied since the last call into <paramref name="into"/>. Main thread only.</summary>
    public void ConsumeDirtyChunks(ICollection<Int3> into)
    {
        foreach (var c in _dirty) into.Add(c);
        _dirty.Clear();
    }

    /// <summary>Marks every chunk dirty (e.g. after the fog toggle changes what is visible everywhere). Main thread only.</summary>
    public void MarkAllDirty()
    {
        for (int cy = 0; cy < ChunksY; cy++)
            for (int cz = 0; cz < ChunksZ; cz++)
                for (int cx = 0; cx < ChunksX; cx++)
                    _dirty.Add(new Int3(cx, cy, cz));
    }

    // Ambient occlusion reads diagonals, so every chunk touching the 3x3x3 neighborhood is dirtied.
    void MarkDirtyAround(int x, int y, int z)
    {
        EditVersion++;
        int x0 = Math.Max(x - 1, 0) >> 5, x1 = Math.Min(x + 1, SizeX - 1) >> 5;
        int y0 = Math.Max(y - 1, 0) >> 5, y1 = Math.Min(y + 1, Height - 1) >> 5;
        int z0 = Math.Max(z - 1, 0) >> 5, z1 = Math.Min(z + 1, SizeZ - 1) >> 5;
        for (int cy = y0; cy <= y1; cy++)
            for (int cz = z0; cz <= z1; cz++)
                for (int cx = x0; cx <= x1; cx++)
                    _dirty.Add(new Int3(cx, cy, cz));
    }
}
