namespace ColonySim.Core;

public sealed class JobQueue
{
    readonly Dictionary<Int3, Job> _jobs = new();

    public int Count => _jobs.Count;
    public IEnumerable<Job> All => _jobs.Values;

    /// <summary>Increments on every change.</summary>
    public int Version { get; private set; }

    /// <summary>Replaces any job at the same position.</summary>
    public void Add(Job j) { _jobs[j.Position] = j; Version++; }

    public bool Remove(Int3 p)
    {
        if (!_jobs.Remove(p)) return false;
        Version++;
        return true;
    }

    public bool Contains(Int3 p) => _jobs.ContainsKey(p);
    public bool TryGet(Int3 p, out Job j) => _jobs.TryGetValue(p, out j);
}
