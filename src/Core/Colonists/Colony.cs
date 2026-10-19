namespace ColonySim.Core;

/// <summary>The colony's people and its clock: everything the simulation keeps besides the map itself.</summary>
public sealed class Colony(Int3 home)
{
    public List<Colonist> Colonists { get; } = new();

    /// <summary>The id the next colonist gets. Starts at 1: id 0 means "none".</summary>
    public int NextColonistId { get; set; } = 1;

    /// <summary>Simulation ticks since the colony landed (<see cref="SimClock.TicksPerSecond"/> per second at 1×).</summary>
    public long Tick { get; set; }

    /// <summary>The start site's standing cell; the home component and the stuck flag measure against it (ruling 8).</summary>
    public Int3 Home { get; } = home;

    public List<ViewBookmark> Bookmarks { get; } = new();

    /// <summary>Where the colonists' bodies are; kept by the simulation's mover.</summary>
    public BodyMap Bodies { get; } = new();
}
