namespace ColonySim.Core;

/// <summary>
/// Chooses a colonist's next job:
/// <list type="number">
/// <item>Priorities 1 to 4; within one, work types by the colonist's skill for them, best first (ties in
/// <see cref="WorkTypes.All"/> order).</item>
/// <item>Within a work type, by chunk ring from the colonist (<see cref="JobQueue.ReadyInRing"/>): the nearest valid job
/// (squared distance) of the first ring that has one and the ring after it.</item>
/// <item>A job is valid when it is of the work type, unclaimed, ready, not marked unreachable or needing a way back at
/// the current navigation version, and has a work spot in the colonist's component. One without such a spot is
/// marked <see cref="JobStatus.Unreachable"/> when the colonist is home (from elsewhere, it may just be out of reach).</item>
/// <item>The chosen job's spots are tried nearest first (for walls, those open to the sky first, so walls go up from
/// outside): the first the <see cref="StrandCheck"/> allows wins. With none, the job is marked
/// <see cref="JobStatus.NeedsWayBack"/> and the search goes on. When only other colonists would be sealed in, the
/// mark is made at version 0, which never matches: where they are isn't something navigation versions track, so the
/// job is looked at again on every search, and taken once they are out.</item>
/// </list>
/// Jobs met on the way that have nothing left to do are cleared (<see cref="WorkRules.NeedsWork"/>). Scratch space is
/// kept between searches: one finder per simulation, main thread only.
/// </summary>
internal sealed class JobFinder
{
    /// <summary>How far above a spot is looked at to call it open to the sky.</summary>
    const int SkyCheck = 8;

    readonly List<Job> _ring = new();
    readonly PriorityQueue<Job, int> _candidates = new();
    readonly List<(Job, int)> _gathered = new();
    readonly HashSet<Job> _moot = new();
    readonly HashSet<Job> _sealing = new();
    readonly int[] _typeOrder = new int[WorkTypes.All.Count];

    public bool TryFind(Simulation sim, Colonist c, out Job job, out Int3 workSpot)
    {
        sim.SyncNav();
        job = null!;
        workSpot = default;
        Int3 at = c.Motion?.To ?? c.Cell;
        int component = sim.Nav.ComponentOf(at);
        if (component < 0) return false;
        try
        {
            for (int priority = 1; priority <= 4; priority++)
                foreach (var type in TypesAt(c, priority))
                    while (Nearest(sim, at, component, type) is { } candidate)
                    {
                        switch (TrySpots(sim, c, at, component, candidate, out workSpot))
                        {
                            case SpotResult.Found:
                                job = candidate;
                                return true;
                            case SpotResult.SealsOthers:
                                _sealing.Add(candidate);
                                sim.Map.Jobs.SetStatus(candidate, JobStatus.NeedsWayBack, 0);
                                break;
                            default:
                                sim.Map.Jobs.SetStatus(candidate, JobStatus.NeedsWayBack, sim.Nav.Version);
                                break;
                        }
                    }
            return false;
        }
        finally
        {
            _sealing.Clear();
            // Cleared only now: their edits would change navigation in the middle of the search.
            foreach (var moot in _moot)
                if (sim.Map.Jobs.TryGetById(moot.Id, out _)) WorkRules.DropMoot(sim.Map, moot);
            _moot.Clear();
        }
    }

    /// <summary>The work types the colonist has at this priority, best skill first.</summary>
    IEnumerable<WorkType> TypesAt(Colonist c, int priority)
    {
        int n = 0;
        for (int i = 0; i < WorkTypes.All.Count; i++)
            if (c.Priorities[i] == priority) _typeOrder[n++] = i;
        // Insertion sort: stable, and there are only a handful.
        for (int i = 1; i < n; i++)
        {
            int t = _typeOrder[i], j = i - 1;
            while (j >= 0 && Level(c, _typeOrder[j]) < Level(c, t))
            {
                _typeOrder[j + 1] = _typeOrder[j];
                j--;
            }
            _typeOrder[j + 1] = t;
        }
        for (int i = 0; i < n; i++) yield return WorkTypes.All[_typeOrder[i]];
    }

    static int Level(Colonist c, int typeIndex) =>
        WorkTypes.All[typeIndex].Skill is { } s ? c.Skills[(int)s].Level : 0;

    /// <summary>The nearest valid job of the type in the first ring that has one and the ring after, or null.</summary>
    Job? Nearest(Simulation sim, Int3 at, int component, WorkType type)
    {
        var jobs = sim.Map.Jobs;
        int version = sim.Nav.Version;
        bool home = component == sim.HomeComponent;
        Job? best = null;
        int ready = 0;
        foreach (var kind in type.Kinds) ready += jobs.ReadyOfKind(kind);
        int bestD2 = int.MaxValue, lastRing = ready > 0 ? jobs.MaxRing : -1;
        Span<Int3> spots = stackalloc Int3[WorkRules.MaxWorkSpots];
        for (int r = 0; r <= lastRing; r++)
        {
            _ring.Clear();
            jobs.ReadyInRing(at, r, type.Kinds, _ring, bestD2); // the ring after a find: only chunks that may hold a nearer job
            _gathered.Clear();
            foreach (var j in _ring)
            {
                Int3 d = j.Position - at;
                int d2 = d.X * d.X + d.Y * d.Y + d.Z * d.Z;
                if (d2 >= bestD2 || j.ClaimedBy != 0) continue;
                if (j.Status != JobStatus.Open && j.StatusVersion == version) continue;
                if (_moot.Contains(j) || _sealing.Contains(j)) continue;
                _gathered.Add((j, d2));
            }
            // Nearest first: a heap, as usually the first one or two are taken and the rest never looked at.
            _candidates.Clear();
            _candidates.EnqueueRange(_gathered);
            while (_candidates.TryDequeue(out var j, out int d2))
            {
                if (!WorkRules.NeedsWork(sim.Map, j))
                {
                    _moot.Add(j);
                    continue;
                }
                if (HasSpotIn(sim.Nav, j, component, spots))
                {
                    best = j;
                    bestD2 = d2;
                    lastRing = Math.Min(lastRing, r + 1);
                    break; // the rest of this ring is farther
                }
                if (home) jobs.SetStatus(j, JobStatus.Unreachable, version);
            }
        }
        if (best is { Status: not JobStatus.Open }) jobs.SetStatus(best, JobStatus.Open, version);
        return best;
    }

    static bool HasSpotIn(Navigation nav, Job job, int component, Span<Int3> spots)
    {
        int n = WorkRules.WorkSpots(nav.Rules, job, spots);
        for (int i = 0; i < n; i++)
            if (nav.ComponentOf(spots[i]) == component) return true;
        return false;
    }

    enum SpotResult { Found, NoWayBack, SealsOthers }

    /// <summary>The first of the job's reachable spots, in spot order, that the strand check allows.</summary>
    static SpotResult TrySpots(Simulation sim, Colonist c, Int3 at, int component, Job job, out Int3 workSpot)
    {
        Span<Int3> all = stackalloc Int3[WorkRules.MaxWorkSpots];
        int n = WorkRules.WorkSpots(sim.Nav.Rules, job, all);
        Span<(int Key, Int3 Spot)> spots = stackalloc (int, Int3)[WorkRules.MaxWorkSpots];
        int m = 0;
        bool wall = job.Kind == JobKind.BuildWall;
        for (int i = 0; i < n; i++)
        {
            if (sim.Nav.ComponentOf(all[i]) != component) continue;
            Int3 d = all[i] - at;
            int key = d.X * d.X + d.Y * d.Y + d.Z * d.Z;
            if (wall && !OpenToSky(sim.Map, all[i])) key += 1 << 20; // after every open one
            spots[m++] = (key, all[i]);
        }
        spots[..m].Sort((a, b) => a.Key.CompareTo(b.Key));
        var edit = StrandCheck.Edit.Of(sim, job);
        workSpot = default;
        for (int i = 0; i < m; i++)
            if (edit.LeavesWayBack(c, spots[i].Spot))
            {
                if (edit.SealsOthers(c)) return SpotResult.SealsOthers;
                workSpot = spots[i].Spot;
                return SpotResult.Found;
            }
        return SpotResult.NoWayBack;
    }

    /// <summary>No solid block (as the colony knows it) within <see cref="SkyCheck"/> above the cell.</summary>
    static bool OpenToSky(ColonyMap map, Int3 cell)
    {
        for (int k = 1; k <= SkyCheck; k++)
            if (Blocks.Registry.Get(map.View.GetKnownBlock(cell.X, cell.Y + k, cell.Z)).IsSolid) return false;
        return true;
    }
}
