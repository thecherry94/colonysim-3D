using System.Diagnostics;

namespace ColonySim.Core;

/// <summary>A move from section to section across a chunk border: the cheapest one between that pair of sections.</summary>
public readonly record struct SectionLink(int ToSection, Int3 From, Int3 To, float Cost);

/// <summary>
/// Navigation sections and exact connectivity over the known world.
/// <para>
/// Each chunk's standable cells are split into <em>sections</em>: the sets connected by <see cref="NavRules"/> moves
/// that stay inside the chunk. Moves across chunk borders, vertical ones included, become <see cref="SectionLink"/>s,
/// and components are the closure of sections over links. That is exactly cell connectivity, because moves are
/// symmetric, so <see cref="ComponentOf"/> answers "can a colonist get from A to B" in O(log n).
/// </para>
/// <para>
/// The world reports edits through <see cref="VoxelWorld.Changed"/>; <see cref="Update"/> rebuilds the chunks they
/// touched, re-derives the links of those chunks' neighbours and recomputes the components. Queries reflect the last
/// <see cref="Update"/>: edits made since are not visible until it runs.
/// </para>
/// <para>
/// Threading: main thread only (the constructor builds the chunks in parallel, but returns only when done).
/// </para>
/// </summary>
public sealed class Navigation
{
    readonly VoxelWorld _world;
    readonly PlayerView _view;
    readonly NavChunk[] _chunks;
    readonly int _chunksX, _chunksY, _chunksZ;
    readonly NavChunk.Builder _builder = new();

    // Per global section id. Ids freed by a rebuild (chunk -1, no links) are reused by later ones.
    int _sectionCount;
    int[] _sectionChunk = [];
    Int3[] _anchors = [];
    SectionLink[][] _links = [];
    int[] _component = [];
    readonly Stack<int> _free = new();

    // Chunks dirtied since the last Update, as a list and a mark per chunk.
    readonly List<int> _dirty = new();
    readonly bool[] _dirtyMark;

    // Scratch.
    readonly bool[] _chunkMark;
    readonly List<(int Local, SectionLink Link)> _candidates = new();
    readonly List<SectionLink> _sectionLinks = new();
    readonly List<int> _neighbours = new();
    readonly Dictionary<int, List<CrossMove>> _reversed = new();
    int[] _parent = [];
    int[] _previewParent = [];
    int _previewSerial;

    public Navigation(ColonyMap map)
    {
        _world = map.World;
        _view = map.View;
        Rules = new NavRules(map.View);
        _chunksX = _world.ChunksX; _chunksY = _world.ChunksY; _chunksZ = _world.ChunksZ;
        int count = _chunksX * _chunksY * _chunksZ;
        _chunks = new NavChunk[count];
        _dirtyMark = new bool[count];
        _chunkMark = new bool[count];

        Parallel.For(0, count, () => new NavChunk.Builder(),
            (i, _, b) => { _chunks[i] = NavChunk.Build(Rules.Core, CoordsOf(i), b); return b; }, _ => { });
        for (int i = 0; i < count; i++) AssignIds(i, _chunks[i]);
        // Each chunk writes only its own sections' links, reading the (now complete) chunks.
        Parallel.For(0, count, () => (new List<(int, SectionLink)>(), new List<SectionLink>()),
            (i, _, scratch) => { WriteLinks(_chunks[i], CrossCandidates(_chunks[i], scratch.Item1), scratch.Item2); return scratch; },
            _ => { });
        foreach (var c in _chunks) c.DropCross();
        RecomputeComponents();
        _world.Changed += OnWorldChanged;
    }

    /// <summary>The move rules every answer is built on.</summary>
    public NavRules Rules { get; }

    /// <summary>Starts at 1 and increments whenever an <see cref="Update"/> changes sections, links or components.</summary>
    public int Version { get; private set; } = 1;

    /// <summary>One past the highest section id. Ids below it that a rebuild freed have no links and no members.</summary>
    public int SectionCount => _sectionCount;

    /// <summary>The cell's connectivity component, or -1 when it isn't standable. Ids are arbitrary and change between updates.</summary>
    public int ComponentOf(Int3 c)
    {
        int s = SectionOf(c);
        return s < 0 ? -1 : _component[s];
    }

    /// <summary>The cell's section, or -1 when it isn't standable.</summary>
    public int SectionOf(Int3 c)
    {
        if (!_world.InBounds(c.X, c.Y, c.Z)) return -1;
        var chunk = _chunks[ChunkIndexOf(c)];
        int local = chunk.LocalOf(c);
        return local < 0 ? -1 : chunk.Sections[local];
    }

    /// <summary>A standable cell of the section, near its middle.</summary>
    public Int3 SectionAnchor(int section) => _anchors[section];

    /// <summary>The section's links, one per neighbouring section (the cheapest move to it), ordered by <see cref="SectionLink.ToSection"/>.</summary>
    public IReadOnlyList<SectionLink> SectionLinks(int section) => _links[section];

    /// <summary>The component of the standable cell nearest <paramref name="anchor"/> (within 8), or -1 if there is none.</summary>
    public int HomeComponent(Int3 anchor) => Rules.FindStandableNear(anchor, 8, out var c) ? ComponentOf(c) : -1;

    /// <summary>
    /// Rebuilds every chunk dirtied since the last call and re-derives the links of their neighbours; then, if any
    /// section or link changed, recomputes the components and bumps <see cref="Version"/>.
    /// </summary>
    public void Update()
    {
        if (_dirty.Count == 0) return;
        _previewSerial++; // outstanding previews read chunks this replaces
        bool changed = false;
        foreach (int ci in _dirty)
        {
            var old = _chunks[ci];
            var rebuilt = NavChunk.Build(Rules.Core, CoordsOf(ci), _builder);
            if (rebuilt.SameSections(old))
            {
                if (rebuilt != NavChunk.Empty) rebuilt.Sections = old.Sections;
            }
            else
            {
                Release(old);
                AssignIds(ci, rebuilt);
                changed = true;
            }
            _chunks[ci] = rebuilt;
        }

        foreach (int ci in _dirty) changed |= WriteLinks(_chunks[ci], CrossCandidates(_chunks[ci], _candidates), _sectionLinks);

        // Clean neighbours keep their links into clean chunks; those into rebuilt chunks are re-derived from the rebuilt
        // chunks' moves out, reversed (moves are symmetric).
        var neighbours = _neighbours;
        neighbours.Clear();
        foreach (int ci in _dirty)
        {
            Int3 c = CoordsOf(ci);
            for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = c.X + dx, y = c.Y + dy, z = c.Z + dz;
                        if ((uint)x >= (uint)_chunksX || (uint)y >= (uint)_chunksY || (uint)z >= (uint)_chunksZ) continue;
                        int ni = ChunkIndex(x, y, z);
                        if (_dirtyMark[ni] || _chunkMark[ni] || _chunks[ni].SectionCount == 0) continue;
                        _chunkMark[ni] = true;
                        neighbours.Add(ni);
                    }
        }
        var reversed = _reversed;
        foreach (var list in reversed.Values) list.Clear();
        foreach (int ci in _dirty)
            foreach (var m in _chunks[ci].Cross)
            {
                int ti = ChunkIndexOf(m.To);
                if (!_chunkMark[ti]) continue; // into another rebuilt chunk, which found the way back itself
                if (!reversed.TryGetValue(ti, out var list)) reversed[ti] = list = new();
                list.Add(m);
            }
        foreach (int ni in neighbours)
        {
            var chunk = _chunks[ni];
            _candidates.Clear();
            for (int l = 0; l < chunk.SectionCount; l++)
                foreach (var link in _links[chunk.Sections[l]])
                    if (!_dirtyMark[ChunkIndexOf(link.To)]) _candidates.Add((l, link));
            if (reversed.TryGetValue(ni, out var back))
                foreach (var m in back)
                    _candidates.Add((chunk.LocalOf(m.To), new SectionLink(SectionOf(m.From), m.To, m.From, m.ReverseCost)));
            changed |= WriteLinks(chunk, _candidates, _sectionLinks);
            _chunkMark[ni] = false;
        }

        foreach (int ci in _dirty)
        {
            _chunks[ci].DropCross();
            _dirtyMark[ci] = false;
        }
        _dirty.Clear();
        if (!changed) return;
        RecomputeComponents();
        Version++;
    }

    /// <summary>
    /// Connectivity as it would be with <paramref name="cell"/> set to <paramref name="block"/> in the known world. The
    /// navigation state is left as it is (pending edits are applied first, with <see cref="Update"/>). The preview is
    /// valid until the next <see cref="Preview"/> or <see cref="Update"/>.
    /// </summary>
    public ConnectivityPreview Preview(Int3 cell, ushort block)
    {
        Update();
        int serial = ++_previewSerial;
        var rules = new NavRules<OverlaidBlocks>(new OverlaidBlocks(_view, cell, block));
        var indices = new List<int>();
        var chunks = new List<NavChunk>();
        var bases = new List<int>();
        int next = _sectionCount;
        var (lo, hi) = ChunksReading(cell);
        for (int cy = lo.Y; cy <= hi.Y; cy++)
            for (int cz = lo.Z; cz <= hi.Z; cz++)
                for (int cx = lo.X; cx <= hi.X; cx++)
                {
                    var chunk = NavChunk.Build(rules, new Int3(cx, cy, cz), _builder, costs: false);
                    int ci = ChunkIndex(cx, cy, cz);
                    indices.Add(ci);
                    chunks.Add(chunk);
                    bases.Add(next);
                    next += chunk.SectionCount;
                    _chunkMark[ci] = true;
                }

        if (_previewParent.Length < next) _previewParent = new int[Math.Max(next, _previewParent.Length * 2)];
        var parent = _previewParent;
        for (int s = 0; s < next; s++) parent[s] = s;
        // Everything outside the rebuilt chunks keeps its links, except those into them: the rebuilt chunks' own moves
        // out replace both directions.
        for (int s = 0; s < _sectionCount; s++)
        {
            int ci = _sectionChunk[s];
            if (ci < 0 || _chunkMark[ci]) continue;
            foreach (var link in _links[s])
                if (!_chunkMark[_sectionChunk[link.ToSection]]) UnionFind.Union(parent, s, link.ToSection);
        }
        for (int k = 0; k < chunks.Count; k++)
            foreach (var m in chunks[k].Cross)
            {
                int ti = ChunkIndexOf(m.To), to;
                if (_chunkMark[ti])
                {
                    int t = indices.IndexOf(ti);
                    to = bases[t] + chunks[t].LocalOf(m.To);
                }
                else to = SectionOf(m.To);
                Debug.Assert(to >= 0, $"move target {m.To} has no section");
                UnionFind.Union(parent, bases[k] + m.FromLocal, to);
            }
        foreach (int ci in indices) _chunkMark[ci] = false;
        return new ConnectivityPreview(this, serial, indices.ToArray(), chunks.ToArray(), bases.ToArray(), parent);
    }

    internal bool IsCurrent(int previewSerial) => previewSerial == _previewSerial;

    internal VoxelWorld World => _world;

    /// <summary><see cref="SectionLinks"/> as a span, for the pathfinder's inner loops.</summary>
    internal ReadOnlySpan<SectionLink> LinksOf(int section) => _links[section];

    /// <summary>The chunk holding a cell (any cell in bounds), for its keys, sections and landmark costs.</summary>
    internal NavChunk ChunkAt(Int3 c) => _chunks[ChunkIndexOf(c)];

    internal int ChunkIndexOf(Int3 c) => ChunkIndex(c.X >> 5, c.Y >> 5, c.Z >> 5);

    int ChunkIndex(int cx, int cy, int cz) => (cy * _chunksZ + cz) * _chunksX + cx;

    Int3 CoordsOf(int index) => new(index % _chunksX, index / (_chunksX * _chunksZ), index / _chunksX % _chunksZ);

    /// <summary>
    /// The chunks (inclusive coordinate range) holding every cell whose standability or moves read the block at
    /// <paramref name="c"/>: x and z within 1, y from 4 below (climb clearance) to 1 above (standing on it). Every move
    /// whose existence depends on that block has its lower end there.
    /// </summary>
    (Int3 Lo, Int3 Hi) ChunksReading(Int3 c) => (
        new(Math.Max(c.X - 1, 0) >> 5, Math.Max(c.Y - 4, 0) >> 5, Math.Max(c.Z - 1, 0) >> 5),
        new(Math.Min(c.X + 1, _world.SizeX - 1) >> 5, Math.Min(c.Y + 1, _world.SizeY - 1) >> 5, Math.Min(c.Z + 1, _world.SizeZ - 1) >> 5));

    void OnWorldChanged(int x, int y, int z)
    {
        var (lo, hi) = ChunksReading(new Int3(x, y, z));
        for (int cy = lo.Y; cy <= hi.Y; cy++)
            for (int cz = lo.Z; cz <= hi.Z; cz++)
                for (int cx = lo.X; cx <= hi.X; cx++)
                {
                    int ci = ChunkIndex(cx, cy, cz);
                    if (_dirtyMark[ci]) continue;
                    _dirtyMark[ci] = true;
                    _dirty.Add(ci);
                }
    }

    void AssignIds(int chunkIndex, NavChunk chunk)
    {
        if (chunk.SectionCount == 0) return;
        chunk.Sections = new int[chunk.SectionCount];
        for (int l = 0; l < chunk.SectionCount; l++)
        {
            int id = _free.Count > 0 ? _free.Pop() : NewId();
            chunk.Sections[l] = id;
            _sectionChunk[id] = chunkIndex;
            _anchors[id] = chunk.Anchors[l];
            _links[id] = [];
        }
    }

    int NewId()
    {
        if (_sectionCount == _sectionChunk.Length)
        {
            int n = Math.Max(1024, _sectionCount * 2);
            Array.Resize(ref _sectionChunk, n);
            Array.Resize(ref _anchors, n);
            Array.Resize(ref _links, n);
        }
        return _sectionCount++;
    }

    void Release(NavChunk chunk)
    {
        foreach (int id in chunk.Sections)
        {
            _sectionChunk[id] = -1;
            _links[id] = [];
            _free.Push(id);
        }
    }

    /// <summary>The chunk's moves out as link candidates, keyed by local section.</summary>
    List<(int Local, SectionLink Link)> CrossCandidates(NavChunk chunk, List<(int Local, SectionLink Link)> into)
    {
        into.Clear();
        foreach (var m in chunk.Cross)
        {
            int to = SectionOf(m.To);
            Debug.Assert(to >= 0, $"move target {m.To} has no section");
            into.Add((m.FromLocal, new SectionLink(to, m.From, m.To, m.Cost)));
        }
        return into;
    }

    /// <summary>Keeps the cheapest candidate per (section, target) as the chunk's sections' links; true if any changed.</summary>
    bool WriteLinks(NavChunk chunk, List<(int Local, SectionLink Link)> candidates, List<SectionLink> scratch)
    {
        candidates.Sort((a, b) => LinkOrder(chunk, a, b));
        bool changed = false;
        int k = 0;
        for (int l = 0; l < chunk.SectionCount; l++)
        {
            scratch.Clear();
            for (; k < candidates.Count && candidates[k].Local == l; k++)
                if (scratch.Count == 0 || scratch[^1].ToSection != candidates[k].Link.ToSection) scratch.Add(candidates[k].Link);
            SectionLink[] links = scratch.Count == 0 ? [] : scratch.ToArray();
            int id = chunk.Sections[l];
            if (!changed && !links.AsSpan().SequenceEqual(_links[id])) changed = true;
            _links[id] = links;
        }
        return changed;
    }

    /// <summary>
    /// By section, target, cost, then nearness to the middle between the two sections' anchors (so that between open
    /// sections the link sits mid-border, where paths cross, not in a corner: the pathfinder steers by link cells),
    /// then the cells, so the kept link doesn't depend on the order moves were found.
    /// </summary>
    int LinkOrder(NavChunk chunk, (int Local, SectionLink Link) a, (int Local, SectionLink Link) b)
    {
        int d = a.Local.CompareTo(b.Local);
        if (d == 0) d = a.Link.ToSection.CompareTo(b.Link.ToSection);
        if (d == 0) d = a.Link.Cost.CompareTo(b.Link.Cost);
        if (d == 0)
        {
            Int3 mid = chunk.Anchors[a.Local] + _anchors[a.Link.ToSection]; // twice the middle
            d = Off(a.Link, mid).CompareTo(Off(b.Link, mid));
        }
        if (d == 0) d = Compare(a.Link.From, b.Link.From);
        if (d == 0) d = Compare(a.Link.To, b.Link.To);
        return d;

        static int Off(SectionLink l, Int3 mid)
        {
            Int3 o = l.From + l.To - mid;
            return o.X * o.X + o.Y * o.Y + o.Z * o.Z;
        }

        static int Compare(Int3 p, Int3 q)
        {
            int e = p.X.CompareTo(q.X);
            if (e == 0) e = p.Y.CompareTo(q.Y);
            return e != 0 ? e : p.Z.CompareTo(q.Z);
        }
    }

    void RecomputeComponents()
    {
        if (_parent.Length < _sectionCount) _parent = new int[_sectionChunk.Length];
        if (_component.Length < _sectionCount) _component = new int[_sectionChunk.Length];
        for (int s = 0; s < _sectionCount; s++) _parent[s] = s;
        for (int s = 0; s < _sectionCount; s++)
            foreach (var link in _links[s]) UnionFind.Union(_parent, s, link.ToSection);
        for (int s = 0; s < _sectionCount; s++) _component[s] = _sectionChunk[s] < 0 ? -1 : UnionFind.Find(_parent, s);
    }
}

/// <summary>Union-find over int ids in a parent array; a root is its set's smallest id.</summary>
internal static class UnionFind
{
    public static int Find(int[] parent, int x)
    {
        while (parent[x] != x)
        {
            parent[x] = parent[parent[x]];
            x = parent[x];
        }
        return x;
    }

    public static void Union(int[] parent, int a, int b)
    {
        a = Find(parent, a);
        b = Find(parent, b);
        if (a < b) parent[b] = a;
        else if (b < a) parent[a] = b;
    }
}
