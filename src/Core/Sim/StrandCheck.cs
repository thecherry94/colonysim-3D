namespace ColonySim.Core;

/// <summary>
/// Whether finishing a job from a work spot leaves everyone a way back, judged on connectivity as it would be after the
/// job's edit (<see cref="Navigation.Preview"/>; a dig leaves air, a build or carve its block):
/// <list type="bullet">
/// <item>The worker, who does the job from the spot, must still connect from there: to home if it is home now (where
/// it came from doesn't matter, so a colonist inside a room can close the door from outside), else to where it is now.</item>
/// <item>A build must also leave every other colonist who is home now still home. One whose body is in the cell is
/// left out: the build waits for it to move, it isn't refused.</item>
/// </list>
/// A preview rebuilds whole chunks, so most edits are first checked locally (<see cref="CutsNothing"/>): one that
/// provably disconnects nothing leaves every component as it is, and needs no preview. Even with a preview, colonists
/// are only looked at when the edit splits a piece off home (see <see cref="Edit.Of"/>).
/// </summary>
internal static class StrandCheck
{
    /// <summary>The edits from more than this far from home can't change which cell home measures from.</summary>
    const int HomeReach = Navigation.HomeRadius + 1;

    /// <summary>How far, and over how many cells, <see cref="CutsNothing"/> looks for the detours.</summary>
    const int DetourReach = 6, DetourCells = 1024;

    /// <summary>How far up and down a cell's moves read blocks (a 3-high climb reads 4 up; its reverse 4 down).</summary>
    const int MoveReadHeight = 4;

    // Scratch, per thread (each simulation runs on one): the ends of what an edit cuts, and the detour search.
    [ThreadStatic] static HashSet<Int3>? t_ends, t_seen;
    [ThreadStatic] static Queue<Int3>? t_queue;

    public static bool Allows(Simulation sim, Colonist worker, Job job, Int3 workSpot) => Edit.Of(sim, job).Allows(worker, workSpot);

    /// <summary>
    /// One job's edit, checked once and then asked about any number of workers and spots (until navigation next
    /// previews or updates).
    /// </summary>
    internal sealed class Edit
    {
        readonly Simulation _sim;
        readonly NavRules<OverlaidBlocks> _edited;
        /// <summary>Connectivity after the edit; null when the edit provably cuts nothing, so components stay as they are.</summary>
        readonly ConnectivityPreview? _after;
        readonly int _homeAfter;
        readonly List<Colonist> _cutOff = new();

        Edit(Simulation sim, NavRules<OverlaidBlocks> edited, ConnectivityPreview? after, int homeAfter)
        {
            _sim = sim;
            _edited = edited;
            _after = after;
            _homeAfter = homeAfter;
        }

        public static Edit Of(Simulation sim, Job job)
        {
            sim.SyncNav();
            Int3 p = job.Position;
            ushort block = WorkRules.ResultBlock(job);
            var edited = new NavRules<OverlaidBlocks>(new OverlaidBlocks(sim.Map.View, p, block));
            Int3 d = p - sim.Colony.Home;
            bool nearHome = Math.Max(Math.Abs(d.X), Math.Max(Math.Abs(d.Y), Math.Abs(d.Z))) <= HomeReach;
            var ends = Ends(sim.Nav, edited, p);
            if (!nearHome && (ends.Count < 2 || Connected(edited, ends, p))) return new Edit(sim, edited, null, sim.HomeComponent);

            // Home is the standable cell nearest the anchor, which an edit near it may move: find it again in the edited world.
            Int3? homeCell = sim.HomeCell;
            if (nearHome) homeCell = edited.FindStandableNear(sim.Colony.Home, Navigation.HomeRadius, out var h) ? h : null;
            var after = sim.Nav.Preview(p, block);
            var edit = new Edit(sim, edited, after, homeCell is { } cell ? after.ComponentOf(cell) : -1);
            if (WorkRules.IsBuild(job.Kind) && sim.HomeComponent >= 0 && SplitsHome(sim, edited, after, edit._homeAfter, ends))
                foreach (var c in sim.Colony.Colonists)
                {
                    Int3 at = At(c);
                    if (p == at || p == at + Int3.Up || p == c.Cell || p == c.Cell + Int3.Up) continue; // waited for
                    if (sim.Nav.ComponentOf(at) == sim.HomeComponent && after.ComponentOf(at) != edit._homeAfter) edit._cutOff.Add(c);
                }
            return edit;
        }

        /// <summary>
        /// Some cell that is home now is not home after the edit. Every such piece holds one of the edit's ends (an old
        /// path from it toward home crosses something the edit cut, and the cut's near end stays with it), or else the
        /// old home cell itself; so when those are all still home, nothing is cut off, and no colonist need be looked at.
        /// </summary>
        static bool SplitsHome(Simulation sim, NavRules<OverlaidBlocks> edited, ConnectivityPreview after, int homeAfter, HashSet<Int3> ends)
        {
            if (sim.HomeCell is { } old && edited.IsStandable(old) && after.ComponentOf(old) != homeAfter) return true;
            foreach (var e in ends)
                if (sim.Nav.ComponentOf(e) == sim.HomeComponent && after.ComponentOf(e) != homeAfter) return true;
            return false;
        }

        public bool Allows(Colonist worker, Int3 spot) => LeavesWayBack(worker, spot) && !SealsOthers(worker);

        /// <summary>The worker, doing the job from the spot, still connects to home (or, away from home, to where it is).</summary>
        public bool LeavesWayBack(Colonist worker, Int3 spot)
        {
            Int3 at = At(worker);
            var nav = _sim.Nav;
            bool home = _sim.HomeComponent >= 0 && nav.ComponentOf(at) == _sim.HomeComponent;
            if (_after == null) // nothing cut: what is connected now stays connected
                return _edited.IsStandable(spot)
                    && (home ? nav.ComponentOf(spot) == _sim.HomeComponent : _edited.IsStandable(at) && nav.ComponentOf(spot) == nav.ComponentOf(at));
            int there = _after.ComponentOf(spot);
            if (there < 0) return false;
            return there == (home ? _homeAfter : _after.ComponentOf(at));
        }

        /// <summary>A build cuts some colonist other than the worker off from home.</summary>
        public bool SealsOthers(Colonist worker)
        {
            foreach (var c in _cutOff)
                if (c != worker) return true;
            return false;
        }

        /// <summary>Where the colonist is, or will be once the move under way ends.</summary>
        static Int3 At(Colonist c) => c.Motion?.To ?? c.Cell;
    }

    /// <summary>
    /// True when the edit (<paramref name="edited"/> at <paramref name="p"/>) provably disconnects nothing: every move it
    /// takes away, and every cell it leaves unstandable, has a short way round in the edited world. A cell's moves read
    /// blocks at most one column across and <see cref="MoveReadHeight"/> up or down, so only the cells that close can
    /// change; moves are symmetric, so a lost move shows in the moves of its ends there. Any old path then still has a
    /// way through: around a lost move between its ends, around a lost cell between the neighbours it had. False means
    /// "not proven" (the detours weren't found nearby), not "cut".
    /// </summary>
    internal static bool CutsNothing(Navigation nav, NavRules<OverlaidBlocks> edited, Int3 p)
    {
        var ends = Ends(nav, edited, p);
        return ends.Count < 2 || Connected(edited, ends, p);
    }

    /// <summary>
    /// The cells that must all still connect for the edit to cut nothing (<see cref="CutsNothing"/>): both ends of every
    /// move it takes away, and the remaining neighbours of every cell it leaves unstandable (all standable after it).
    /// Scratch: valid until the next call on this thread.
    /// </summary>
    static HashSet<Int3> Ends(Navigation nav, NavRules<OverlaidBlocks> edited, Int3 p)
    {
        var before = nav.Rules.Core;
        Span<Move> was = stackalloc Move[NavRules.MaxMoves], now = stackalloc Move[NavRules.MaxMoves];
        var ends = t_ends ??= new HashSet<Int3>();
        ends.Clear();
        for (int dy = -MoveReadHeight; dy <= MoveReadHeight; dy++)
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    Int3 c = p + new Int3(dx, dy, dz);
                    int nw = before.GetMoves(c, was);
                    if (nw == 0) continue;
                    if (!edited.IsStandable(c))
                    {
                        for (int i = 0; i < nw; i++)
                            if (edited.IsStandable(was[i].To)) ends.Add(was[i].To);
                        continue;
                    }
                    var kept = now[..edited.GetMoves(c, now)];
                    for (int i = 0; i < nw; i++)
                    {
                        if (Has(kept, was[i].To)) continue;
                        ends.Add(c);
                        if (edited.IsStandable(was[i].To)) ends.Add(was[i].To);
                    }
                }
        return ends;

        static bool Has(Span<Move> moves, Int3 to)
        {
            foreach (var m in moves)
                if (m.To == to) return true;
            return false;
        }
    }

    /// <summary>The cells all connect in the edited world without leaving <see cref="DetourReach"/> of <paramref name="p"/> (searching at most <see cref="DetourCells"/> cells).</summary>
    static bool Connected(NavRules<OverlaidBlocks> edited, HashSet<Int3> cells, Int3 p)
    {
        Span<Move> moves = stackalloc Move[NavRules.MaxMoves];
        Int3 start = default;
        foreach (var cell in cells)
        {
            start = cell;
            break;
        }
        var seen = t_seen ??= new HashSet<Int3>();
        var queue = t_queue ??= new Queue<Int3>();
        seen.Clear();
        queue.Clear();
        seen.Add(start);
        queue.Enqueue(start);
        int left = cells.Count - 1;
        while (queue.TryDequeue(out var c) && seen.Count < DetourCells)
        {
            int n = edited.GetMoves(c, moves);
            for (int i = 0; i < n; i++)
            {
                Int3 to = moves[i].To, d = to - p;
                if (Math.Max(Math.Abs(d.X), Math.Max(Math.Abs(d.Y), Math.Abs(d.Z))) > DetourReach || !seen.Add(to)) continue;
                if (cells.Contains(to) && --left == 0) return true;
                queue.Enqueue(to);
            }
        }
        return false;
    }
}
