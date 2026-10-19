namespace ColonySim.Core;

/// <summary>Everything that makes up one colony's map: settings, voxels, player view and pending jobs.</summary>
public sealed class ColonyMap
{
    public ColonyMap(MapSettings settings, VoxelWorld world, JobQueue? jobs = null)
    {
        Settings = settings;
        World = world;
        Layers = new TerrainLayers(settings);
        View = new PlayerView(world, Layers);
        Jobs = jobs ?? new JobQueue();
        Jobs.ChunkCounts = new Int3((world.SizeX + 31) / 32, (world.SizeY + 31) / 32, (world.SizeZ + 31) / 32);
    }

    public MapSettings Settings { get; }
    public VoxelWorld World { get; }
    public TerrainLayers Layers { get; }
    public PlayerView View { get; }
    public JobQueue Jobs { get; }
}
