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
/// mark is made at version 0 with the tick: where they are isn't something navigation versions track, so the job is
/// looked at again after <see cref="SealRecheckTicks"/>, and taken once they are out.</item>
/// <item>Jobs other colonists are in the way of are held back the same way: a dig someone stands on (or is stepping
/// onto), and a job whose only spots would put the colonist in the way of a job someone else has claimed (its body in
/// a build's cell, or its feet on a block being dug); such spots are passed over. So no two colonists ever end up each
/// waiting for the other to move.</item>
/// </list>
/// What bounds a search when nothing valid is near:
/// <list type="bullet">
/// <item>A search checks at most <see cref="MaxChecks"/> candidates (a strand check counts
/// <see cref="StrandCheckCost"/>); the colonist's next search of that work type goes on from where it stopped.</item>
/// <item>A pass over a work type that finds nothing, begun and ended at the same navigation and ready versions
/// (<see cref="JobQueue.ReadyVersion"/>), is remembered for its component: other searches from there skip the type
/// until either version changes, or a job held back for sealing others in comes due.</item>
/// </list>
/// Jobs met on the way that have nothing left to do are cleared (<see cref="WorkRules.NeedsWork"/>). Scratch space and
/// these memos are kept between searches: one finder per simulation, main thread only.
/// </summary>
internal sealed class JobFinder
{
    /// <summary>How far above a spot is looked at to call it open to the sky.</summary>
    const int SkyCheck = 8;

    /// <summary>Candidate checks per search at most.</summary>
    public const int MaxChecks = 256;

    /// <summary>What a strand check (possibly a navigation preview) counts against <see cref="MaxChecks"/>.</summary>
    public const int StrandCheckCost = 64;

    /// <summary>Ticks before a job held back for other colonists (it would seal them in, or they are in the way) is looked at again.</summary>
    public const int SealRecheckTicks = 60;

    /// <summary>Where a colonist's pass over a work type has got to: everything before (ring, d2, id) from <see cref="At"/> was checked and isn't valid.</summary>
    readonly record struct Cursor(Int3 At, int Ring, int D2, long Id, int NavVersion, int ReadyVersion);

    /// <summary>Nothing to find, at a ready version, until a tick (when a held-back job comes due).</summary>
    readonly record struct Nothing(int ReadyVersion, long Until);

    readonly List<Job> _ring = new();
    readonly PriorityQueue<Job, (int D2, long Id)> _candidates = new();
    readonly List<(Job, (int, long))> _gathered = new();
    readonly HashSet<Job> _moot = new();
    readonly HashSet<Job> _heldBack = new();
    readonly int[] _typeOrder = new int[WorkTypes.All.Count];
    readonly Dictionary<(int Type, int Component), Nothing> _nothing = new();
    readonly Dictionary<(int Colonist, int Type), Cursor> _cursors = new();
    int _nothingNavVersion, _checks;
    long _dueAt;

    /// <summary>The candidate checks the last search made (tests).</summary>
    internal int LastChecks { get; private set; }

    public bool TryFind(Simulation sim, Colonist c, out Job job, out Int3 workSpot)
    {
        sim.SyncNav();
        job = null!;
        workSpot = default;
        Int3 at = c.Motion?.To ?? c.Cell;
        int component = sim.Nav.ComponentOf(at);
        if (component < 0) return false;
        var jobs = sim.Map.Jobs;
        long tick = sim.Colony.Tick;
        if (_nothingNavVersion != sim.Nav.Version)
        {
            _nothing.Clear();
            _nothingNavVersion = sim.Nav.Version;
        }
        _checks = 0;
        try
        {
            for (int priority = 1; priority <= 4; priority++)
                foreach (int ti in TypesAt(c, priority))
                {
                    if (_nothing.TryGetValue((ti, component), out var none) && none.ReadyVersion == jobs.ReadyVersion && tick < none.Until)
                        continue;
                    if (!_cursors.TryGetValue((c.Id, ti), out var cursor) || cursor.At != at)
                        cursor = new Cursor(at, 0, -1, -1, sim.Nav.Version, jobs.ReadyVersion);
                    _dueAt = long.MaxValue;
                    while (true)
                    {
                        var candidate = Nearest(sim, at, component, WorkTypes.All[ti], ref cursor, out bool capped);
                        if (capped)
                        {
                            _cursors[(c.Id, ti)] = cursor;
                            return false; // the next search goes on from here
                        }
                        if (candidate == null) break;
                        var result = SpotResult.HeldBack;
                        workSpot = default;
                        if (!(candidate.Kind == JobKind.Dig && WorkCompletion.IsInTheWay(sim.Map, candidate)))
                        {
                            _checks += StrandCheckCost;
                            result = TrySpots(sim, c, at, component, candidate, out workSpot);
                        }
                        switch (result)
                        {
                            case SpotResult.Found:
                                _cursors.Remove((c.Id, ti));
                                job = candidate;
                                return true;
                            case SpotResult.HeldBack:
                                _heldBack.Add(candidate);
                                jobs.SetStatus(candidate, JobStatus.NeedsWayBack, 0, tick);
                                _dueAt = Math.Min(_dueAt, tick + SealRecheckTicks);
                                break;
                            default:
                                jobs.SetStatus(candidate, JobStatus.NeedsWayBack, sim.Nav.Version);
                                break;
                        }
                    }
                    _cursors.Remove((c.Id, ti));
                    if (cursor.NavVersion == sim.Nav.Version && cursor.ReadyVersion == jobs.ReadyVersion)
                        _nothing[(ti, component)] = new Nothing(jobs.ReadyVersion, _dueAt);
                }
            return false;
        }
        finally
        {
            LastChecks = _checks;
            _heldBack.Clear();
            // Cleared only now: their edits would change navigation in the middle of the search.
            foreach (var moot in _moot)
                if (jobs.TryGetById(moot.Id, out _)) WorkRules.DropMoot(sim.Map, moot);
            _moot.Clear();
        }
    }

    /// <summary>The indices (in <see cref="WorkTypes.All"/>) of the work types the colonist has at this priority, best skill first.</summary>
    IEnumerable<int> TypesAt(Colonist c, int priority)
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
        for (int i = 0; i < n; i++) yield return _typeOrder[i];
    }

    static int Level(Colonist c, int typeIndex) =>
        WorkTypes.All[typeIndex].Skill is { } s ? c.Skills[(int)s].Level : 0;

    /// <summary>
    /// The nearest valid job of the type in the first ring that has one and the ring after, or null; starting after the
    /// cursor, which it moves past every candidate found invalid before a valid one. Out of checks before finding one,
    /// it returns null with <paramref name="capped"/> set.
    /// </summary>
    Job? Nearest(Simulation sim, Int3 at, int component, WorkType type, ref Cursor cursor, out bool capped)
    {
        capped = false;
        var jobs = sim.Map.Jobs;
        int version = sim.Nav.Version;
        long tick = sim.Colony.Tick;
        bool home = component == sim.HomeComponent;
        Job? best = null;
        int ready = 0;
        foreach (var kind in type.Kinds) ready += jobs.ReadyOfKind(kind);
        int bestD2 = int.MaxValue, lastRing = ready > 0 ? jobs.MaxRing : -1;
        Span<Int3> spots = stackalloc Int3[WorkRules.MaxWorkSpots];
        for (int r = cursor.Ring; r <= lastRing; r++)
        {
            _ring.Clear();
            jobs.ReadyInRing(at, r, type.Kinds, _ring, bestD2); // the ring after a find: only chunks that may hold a nearer job
            _gathered.Clear();
            foreach (var j in _ring)
            {
                Int3 d = j.Position - at;
                int d2 = d.X * d.X + d.Y * d.Y + d.Z * d.Z;
                if (d2 >= bestD2 || j.ClaimedBy != 0) continue;
                if (r == cursor.Ring && (d2, j.Id).CompareTo((cursor.D2, cursor.Id)) <= 0) continue; // checked before
                if (j.Status != JobStatus.Open)
                {
                    if (j.StatusVersion == version) continue;
                    if (j.StatusVersion == 0 && tick < j.StatusTick + SealRecheckTicks)
                    {
                        _dueAt = Math.Min(_dueAt, j.StatusTick + SealRecheckTicks);
                        continue;
                    }
                }
                if (_moot.Contains(j) || _heldBack.Contains(j)) continue;
                _gathered.Add((j, (d2, j.Id)));
            }
            // Nearest first: a heap, as usually the first one or two are taken and the rest never looked at.
            _candidates.Clear();
            _candidates.EnqueueRange(_gathered);
            while (_candidates.TryDequeue(out var j, out var key))
            {
                if (_checks >= MaxChecks)
                {
                    capped = best == null;
                    return best;
                }
                _checks++;
                if (!WorkRules.NeedsWork(sim.Map, j)) _moot.Add(j);
                else if (HasSpotIn(sim.Nav, j, component, spots))
                {
                    best = j;
                    bestD2 = key.D2;
                    lastRing = Math.Min(lastRing, r + 1);
                    break; // the rest of this ring is farther
                }
                else if (home) jobs.SetStatus(j, JobStatus.Unreachable, version);
                if (best == null) cursor = cursor with { Ring = r, D2 = key.D2, Id = key.Id };
            }
            if (best == null) cursor = cursor with { Ring = r + 1, D2 = -1, Id = -1 };
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

    /// <summary>
    /// How trying a job's spots went. <see cref="SpotResult.HeldBack"/>: other colonists keep it from being done now
    /// (it would seal them in, or they are in the way), so it is looked at again after <see cref="SealRecheckTicks"/>.
    /// </summary>
    enum SpotResult { Found, NoWayBack, HeldBack }

    /// <summary>
    /// The first of the job's reachable spots, in spot order, that the strand check allows, passing over those in the
    /// way of someone else's claimed job (<see cref="InTheWayOfOthers"/>).
    /// </summary>
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
        bool passedOver = false;
        for (int i = 0; i < m; i++)
        {
            if (InTheWayOfOthers(sim.Map.Jobs, c, spots[i].Spot))
            {
                passedOver = true;
                continue;
            }
            if (edit.LeavesWayBack(c, spots[i].Spot))
            {
                if (edit.SealsOthers(c)) return SpotResult.HeldBack;
                workSpot = spots[i].Spot;
                return SpotResult.Found;
            }
        }
        return passedOver ? SpotResult.HeldBack : SpotResult.NoWayBack;
    }

    /// <summary>
    /// Working from the spot would put the colonist in the way of a job another colonist has claimed (and may be
    /// waiting at): its body in a build's cell, or its feet on a block being dug.
    /// </summary>
    static bool InTheWayOfOthers(JobQueue jobs, Colonist c, Int3 spot)
    {
        return Claimed(spot, build: true) || Claimed(spot + Int3.Up, build: true) || Claimed(spot + Int3.Down, build: false);

        bool Claimed(Int3 cell, bool build) =>
            jobs.TryGet(cell, out var j) && j.ClaimedBy != 0 && j.ClaimedBy != c.Id
            && (build ? WorkRules.IsBuild(j.Kind) : j.Kind == JobKind.Dig);
    }

    /// <summary>No solid block (as the colony knows it) within <see cref="SkyCheck"/> above the cell.</summary>
    static bool OpenToSky(ColonyMap map, Int3 cell)
    {
        for (int k = 1; k <= SkyCheck; k++)
            if (Blocks.Registry.Get(map.View.GetKnownBlock(cell.X, cell.Y + k, cell.Z)).IsSolid) return false;
        return true;
    }
}
