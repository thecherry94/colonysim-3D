namespace ColonySim.Core;

/// <summary>Everything that makes up one colony's map: settings, voxels, player view, pending jobs, the colony and navigation.</summary>
public sealed class ColonyMap
{
    public ColonyMap(MapSettings settings, VoxelWorld world, JobQueue? jobs = null, StartSite site = default, Colony? colony = null)
    {
        Settings = settings;
        StartSite = site;
        World = world;
        Layers = new TerrainLayers(settings);
        View = new PlayerView(world, Layers);
        Jobs = jobs ?? new JobQueue();
        Jobs.ChunkCounts = new Int3((world.SizeX + 31) / 32, (world.SizeY + 31) / 32, (world.SizeZ + 31) / 32);
        Colony = colony ?? new Colony(new Int3(site.X, site.Level, site.Z));
    }

    public MapSettings Settings { get; }

    /// <summary>Where the colonists land; <c>default</c> when the map has none (tests, loaded saves until they store it).</summary>
    public StartSite StartSite { get; }

    public VoxelWorld World { get; }
    public TerrainLayers Layers { get; }
    public PlayerView View { get; }
    public JobQueue Jobs { get; }

    /// <summary>The colonists and the clock: landed by the generator or loaded; without either, an empty colony homed at the start site.</summary>
    public Colony Colony { get; }

    /// <summary>Increments when the player changes the colony (priorities, bookmarks, colonists), not when the simulation moves it; the HUD's unsaved-changes check reads it.</summary>
    public int ColonyVersion { get; private set; }

    public void MarkColonyChanged() => ColonyVersion++;

    Navigation? _nav;

    /// <summary>Sections and connectivity, built on first access (main thread); from then on it tracks <see cref="VoxelWorld.Changed"/>.</summary>
    public Navigation Nav => _nav ??= new Navigation(this);
}
