namespace ColonySim.Core;

/// <summary>One 32^3 block of voxels. Uniform (single id, no array) until a differing write.</summary>
internal sealed class Chunk
{
    public const int Size = VoxelWorld.ChunkSize;
    public const int Volume = Size * Size * Size;

    ushort[]? _blocks;
    ulong[]? _discovery;

    public bool IsUniform => _blocks == null;
    public ushort UniformId => 0; // uniform chunks are always Air; filled chunks allocate their array

    public static int Index(int lx, int ly, int lz) => (ly * Size + lz) * Size + lx;

    public ushort Get(int index) => _blocks == null ? (ushort)0 : _blocks[index];

    public void Set(int index, ushort id)
    {
        if (_blocks == null)
        {
            if (id == 0) return;
            _blocks = new ushort[Volume];
        }
        _blocks[index] = id;
    }

    public bool IsDiscovered(int index)
    {
        if (_discovery == null) return false;
        return (_discovery[index >> 6] & (1UL << (index & 63))) != 0;
    }

    public void SetDiscovered(int index)
    {
        _discovery ??= new ulong[Volume / 64];
        _discovery[index >> 6] |= 1UL << (index & 63);
    }
}
