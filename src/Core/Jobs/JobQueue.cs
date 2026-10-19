namespace ColonySim.Core;

/// <summary>Pending jobs keyed by cell, with ids, claims, progress and a per-chunk spatial index.</summary>
public sealed class JobQueue
{
    const int ChunkShift = 5;

    readonly Dictionary<Int3, Job> _jobs = new();
    readonly Dictionary<long, Job> _byId = new();
    readonly Dictionary<Int3, List<Job>> _chunks = new();
    readonly HashSet<Int3> _changed = new();

    public int Count => _jobs.Count;
    public IEnumerable<Job> All => _jobs.Values;

    /// <summary>Increments on every change.</summary>
    public int Version { get; private set; }

    /// <summary>Next id handed out; saves restore it so ids never repeat.</summary>
    public long NextId { get; internal set; } = 1;

    /// <summary>Chunk counts per axis of the map, bounding <see cref="ByRing"/>. <see cref="ColonyMap"/> sets it.</summary>
    public Int3 ChunkCounts { get; set; } = new(16, 8, 16);

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
    public void Release(Job j) { j.ClaimedBy = 0; Touch(j); }

    /// <summary>Progress is not shown by the overlay, so this neither bumps the version nor marks the chunk.</summary>
    public void AddProgress(Job j, float ticks) => j.Progress += ticks;

    public void SetStatus(Job j, JobStatus s, int navVersion) { j.Status = s; j.StatusVersion = navVersion; Touch(j); }

    public IEnumerable<Job> InChunk(Int3 chunk) =>
        _chunks.TryGetValue(chunk, out var list) ? list : Enumerable.Empty<Job>();

    /// <summary>
    /// Jobs by chunk ring: ring r holds the chunks at Chebyshev distance exactly r (in x, y and z) from the cell's
    /// chunk, clamped to the map. Order within a ring is unspecified.
    /// </summary>
    public IEnumerable<Job> ByRing(Int3 cell, int maxRing)
    {
        if (_jobs.Count == 0) yield break;
        var c = ChunkOf(cell);
        var n = ChunkCounts;
        int limit = Math.Min(maxRing, Math.Max(n.X, Math.Max(n.Y, n.Z)));
        for (int r = 0; r <= limit; r++)
        {
            int x0 = Math.Max(0, c.X - r), x1 = Math.Min(n.X - 1, c.X + r);
            int y0 = Math.Max(0, c.Y - r), y1 = Math.Min(n.Y - 1, c.Y + r);
            int z0 = Math.Max(0, c.Z - r), z1 = Math.Min(n.Z - 1, c.Z + r);
            for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++)
                {
                    bool edge = Math.Abs(y - c.Y) == r || Math.Abs(z - c.Z) == r;
                    for (int x = x0; x <= x1; x++)
                    {
                        if (!edge && Math.Abs(x - c.X) != r) continue; // inside the shell only the two x faces count
                        if (_chunks.TryGetValue(new Int3(x, y, z), out var list))
                            for (int i = 0; i < list.Count; i++) yield return list[i];
                    }
                }
        }
    }

    /// <summary>Appends the chunks whose jobs were added, removed, claimed, released or re-statused since the last call.</summary>
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
        job.ClaimedBy = 0;
    }

    void Touch(Job job) { _changed.Add(ChunkOf(job.Position)); Version++; }
}
