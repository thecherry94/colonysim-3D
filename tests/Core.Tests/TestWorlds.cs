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
}
