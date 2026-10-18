namespace ColonySim.Core;

/// <summary>The world as the player knows it: undiscovered air/water is shown as the ground material it hides inside.</summary>
public sealed class PlayerView(VoxelWorld world, TerrainLayers layers)
{
    public VoxelWorld World { get; } = world;

    /// <summary>Callers toggling this must follow with <see cref="VoxelWorld.MarkAllDirty"/>.</summary>
    public bool FogEnabled { get; set; } = true;

    public ushort GetVisibleBlock(int x, int y, int z)
    {
        if (!World.InBounds(x, y, z)) return Blocks.Air;
        ushort id = World.GetBlock(x, y, z);
        if (FogEnabled && !Blocks.Registry.Get(id).IsSolid && !World.IsDiscovered(x, y, z))
            return layers.BaseMaterial(x, y, z, World.GetSurfaceHeight(x, z));
        return id;
    }
}
