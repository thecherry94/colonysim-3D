namespace ColonySim.Core.Tests;

public static class TestWorlds
{
    /// <summary>Flat world: bedrock at y=0, <paramref name="fill"/> (0 means Stone) for 1..surface, air above. No dirty chunks.</summary>
    public static VoxelWorld SolidBelow(int size = 64, int surface = 150, ushort fill = 0)
    {
        if (fill == 0) fill = Blocks.Stone;
        var w = new VoxelWorld(size, size);
        for (int z = 0; z < size; z++)
            for (int x = 0; x < size; x++)
            {
                w.SetBlock(x, 0, z, Blocks.Bedrock, markDirty: false);
                for (int y = 1; y <= surface; y++) w.SetBlock(x, y, z, fill, markDirty: false);
                w.SetSurfaceHeight(x, z, surface);
            }
        return w;
    }

    /// <summary>Flat world with initial discovery flood, wrapped in a ColonyMap.</summary>
    public static ColonyMap FlatMap(int size = 64, int surface = 150)
    {
        var world = SolidBelow(size, surface);
        Discovery.InitialFlood(world);
        return new ColonyMap(new MapSettings(1, size, size, new TerrainParams()), world);
    }

    /// <summary>FNV-1a hash over every block id in the world.</summary>
    public static ulong WorldHash(VoxelWorld w)
    {
        ulong h = 14695981039346656037UL;
        for (int y = 0; y < w.SizeY; y++)
            for (int z = 0; z < w.SizeZ; z++)
                for (int x = 0; x < w.SizeX; x++)
                {
                    ushort b = w.GetBlock(x, y, z);
                    h = (h ^ (byte)b) * 1099511628211UL;
                    h = (h ^ (byte)(b >> 8)) * 1099511628211UL;
                }
        return h;
    }
}

/// <summary>Reports synchronously on the calling thread (unlike <see cref="Progress{T}"/>, which posts to a context).</summary>
public sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T v) => report(v);
}
