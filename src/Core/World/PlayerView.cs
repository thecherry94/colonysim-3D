namespace ColonySim.Core;

/// <summary>The world as the player knows it: undiscovered air/water is shown as the ground material it hides inside.</summary>
public sealed class PlayerView(VoxelWorld world, TerrainLayers layers)
{
    static readonly bool[] Solid = Blocks.Registry.All.Select(d => d.IsSolid).ToArray();

    public VoxelWorld World { get; } = world;

    /// <summary>Callers toggling this must follow with <see cref="VoxelWorld.MarkAllDirty"/>.</summary>
    public bool FogEnabled { get; set; } = true;

    /// <summary>What the player sees: <see cref="GetKnownBlock"/>, or the real block while fog is off.</summary>
    public ushort GetVisibleBlock(int x, int y, int z) => FogEnabled ? GetKnownBlock(x, y, z) : World.GetBlock(x, y, z);

    /// <summary>The block as the colony knows it, always fogged whatever <see cref="FogEnabled"/> says. Air out of bounds.</summary>
    public ushort GetKnownBlock(int x, int y, int z)
    {
        if (!World.InBounds(x, y, z)) return Blocks.Air;
        ushort id = World.GetBlockInBounds(x, y, z, out bool discovered);
        if (!discovered && !Solid[id]) return layers.BaseMaterial(x, y, z, World.GetSurfaceHeight(x, z));
        return id;
    }
}
