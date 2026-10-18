namespace ColonySim.Core;

/// <summary>
/// Debug "instant work" mode: completes queued jobs without colonists.
/// Order: all digs top-down (highest Y first), then builds bottom-up; ties by X, then Z.
/// </summary>
public static class InstantWorker
{
    /// <summary>Completes up to <paramref name="maxJobs"/> jobs and returns how many were processed.</summary>
    public static int Step(ColonyMap map, int maxJobs)
    {
        if (maxJobs <= 0 || map.Jobs.Count == 0) return 0;

        // Single pass with a bounded max-heap holding the maxJobs best (smallest-key) jobs.
        var heap = new PriorityQueue<Job, (int Phase, int Y, int X, int Z)>(
            Comparer<(int Phase, int Y, int X, int Z)>.Create((a, b) => b.CompareTo(a)));
        foreach (var job in map.Jobs.All)
        {
            var key = KeyOf(job);
            if (heap.Count < maxJobs) heap.Enqueue(job, key);
            else if (heap.TryPeek(out _, out var worst) && key.CompareTo(worst) < 0)
                heap.DequeueEnqueue(job, key);
        }

        var batch = new List<(Job Job, (int Phase, int Y, int X, int Z) Key)>(heap.Count);
        while (heap.TryDequeue(out var j, out var k)) batch.Add((j, k));
        batch.Sort((a, b) => a.Key.CompareTo(b.Key));

        foreach (var (job, _) in batch)
        {
            var p = job.Position;
            if (job.Kind == JobKind.Dig)
            {
                map.World.SetBlock(p.X, p.Y, p.Z, Blocks.Air);
                Discovery.RevealFrom(map.World, p.X, p.Y, p.Z);
            }
            else if (map.World.GetBlock(p.X, p.Y, p.Z) == Blocks.Air)
            {
                map.World.SetBlock(p.X, p.Y, p.Z, job.Block);
            }
            map.Jobs.Remove(p);
        }
        return batch.Count;
    }

    static (int Phase, int Y, int X, int Z) KeyOf(Job j) =>
        j.Kind == JobKind.Dig
            ? (0, -j.Position.Y, j.Position.X, j.Position.Z)
            : (1, j.Position.Y, j.Position.X, j.Position.Z);
}
