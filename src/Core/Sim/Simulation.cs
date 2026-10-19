namespace ColonySim.Core;

/// <summary>
/// The colony's fixed-tick simulation over a map: navigation, path requests and the colonists. Each
/// <see cref="Tick"/> brings navigation up to date with the world's edits, answers path requests within a node
/// budget, then steps every colonist. Main thread only, like the world edits it reacts to.
/// </summary>
public sealed class Simulation
{
    /// <summary>Path search nodes expanded per tick, over all colonists' requests.</summary>
    public const int PathBudget = 15000;

    readonly ColonistMover _mover;
    readonly Random _rng;
    int _navVersion;

    public Simulation(ColonyMap map)
    {
        Map = map;
        Nav = map.Nav; // builds navigation if it isn't yet (up to half a second on Large): at load, never mid-game
        Paths = new PathService(Nav);
        _mover = new ColonistMover(this);
        _rng = new Random((int)Hash.Of(map.Settings.Seed, 42, 0, 0));
        SyncNav();
    }

    public ColonyMap Map { get; }
    public Colony Colony => Map.Colony;
    public Navigation Nav { get; }
    public PathService Paths { get; }

    /// <summary>The connectivity component of home (<see cref="Colony.Home"/>), or -1 when nothing near it is standable. Refreshed when navigation changes.</summary>
    public int HomeComponent { get; private set; }

    /// <summary>One simulation step: navigation, then paths, then each colonist (moving, then working), then the clock.</summary>
    public void Tick()
    {
        SyncNav();
        Paths.Process(PathBudget);
        foreach (var c in Colony.Colonists)
        {
            _mover.Step(c);
            // The worker (choosing and doing jobs) steps here.
        }
        Colony.Tick++;
    }

    /// <summary>A new colonist on the standable cell nearest <paramref name="near"/> (within <see cref="ColonistFactory.SpawnRadius"/>), for debugging and tests.</summary>
    public Colonist Spawn(Int3 near)
    {
        SyncNav();
        if (!Nav.Rules.FindStandableNear(near, ColonistFactory.SpawnRadius, out var cell))
            throw new InvalidOperationException($"Nowhere to stand near {near}.");
        var c = ColonistFactory.Create(Colony, _rng);
        c.Cell = cell;
        c.Stuck = IsStuck(cell);
        return c;
    }

    /// <summary>Sends the colonist walking to <paramref name="target"/>, dropping any walk it was on.</summary>
    internal void GoTo(Colonist c, Int3 target) => _mover.GoTo(c, target);

    /// <summary>A colonist standing at <paramref name="cell"/> can't reach home.</summary>
    internal bool IsStuck(Int3 cell) => Nav.ComponentOf(cell) != HomeComponent;

    /// <summary>Applies pending world edits to navigation; when that changed it, re-derives home and every colonist's stuck flag.</summary>
    void SyncNav()
    {
        Nav.Update();
        if (Nav.Version == _navVersion) return;
        _navVersion = Nav.Version;
        HomeComponent = Nav.HomeComponent(Colony.Home);
        foreach (var c in Colony.Colonists) c.Stuck = IsStuck(c.Cell);
    }
}
