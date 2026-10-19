namespace ColonySim.Core;

/// <summary>
/// Pending jobs keyed by cell, with ids, claims, progress and a per-chunk spatial index.
/// <para>
/// It also keeps the work order: digs and carves go top-down, builds bottom-up. A dig or carve waits while a dig or
/// carve is 1–3 cells straight above it; a build waits while another build is 1–4 cells below it, in its column or a
/// neighbouring one. The waiting counts are kept up to date as jobs come and go, and each chunk lists its ready jobs
/// apart, by kind, so a job search only ever looks at jobs of the kinds it wants that can be worked now.
/// </para>
/// </summary>
public sealed class JobQueue
{
    const int ChunkShift = 5;

    /// <summary>Edge length of the spatial-index chunks (the same grid as the voxel chunks).</summary>
    public const int ChunkSize = 1 << ChunkShift;

    readonly Dictionary<Int3, Job> _jobs = new();
    readonly Dictionary<long, Job> _byId = new();
    readonly Dictionary<Int3, List<Job>> _chunks = new();
    readonly Dictionary<Int3, List<Job>>[] _ready = Enum.GetValues<JobKind>().Select(_ => new Dictionary<Int3, List<Job>>()).ToArray();
    readonly int[] _readyOfKind = new int[Enum.GetValues<JobKind>().Length];
    readonly HashSet<Int3> _changed = new();

    public int Count => _jobs.Count;
    public IEnumerable<Job> All => _jobs.Values;

    /// <summary>Jobs no other job must go before (the work order above).</summary>
    public int ReadyCount { get; private set; }

    /// <summary>Increments whenever a job may have become available to work: it became ready, or its claim was released.</summary>
    public int ReadyVersion { get; private set; }

    /// <summary>Increments on every change.</summary>
    public int Version { get; private set; }

    /// <summary>Next id handed out; saves restore it so ids never repeat.</summary>
    public long NextId { get; internal set; } = 1;

    /// <summary>Chunk counts per axis of the map, bounding <see cref="ByRing"/>. <see cref="ColonyMap"/> sets it.</summary>
    public Int3 ChunkCounts { get; internal set; } = new(16, 8, 16);

    /// <summary>The highest ring <see cref="ByRing"/> can reach.</summary>
    public int MaxRing => Math.Max(ChunkCounts.X, Math.Max(ChunkCounts.Y, ChunkCounts.Z));

    public static Int3 ChunkOf(Int3 p) => new(p.X >> ChunkShift, p.Y >> ChunkShift, p.Z >> ChunkShift);

    /// <summary>
    /// Adds a job. A job at the cell with the same kind and block is kept as is (progress and claim included);
    /// anything else there is replaced by a new job and released.
    /// </summary>
    public Job Add(JobSpec spec)
    {
        if (_jobs.TryGetValue(spec.Position, out var old))
        {
            if (old.Kind == spec.Kind && old.Block == spec.Block) return old;
            Detach(old);
        }
        var job = new Job(NextId++, spec);
        Attach(job);
        Touch(job);
        return job;
    }

    /// <summary>Loading: inserts a job with its saved id, claim and progress.</summary>
    internal void Restore(Job job)
    {
        if (_jobs.TryGetValue(job.Position, out var old)) Detach(old);
        Attach(job);
        Touch(job);
        if (job.Id >= NextId) NextId = job.Id + 1;
    }

    public bool Remove(Int3 p)
    {
        if (!_jobs.TryGetValue(p, out var job)) return false;
        Detach(job);
        Touch(job);
        return true;
    }

    public bool Contains(Int3 p) => _jobs.ContainsKey(p);
    public bool TryGet(Int3 p, out Job j) => _jobs.TryGetValue(p, out j!);
    public bool TryGetById(long id, out Job job) => _byId.TryGetValue(id, out job!);

    public void Claim(Job j, int colonistId) { j.ClaimedBy = colonistId; Touch(j); }
    public void Release(Job j) { j.ClaimedBy = 0; ReadyVersion++; Touch(j); }

    /// <summary>Progress is not shown by the overlay, so this neither bumps the version nor marks the chunk.</summary>
    public void AddProgress(Job j, float ticks) => j.Progress += ticks;

    /// <summary>Sets the job's status as found at a navigation version, or (version 0) at a simulation tick.</summary>
    public void SetStatus(Job j, JobStatus s, int navVersion, long tick = 0)
    {
        j.Status = s;
        j.StatusVersion = navVersion;
        j.StatusTick = tick;
        Touch(j);
    }

    public IEnumerable<Job> InChunk(Int3 chunk) =>
        _chunks.TryGetValue(chunk, out var list) ? list : Enumerable.Empty<Job>();

    /// <summary>
    /// Jobs by chunk ring: ring r holds the chunks at Chebyshev distance exactly r (in x, y and z) from the cell's
    /// chunk, clamped to the map. Order within a ring is unspecified.
    /// </summary>
    public IEnumerable<Job> ByRing(Int3 cell, int maxRing)
    {
        if (_jobs.Count == 0) yield break;
        var ring = new List<Job>();
        int limit = Math.Min(maxRing, MaxRing);
        for (int r = 0; r <= limit; r++)
        {
            ring.Clear();
            CollectRing(_chunks, cell, r, ring);
            foreach (var job in ring) yield return job;
        }
    }

    /// <summary>Ready jobs of the kind.</summary>
    public int ReadyOfKind(JobKind kind) => _readyOfKind[(int)kind];

    /// <summary>
    /// Appends the ready jobs (none waiting for another) of the kinds in ring <paramref name="r"/> around the cell, as
    /// <see cref="ByRing"/> counts rings, skipping the chunks with no cell nearer the cell than squared distance
    /// <paramref name="belowD2"/>.
    /// </summary>
    public void ReadyInRing(Int3 cell, int r, IReadOnlyList<JobKind> kinds, List<Job> into, int belowD2 = int.MaxValue)
    {
        foreach (var kind in kinds)
            if (_readyOfKind[(int)kind] > 0) CollectRing(_ready[(int)kind], cell, r, into, belowD2);
    }

    /// <summary>Appends the jobs of the ring's chunks: its shell only, so a ring costs its surface, not its volume.</summary>
    void CollectRing(Dictionary<Int3, List<Job>> index, Int3 cell, int r, List<Job> into, int belowD2 = int.MaxValue)
    {
        var c = ChunkOf(cell);
        var n = ChunkCounts;
        int x0 = Math.Max(0, c.X - r), x1 = Math.Min(n.X - 1, c.X + r);
        int y0 = Math.Max(0, c.Y - r), y1 = Math.Min(n.Y - 1, c.Y + r);
        int z0 = Math.Max(0, c.Z - r), z1 = Math.Min(n.Z - 1, c.Z + r);
        for (int y = y0; y <= y1; y++)
            for (int z = z0; z <= z1; z++)
            {
                if (Math.Abs(y - c.Y) == r || Math.Abs(z - c.Z) == r)
                {
                    for (int x = x0; x <= x1; x++) Append(x, y, z);
                    continue;
                }
                // Inside the shell's y and z range only the two x faces belong to the ring.
                if (c.X - r >= 0) Append(c.X - r, y, z);
                if (c.X + r < n.X) Append(c.X + r, y, z);
            }

        void Append(int x, int y, int z)
        {
            if (!index.TryGetValue(new Int3(x, y, z), out var list)) return;
            if (belowD2 != int.MaxValue)
            {
                int dx = Gap(cell.X, x), dy = Gap(cell.Y, y), dz = Gap(cell.Z, z);
                if ((long)dx * dx + (long)dy * dy + (long)dz * dz >= belowD2) return;
            }
            into.AddRange(list);
        }

        // How far a coordinate is from a chunk's span on that axis (0 inside it).
        static int Gap(int v, int chunk) => Math.Max(0, Math.Max(chunk * ChunkSize - v, v - (chunk * ChunkSize + ChunkSize - 1)));
    }

    /// <summary>
    /// Single consumer: the Game's DesignationOverlay drains this each frame, so nothing else may call it.
    /// Appends the chunks whose jobs were added, removed, claimed, released or re-statused since the last call.</summary>
    public void ConsumeChangedChunks(ICollection<Int3> into)
    {
        foreach (var c in _changed) into.Add(c);
        _changed.Clear();
    }

    void Attach(Job job)
    {
        _jobs[job.Position] = job;
        _byId[job.Id] = job;
        var chunk = ChunkOf(job.Position);
        if (!_chunks.TryGetValue(chunk, out var list)) _chunks[chunk] = list = new List<Job>();
        list.Add(job);
        job.WaitingFor = 0;
        Order(job, +1);
        if (job.WaitingFor == 0) MarkReady(job);
    }

    void Detach(Job job)
    {
        _jobs.Remove(job.Position);
        _byId.Remove(job.Id);
        var chunk = ChunkOf(job.Position);
        if (_chunks.TryGetValue(chunk, out var list))
        {
            list.Remove(job);
            if (list.Count == 0) _chunks.Remove(chunk);
        }
        if (job.ReadyIndex >= 0) MarkWaiting(job);
        Order(job, -1);
        job.ClaimedBy = 0;
    }

    static bool IsDig(JobKind k) => k is JobKind.Dig or JobKind.CarveStairs;

    /// <summary>
    /// The work order around a job coming (+1) or going (-1): the jobs that must wait for it count it in or out, and
    /// one coming counts the jobs it must wait for.
    /// </summary>
    void Order(Job job, int delta)
    {
        Int3 p = job.Position;
        if (IsDig(job.Kind))
        {
            for (int k = 1; k <= 3; k++)
            {
                if (delta > 0 && IsDigAt(p + Int3.Up * k)) job.WaitingFor++;
                if (_jobs.TryGetValue(p - Int3.Up * k, out var below) && IsDig(below.Kind)) Wait(below, delta);
            }
            return;
        }
        for (int k = 1; k <= 4; k++)
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (delta > 0 && IsBuildAt(p + new Int3(dx, -k, dz))) job.WaitingFor++;
                    if (_jobs.TryGetValue(p + new Int3(dx, k, dz), out var above) && !IsDig(above.Kind)) Wait(above, delta);
                }
    }

    bool IsDigAt(Int3 p) => _jobs.TryGetValue(p, out var j) && IsDig(j.Kind);
    bool IsBuildAt(Int3 p) => _jobs.TryGetValue(p, out var j) && !IsDig(j.Kind);

    void Wait(Job job, int delta)
    {
        int before = job.WaitingFor;
        job.WaitingFor += delta;
        if (before == 0) MarkWaiting(job);
        else if (job.WaitingFor == 0) MarkReady(job);
    }

    void MarkReady(Job job)
    {
        var chunk = ChunkOf(job.Position);
        var index = _ready[(int)job.Kind];
        if (!index.TryGetValue(chunk, out var list)) index[chunk] = list = new List<Job>();
        job.ReadyIndex = list.Count;
        list.Add(job);
        _readyOfKind[(int)job.Kind]++;
        ReadyCount++;
        ReadyVersion++;
    }

    /// <summary>Takes the job out of its chunk's ready list for its kind (swapping the last one into its place).</summary>
    void MarkWaiting(Job job)
    {
        var chunk = ChunkOf(job.Position);
        var index = _ready[(int)job.Kind];
        var list = index[chunk];
        var last = list[^1];
        list[job.ReadyIndex] = last;
        last.ReadyIndex = job.ReadyIndex;
        list.RemoveAt(list.Count - 1);
        if (list.Count == 0) index.Remove(chunk);
        job.ReadyIndex = -1;
        _readyOfKind[(int)job.Kind]--;
        ReadyCount--;
    }

    void Touch(Job job) { _changed.Add(ChunkOf(job.Position)); Version++; }
}
