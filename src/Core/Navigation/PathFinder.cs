namespace ColonySim.Core;

/// <summary>
/// A* over feet cells, made hierarchical through the navigation sections for long paths, and resumable so that
/// <see cref="PathService"/> can spread one search over many calls.
/// <list type="bullet">
/// <item>Ends that aren't standable, or lie in different components, are answered at once: unreachable.</item>
/// <item>Ends at most <see cref="ShortRange"/> apart (Chebyshev): plain cell A* with an admissible, consistent heuristic
/// (no move costs less than its horizontal octile distance plus half its rise, nor less than its rise), so the path
/// is optimal.</item>
/// <item>Farther, three steps. (1) A* over the sections' links, backwards from the goal, values each link it settles:
/// the estimated cost from its From cell to the goal. A crossing inside a section is estimated by
/// <see cref="Estimate"/>, from the octile distance and the section's landmark costs, so rivers and cliffs inside
/// sections count. (2) The sections with settled links are the corridor, their links its exits. (3) Cell A* runs in the
/// corridor with the cheapest way out through an exit as its heuristic, weighted by <see cref="CorridorWeight"/>; if
/// that fails, unrestricted cell A*. Long paths are near-optimal, not optimal: the weight and the estimates trade a
/// few percent of path cost for far fewer expanded cells.</item>
/// </list>
/// A request gives up as <see cref="PathStatus.TooLong"/> after <see cref="NodeLimit"/> expanded nodes (cells and
/// links), counted over restarts. One search runs at a time, in pooled buffers: a search allocates only its result.
/// <para>
/// Main thread only; the search reads the known world live and the sections as of the last
/// <see cref="Navigation.Update"/>. When <see cref="Navigation.Version"/> changes between <see cref="Advance"/> calls,
/// the request is answered at once if it now needs no search (say the goal was walled off), else the search starts
/// over on the new data; after <see cref="MaxRestarts"/> restarts it finishes on mixed data instead.
/// <see cref="PathRequest.NavVersion"/> is the version the search last started on.
/// </para>
/// </summary>
public sealed class PathFinder
{
    /// <summary>Ends at most this far apart (Chebyshev) are searched cell by cell, without the sections.</summary>
    public const int ShortRange = 32;

    /// <summary>Expanded nodes (cells and links) after which a request is <see cref="PathStatus.TooLong"/>.</summary>
    public const int NodeLimit = 600_000;

    /// <summary>
    /// The corridor heuristic's weight: greedier than A* proper. Measured on generated Small maps, long paths come out
    /// about 12% above optimal on average; 1.5 gives about 10% for 1.7 times the expanded cells.
    /// </summary>
    const float CorridorWeight = 1.75f;

    /// <summary>Landmark bounds are capped at this many times the octile distance (see <see cref="Estimate"/>).</summary>
    const float LandmarkCap = 3f;

    /// <summary>How often a search starts over when the navigation changes under it, before it just finishes.</summary>
    const int MaxRestarts = 3;

    const float DiagonalExtra = 1.4142f - 1f; // a diagonal's cost over a straight step
    const int Landmarks = NavChunk.Landmarks;

    enum Phase { Idle, Links, Corridor, Open }

    enum Outcome { OutOfBudget, Found, Exhausted, TooLong }

    readonly Navigation _nav;
    readonly NavRules<KnownBlocks> _rules;
    readonly VoxelWorld _world;
    readonly Move[] _moves = new Move[NavRules.MaxMoves];

    PathRequest? _current;
    Phase _phase;
    readonly int _nodeLimit;
    bool _hierarchical;
    int _expanded, _restarts;
    int _startVersion; // the Navigation.Version the search (re)started on: what its answer is stamped with
    int _seenVersion; // the last version Advance reacted to

    // ---- Cell search: nodes, a cell → node map (open addressing, cleared by bumping the stamp) and the open heap.
    struct Node
    {
        public Move Via; // the move that reached it; Via.To is the node's cell
        public float G, H;
        public int Parent;
        public bool Closed; // expanded, or outside the corridor
    }

    struct Slot
    {
        public int Stamp, Key, Node;
    }

    Node[] _nodes = new Node[4096];
    int _nodeCount, _goalNode;
    Slot[] _slots = new Slot[1 << 13];
    int _slotBits = 13, _stamp;
    readonly Frontier _open = new();

    // ---- Link search. A link is numbered by its section's base plus its index in SectionLinks; a section gets its base
    // when the search first touches it (stamped). Per link: the stamp of the search that reached/settled it, its value
    // (estimated cost from its From cell to the goal), its section, and, once looked up (stamped), the landmark costs
    // of its From and of its To cell.
    int[] _linkBase = [], _baseStamp = [];
    int _linkCount;
    float[] _linkV = [], _fromMarks = [], _toMarks = [];
    int[] _linkSection = [], _linkSeen = [], _linkDone = [], _fromMarked = [], _toMarked = [];
    readonly List<int> _settled = new();
    readonly Frontier _linkOpen = new();
    int _linkStamp, _startSection, _goalSection;
    float _bestStart;

    // ---- The corridor: per section (valid when _corridor[s] == _linkStamp), its exits, cheapest first.
    int[] _corridor = [], _exitStart = [], _exitCount = [], _exitFill = [], _exitPlaced = [];
    Int3[] _exitCell = new Int3[256];
    float[] _exitV = new float[256], _exitMarks = new float[256 * Landmarks];
    readonly float[] _startMarks = new float[Landmarks], _goalMarks = new float[Landmarks];

    public PathFinder(Navigation nav) : this(nav, NodeLimit) { }

    /// <summary>A finder with a smaller node limit, for tests.</summary>
    internal PathFinder(Navigation nav, int nodeLimit)
    {
        _nodeLimit = nodeLimit;
        _nav = nav;
        _rules = nav.Rules.Core;
        _world = nav.World;
    }

    /// <summary>Finds the path now, however long it takes: for tests, benchmarks and one-off questions.</summary>
    public PathRequest FindNow(Int3 from, Int3 to) => FindNow(from, to, hierarchical: true);

    /// <summary><see cref="FindNow(Int3, Int3)"/>, optionally with plain cell A* at any range (the optimal reference).</summary>
    internal PathRequest FindNow(Int3 from, Int3 to, bool hierarchical)
    {
        var r = new PathRequest(from, to);
        if (AnswerNow(r)) return r;
        Begin(r, hierarchical);
        int budget = int.MaxValue;
        Advance(ref budget);
        return r;
    }

    /// <summary>The request being searched, if any.</summary>
    internal PathRequest? Current => _current;

    /// <summary>Answers what needs no search, and returns true if it did: unreachable ends, and a cell to itself.</summary>
    internal bool AnswerNow(PathRequest r)
    {
        int a = _nav.ComponentOf(r.From);
        if (a < 0 || a != _nav.ComponentOf(r.To) || !_rules.IsStandable(r.From) || !_rules.IsStandable(r.To))
        {
            Finish(r, PathStatus.Unreachable);
            return true;
        }
        if (r.From != r.To) return false;
        Finish(r, PathStatus.Found);
        return true;
    }

    /// <summary>Starts searching for <paramref name="r"/> (which <see cref="AnswerNow"/> didn't answer), dropping any search in progress.</summary>
    internal void Begin(PathRequest r, bool hierarchical = true)
    {
        _current = r;
        _expanded = 0;
        _restarts = 0;
        _hierarchical = hierarchical;
        Start();
    }

    void Start()
    {
        var r = _current!;
        _startVersion = _seenVersion = _nav.Version;
        Int3 d = r.To - r.From;
        if (!_hierarchical || Math.Max(Math.Abs(d.X), Math.Max(Math.Abs(d.Y), Math.Abs(d.Z))) <= ShortRange) StartCells(Phase.Open);
        else StartLinks();
    }

    /// <summary>Drops the search in progress.</summary>
    internal void Abandon()
    {
        _current = null;
        _phase = Phase.Idle;
    }

    /// <summary>
    /// Advances the current search, expanding at most <paramref name="budget"/> nodes (it is reduced by those
    /// expanded). Returns true when the request is answered, false when the budget ran out first.
    /// </summary>
    internal bool Advance(ref int budget)
    {
        var r = _current ?? throw new InvalidOperationException("No search in progress");
        if (_nav.Version != _seenVersion)
        {
            // The navigation changed under the search: answer at once what now needs no search (a goal walled off),
            // else start over on the new data. Only a few times, so constant edits can't starve a request: after
            // that the search finishes on mixed data, unrestricted (the corridor's section ids may be stale).
            _seenVersion = _nav.Version;
            if (AnswerNow(r))
            {
                r.NodesExpanded = _expanded;
                Abandon();
                return true;
            }
            if (_restarts < MaxRestarts)
            {
                _restarts++;
                Start();
            }
            else if (_phase != Phase.Open)
            {
                _startVersion = _seenVersion;
                StartCells(Phase.Open);
            }
        }
        while (true)
        {
            var outcome = _phase == Phase.Links ? StepLinks(ref budget) : StepCells(ref budget);
            r.NodesExpanded = _expanded;
            switch (outcome)
            {
                case Outcome.OutOfBudget:
                    return false;
                case Outcome.TooLong:
                    Done(r, PathStatus.TooLong);
                    return true;
                case Outcome.Found when _phase == Phase.Links:
                    BuildCorridor();
                    StartCells(Phase.Corridor);
                    continue;
                case Outcome.Found:
                    WritePath(r);
                    Done(r, PathStatus.Found);
                    return true;
                case Outcome.Exhausted when _phase != Phase.Open:
                    StartCells(Phase.Open); // the links or the corridor missed a way the cells have
                    continue;
                default:
                    Done(r, PathStatus.Unreachable);
                    return true;
            }
        }
    }

    void Done(PathRequest r, PathStatus status)
    {
        r.Status = status;
        r.NavVersion = _startVersion;
        Abandon();
    }

    void Finish(PathRequest r, PathStatus status)
    {
        r.Status = status;
        r.NavVersion = _nav.Version;
    }

    /// <summary>The admissible lower bound on the cost from <paramref name="a"/> to <paramref name="b"/>.</summary>
    static float Octile(Int3 a, Int3 b)
    {
        int dx = Math.Abs(a.X - b.X), dy = Math.Abs(a.Y - b.Y), dz = Math.Abs(a.Z - b.Z);
        float flat = Math.Max(dx, dz) + DiagonalExtra * Math.Min(dx, dz);
        return Math.Max(flat + 0.5f * dy, dy);
    }

    /// <summary>
    /// The estimated cost between two cells of one section, with landmark costs <paramref name="ma"/> and
    /// <paramref name="mb"/>: the octile bound, or where higher the landmarks' triangle-inequality bound. That one is
    /// measured inside the chunk, so it can overshoot where a shorter way leaves the chunk; it is capped at
    /// <see cref="LandmarkCap"/> times the octile distance.
    /// </summary>
    static float Estimate(Int3 a, ReadOnlySpan<float> ma, Int3 b, ReadOnlySpan<float> mb)
    {
        float octile = Octile(a, b), landmarks = 0;
        for (int i = 0; i < Landmarks; i++) landmarks = Math.Max(landmarks, Math.Abs(ma[i] - mb[i]));
        return Math.Max(octile, Math.Min(landmarks, LandmarkCap * octile));
    }

    /// <summary>Copies the landmark costs of a cell (standable as of the last Update) into <paramref name="into"/>; zeros if it isn't.</summary>
    void MarksOf(Int3 c, Span<float> into)
    {
        var chunk = _nav.ChunkAt(c);
        int i = chunk.IndexOf(c);
        if (i < 0 || chunk.LandmarkCosts.Length == 0) into.Clear();
        else chunk.LandmarkCosts.AsSpan(i * Landmarks, Landmarks).CopyTo(into);
    }

    // ---- Cell search ----

    void StartCells(Phase phase)
    {
        _phase = phase;
        _nodeCount = 0;
        _open.Clear();
        if (++_stamp == int.MaxValue)
        {
            Array.Clear(_slots);
            _stamp = 1;
        }
        int start = AddNode(_current!.From, phase == Phase.Corridor);
        _nodes[start].G = 0;
        _nodes[start].Parent = -1;
        _open.Push(_nodes[start].H, 0, start);
    }

    Outcome StepCells(ref int budget)
    {
        Int3 goal = _current!.To;
        bool corridor = _phase == Phase.Corridor;
        while (_open.Count > 0)
        {
            if (budget <= 0) return Outcome.OutOfBudget;
            if (_expanded >= _nodeLimit) return Outcome.TooLong;
            int i = _open.Pop();
            if (_nodes[i].Closed) continue; // a stale entry, superseded by a cheaper one
            _nodes[i].Closed = true;
            _expanded++;
            budget--;
            Int3 c = _nodes[i].Via.To;
            if (c == goal)
            {
                _goalNode = i;
                return Outcome.Found;
            }
            float g = _nodes[i].G;
            int n = _rules.GetMoves(c, _moves);
            for (int k = 0; k < n; k++)
            {
                ref readonly Move m = ref _moves[k];
                int j = FindNode(m.To);
                if (j < 0) j = AddNode(m.To, corridor);
                ref Node next = ref _nodes[j];
                if (next.Closed) continue;
                float ng = g + m.Cost;
                if (ng >= next.G) continue;
                next.G = ng;
                next.Parent = i;
                next.Via = m;
                _open.Push(ng + next.H, ng, j);
            }
        }
        return Outcome.Exhausted;
    }

    void WritePath(PathRequest r)
    {
        int count = 0;
        for (int i = _goalNode; _nodes[i].Parent >= 0; i = _nodes[i].Parent) count++;
        var moves = new Move[count];
        for (int i = _goalNode; _nodes[i].Parent >= 0; i = _nodes[i].Parent) moves[--count] = _nodes[i].Via;
        r.Moves = moves;
        r.Cost = _nodes[_goalNode].G;
    }

    int KeyOf(Int3 c) => (c.Y * _world.SizeZ + c.Z) * _world.SizeX + c.X;

    int SlotOf(int key) => (int)((uint)key * 2654435769u >> (32 - _slotBits));

    int FindNode(Int3 c)
    {
        int key = KeyOf(c), mask = _slots.Length - 1;
        for (int s = SlotOf(key); _slots[s].Stamp == _stamp; s = (s + 1) & mask)
            if (_slots[s].Key == key) return _slots[s].Node;
        return -1;
    }

    /// <summary>Adds an unreached node for a cell not in the map yet: closed if <paramref name="corridor"/> and it's outside.</summary>
    int AddNode(Int3 c, bool corridor)
    {
        if (_nodeCount == _nodes.Length) Array.Resize(ref _nodes, _nodes.Length * 2);
        if (2 * (_nodeCount + 1) > _slots.Length) GrowSlots();
        int i = _nodeCount++;
        float h = corridor ? CorridorHeuristic(c) : Octile(c, _current!.To);
        _nodes[i] = new Node { Via = new Move(c, MoveKind.Walk, 0, 0), G = float.PositiveInfinity, H = h, Closed = h < 0 };
        Insert(KeyOf(c), i);
        return i;
    }

    void Insert(int key, int node)
    {
        int mask = _slots.Length - 1, s = SlotOf(key);
        while (_slots[s].Stamp == _stamp) s = (s + 1) & mask;
        _slots[s] = new Slot { Stamp = _stamp, Key = key, Node = node };
    }

    void GrowSlots()
    {
        _slots = new Slot[_slots.Length * 2];
        _slotBits++;
        _stamp = 1;
        for (int i = 0; i < _nodeCount; i++) Insert(KeyOf(_nodes[i].Via.To), i);
    }

    // ---- Link search ----

    void StartLinks()
    {
        _phase = Phase.Links;
        var r = _current!;
        _startSection = _nav.SectionOf(r.From);
        _goalSection = _nav.SectionOf(r.To);
        int sections = _nav.SectionCount;
        if (_corridor.Length < sections)
        {
            int n = Math.Max(sections, _corridor.Length * 2);
            Array.Resize(ref _linkBase, n);
            Array.Resize(ref _baseStamp, n);
            _corridor = new int[n];
            _exitStart = new int[n];
            _exitCount = new int[n];
            _exitFill = new int[n];
            _exitPlaced = new int[n];
        }
        if (++_linkStamp == int.MaxValue)
        {
            Array.Clear(_baseStamp);
            Array.Clear(_linkSeen);
            Array.Clear(_linkDone);
            Array.Clear(_fromMarked);
            Array.Clear(_toMarked);
            Array.Clear(_corridor);
            Array.Clear(_exitPlaced);
            _linkStamp = 1;
        }
        _linkCount = 0;
        _linkOpen.Clear();
        _settled.Clear();
        _bestStart = float.PositiveInfinity;
        MarksOf(r.From, _startMarks);
        MarksOf(r.To, _goalMarks);

        // Seed with the links into the goal's section, valued by their landing's estimate to the goal.
        foreach (var link in _nav.LinksOf(_goalSection))
        {
            int u = link.ToSection, q = LinkFrom(u, _goalSection);
            if (q < 0) continue;
            var into = LinkAt(u, q);
            Reach(q, u, into.Cost + Estimate(into.To, ToMarks(q, into), r.To, _goalMarks), into.From);
        }
    }

    ref readonly SectionLink LinkAt(int section, int link) => ref _nav.LinksOf(section)[link - _linkBase[section]];

    /// <summary>The number of the section's first link in this search, assigned when first asked.</summary>
    int BaseOf(int section)
    {
        if (_baseStamp[section] == _linkStamp) return _linkBase[section];
        _baseStamp[section] = _linkStamp;
        int first = _linkBase[section] = _linkCount;
        _linkCount += _nav.LinksOf(section).Length;
        if (_linkV.Length < _linkCount)
        {
            int n = Math.Max(_linkCount, Math.Max(1024, _linkV.Length * 2));
            Array.Resize(ref _linkV, n);
            Array.Resize(ref _fromMarks, n * Landmarks);
            Array.Resize(ref _toMarks, n * Landmarks);
            Array.Resize(ref _linkSection, n);
            Array.Resize(ref _linkSeen, n);
            Array.Resize(ref _linkDone, n);
            Array.Resize(ref _fromMarked, n);
            Array.Resize(ref _toMarked, n);
        }
        return first;
    }

    /// <summary>The number of the link from section <paramref name="from"/> to section <paramref name="to"/>, or -1.</summary>
    int LinkFrom(int from, int to)
    {
        var links = _nav.LinksOf(from);
        int lo = 0, hi = links.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1, t = links[mid].ToSection;
            if (t == to) return BaseOf(from) + mid;
            if (t < to) lo = mid + 1;
            else hi = mid - 1;
        }
        return -1;
    }

    /// <summary>The landmark costs of link <paramref name="p"/>'s From cell (in its own section), looked up once per search.</summary>
    ReadOnlySpan<float> FromMarks(int p, in SectionLink link)
    {
        var marks = _fromMarks.AsSpan(p * Landmarks, Landmarks);
        if (_fromMarked[p] != _linkStamp)
        {
            _fromMarked[p] = _linkStamp;
            MarksOf(link.From, marks);
        }
        return marks;
    }

    /// <summary>The landmark costs of link <paramref name="p"/>'s To cell (in the section it leads to), looked up once per search.</summary>
    ReadOnlySpan<float> ToMarks(int p, in SectionLink link)
    {
        var marks = _toMarks.AsSpan(p * Landmarks, Landmarks);
        if (_toMarked[p] != _linkStamp)
        {
            _toMarked[p] = _linkStamp;
            MarksOf(link.To, marks);
        }
        return marks;
    }

    void Reach(int link, int section, float v, Int3 from)
    {
        if (_linkDone[link] == _linkStamp || (_linkSeen[link] == _linkStamp && v >= _linkV[link])) return;
        _linkSeen[link] = _linkStamp;
        _linkV[link] = v;
        _linkSection[link] = section;
        _linkOpen.Push(v + Octile(_current!.From, from), v, link);
    }

    /// <summary>
    /// A* backwards from the goal over links: a link's value is its cost plus the estimated cost from where it lands to
    /// the goal. It stops once no link left can improve the start's best estimate.
    /// </summary>
    Outcome StepLinks(ref int budget)
    {
        Int3 start = _current!.From;
        while (_linkOpen.Count > 0)
        {
            if (budget <= 0) return Outcome.OutOfBudget;
            if (_expanded >= _nodeLimit) return Outcome.TooLong;
            if (_linkOpen.PeekF >= _bestStart) return Outcome.Found;
            int p = _linkOpen.Pop();
            if (_linkDone[p] == _linkStamp) continue;
            _linkDone[p] = _linkStamp;
            _settled.Add(p);
            _expanded++;
            budget--;
            int s = _linkSection[p];
            ref readonly var settled = ref LinkAt(s, p);
            Int3 exit = settled.From;
            var exitMarks = FromMarks(p, settled);
            float v = _linkV[p];
            if (s == _startSection) _bestStart = Math.Min(_bestStart, Estimate(start, _startMarks, exit, exitMarks) + v);
            foreach (var link in _nav.LinksOf(s))
            {
                int u = link.ToSection, q = LinkFrom(u, s);
                if (q < 0 || _linkDone[q] == _linkStamp) continue;
                ref readonly var into = ref LinkAt(u, q);
                Reach(q, u, into.Cost + Estimate(into.To, ToMarks(q, into), exit, exitMarks) + v, into.From);
            }
        }
        return _bestStart < float.PositiveInfinity ? Outcome.Found : Outcome.Exhausted;
    }

    /// <summary>The corridor: the goal's section and every section with a settled link, which become its exits.</summary>
    void BuildCorridor()
    {
        _corridor[_goalSection] = _linkStamp;
        _exitCount[_goalSection] = 0;
        foreach (int p in _settled)
        {
            int s = _linkSection[p];
            if (_corridor[s] != _linkStamp)
            {
                _corridor[s] = _linkStamp;
                _exitCount[s] = 0;
            }
            _exitCount[s]++;
        }
        if (_exitCell.Length < _settled.Count)
        {
            int n = Math.Max(_settled.Count, _exitCell.Length * 2);
            _exitCell = new Int3[n];
            _exitV = new float[n];
            _exitMarks = new float[n * Landmarks];
        }
        // Lay the exits out section by section (_exitFill is where a section's next one goes), then prune each section's.
        int total = 0;
        foreach (int p in _settled)
        {
            int s = _linkSection[p];
            if (_exitPlaced[s] == _linkStamp) continue;
            _exitPlaced[s] = _linkStamp;
            _exitStart[s] = _exitFill[s] = total;
            total += _exitCount[s];
        }
        foreach (int p in _settled)
        {
            int s = _linkSection[p], k = _exitFill[s]++;
            ref readonly var link = ref LinkAt(s, p);
            _exitCell[k] = link.From;
            _exitV[k] = _linkV[p];
            FromMarks(p, link).CopyTo(ExitMarks(k));
        }
        foreach (int p in _settled)
        {
            int s = _linkSection[p];
            if (_exitPlaced[s] != _linkStamp) continue;
            _exitPlaced[s] = 0;
            _exitCount[s] = PruneExits(_exitStart[s], _exitCount[s]);
        }
    }

    /// <summary>
    /// Sorts a section's exits by value and drops each that can't give the minimum: one whose value is no less than a
    /// cheaper exit's plus the way between them. Returns how many remain (at the front).
    /// </summary>
    int PruneExits(int start, int count)
    {
        Span<float> marks = stackalloc float[Landmarks];
        for (int i = start + 1; i < start + count; i++) // insertion sort: sections have few exits
        {
            Int3 cell = _exitCell[i];
            float v = _exitV[i];
            ExitMarks(i).CopyTo(marks);
            int j = i - 1;
            for (; j >= start && _exitV[j] > v; j--) CopyExit(j, j + 1);
            _exitCell[j + 1] = cell;
            _exitV[j + 1] = v;
            marks.CopyTo(ExitMarks(j + 1));
        }
        int kept = 0;
        for (int i = start; i < start + count; i++)
        {
            bool dominated = false;
            for (int k = start; k < start + kept && !dominated; k++)
                dominated = _exitV[i] >= _exitV[k] + Estimate(_exitCell[i], ExitMarks(i), _exitCell[k], ExitMarks(k));
            if (dominated) continue;
            if (start + kept != i) CopyExit(i, start + kept);
            kept++;
        }
        return kept;
    }

    Span<float> ExitMarks(int k) => _exitMarks.AsSpan(k * Landmarks, Landmarks);

    void CopyExit(int from, int to)
    {
        _exitCell[to] = _exitCell[from];
        _exitV[to] = _exitV[from];
        ExitMarks(from).CopyTo(ExitMarks(to));
    }

    /// <summary>
    /// The corridor's estimate of a cell's cost to the goal, weighted: through the cheapest exit of its section (or
    /// straight to the goal in the goal's section), never below the octile bound. Negative when the cell is outside.
    /// </summary>
    float CorridorHeuristic(Int3 c)
    {
        var chunk = _nav.ChunkAt(c);
        int i = chunk.IndexOf(c);
        if (i < 0) return -1;
        int s = chunk.Sections[chunk.Local[i]];
        if (s >= _corridor.Length || _corridor[s] != _linkStamp) return -1;
        var marks = chunk.LandmarkCosts.AsSpan(i * Landmarks, Landmarks);
        Int3 goal = _current!.To;
        float h = s == _goalSection ? Estimate(c, marks, goal, _goalMarks) : float.PositiveInfinity;
        for (int k = _exitStart[s], end = k + _exitCount[s]; k < end; k++)
            h = Math.Min(h, Estimate(c, marks, _exitCell[k], ExitMarks(k)) + _exitV[k]);
        return CorridorWeight * Math.Max(h, Octile(c, goal));
    }

    /// <summary>A binary min-heap of node ids by f; equal f pops the larger g first (the deeper node, fewer ties expanded).</summary>
    sealed class Frontier
    {
        struct Entry
        {
            public float F, G;
            public int Id;
        }

        Entry[] _items = new Entry[1024];

        public int Count { get; private set; }

        public float PeekF => _items[0].F;

        public void Clear() => Count = 0;

        public void Push(float f, float g, int id)
        {
            if (Count == _items.Length) Array.Resize(ref _items, Count * 2);
            int i = Count++;
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (!Before(f, g, _items[p])) break;
                _items[i] = _items[p];
                i = p;
            }
            _items[i] = new Entry { F = f, G = g, Id = id };
        }

        public int Pop()
        {
            int top = _items[0].Id;
            var last = _items[--Count];
            int i = 0;
            while (true)
            {
                int c = 2 * i + 1;
                if (c >= Count) break;
                if (c + 1 < Count && Before(_items[c + 1].F, _items[c + 1].G, _items[c])) c++;
                if (!Before(_items[c].F, _items[c].G, last)) break;
                _items[i] = _items[c];
                i = c;
            }
            _items[i] = last;
            return top;
        }

        static bool Before(float f, float g, in Entry e) => f < e.F || (f == e.F && g > e.G);
    }
}
