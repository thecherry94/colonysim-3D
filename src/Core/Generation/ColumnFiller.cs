namespace ColonySim.Core;

/// <summary>Fills terrain columns (ground layers and water) from a height field. Stateless, so chunk columns may be filled in parallel.</summary>
public static class ColumnFiller
{
    public static void FillChunkColumn(VoxelWorld world, TerrainLayers layers, MapSettings s, int[] heights, int chunkX, int chunkZ)
    {
        int waterLevel = s.Terrain.WaterLevel;
        int x0 = chunkX * VoxelWorld.ChunkSize, z0 = chunkZ * VoxelWorld.ChunkSize;
        for (int z = z0; z < z0 + VoxelWorld.ChunkSize; z++)
        for (int x = x0; x < x0 + VoxelWorld.ChunkSize; x++)
        {
            int h = heights[x + z * s.SizeX];
            world.SetSurfaceHeight(x, z, h);
            for (int y = 0; y <= h; y++)
                world.SetBlock(x, y, z, layers.BaseMaterial(x, y, z, h), markDirty: false);
            for (int y = h + 1; y <= waterLevel; y++)
                world.SetBlock(x, y, z, Blocks.Water, markDirty: false);
        }
    }
}
