namespace ColonySim.Core;

/// <summary>
/// Debug "instant work" mode: completes queued jobs without colonists.
/// Order: all digs (incl. stair carving) top-down (highest Y first), then builds bottom-up; ties by X, then Z.
/// A build into a cell a colonist's body is in is skipped and kept until they move.
/// </summary>
public static class InstantWorker
{
    /// <summary>Completes up to <paramref name="maxJobs"/> jobs and returns how many were done (removed from the queue).</summary>
    public static int Step(ColonyMap map, int maxJobs)
    {
        if (maxJobs <= 0 || map.Jobs.Count == 0) return 0;

        // Single pass with a bounded max-heap holding the maxJobs best (smallest-key) jobs.
        var heap = new PriorityQueue<Job, (int Phase, int Y, int X, int Z)>(
            Comparer<(int Phase, int Y, int X, int Z)>.Create((a, b) => b.CompareTo(a)));
        foreach (var job in map.Jobs.All)
        {
            if (IsBuild(job) && WorkCompletion.IsOccupied(map, job.Position)) continue; // waits for the colonist to move
            var key = KeyOf(job);
            if (heap.Count < maxJobs) heap.Enqueue(job, key);
            else if (heap.TryPeek(out _, out var worst) && key.CompareTo(worst) < 0)
                heap.DequeueEnqueue(job, key);
        }

        var batch = new List<(Job Job, (int Phase, int Y, int X, int Z) Key)>(heap.Count);
        while (heap.TryDequeue(out var j, out var k)) batch.Add((j, k));
        batch.Sort((a, b) => a.Key.CompareTo(b.Key));

        int done = 0;
        foreach (var (job, _) in batch)
        {
            // An earlier dig in this batch may have revealed the job's cell and removed it.
            if (!map.Jobs.TryGetById(job.Id, out _)) continue;
            WorkCompletion.Complete(map, job);
            done++;
        }
        return done;
    }

    static bool IsBuild(Job j) => j.Kind is not (JobKind.Dig or JobKind.CarveStairs);

    static (int Phase, int Y, int X, int Z) KeyOf(Job j) =>
        j.Kind is JobKind.Dig or JobKind.CarveStairs
            ? (0, -j.Position.Y, j.Position.X, j.Position.Z)
            : (1, j.Position.Y, j.Position.X, j.Position.Z);
}
