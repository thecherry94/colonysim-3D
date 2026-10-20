namespace ColonySim.Core.Tests;

public class JobQueueTests
{
    [Fact] public void Add_ReplacesAtSamePosition_AndBumpsVersion()
    {
        var q = new JobQueue(); var p = new Int3(1, 2, 3);
        q.Add(new JobSpec(p, JobKind.Dig, Blocks.Air)); var v = q.Version;
        q.Add(new JobSpec(p, JobKind.BuildWall, Blocks.WoodPlanks));
        Assert.Equal(1, q.Count); Assert.True(q.Version > v); Assert.True(q.TryGet(p, out var j)); Assert.Equal(JobKind.BuildWall, j.Kind);
    }
    [Fact] public void Remove_MissingReturnsFalse() => Assert.False(new JobQueue().Remove(new Int3(0, 0, 0)));

    [Fact] public void Add_SameKindAndBlock_KeepsExistingJobAndProgress()
    {
        var q = new JobQueue(); var a = q.Add(new JobSpec(new(1, 2, 3), JobKind.Dig, Blocks.Air)); q.Claim(a, 7); q.AddProgress(a, 12);
        var b = q.Add(new JobSpec(new(1, 2, 3), JobKind.Dig, Blocks.Air));
        Assert.Same(a, b); Assert.Equal(7, b.ClaimedBy); Assert.Equal(12, b.Progress);
    }
    [Fact] public void Add_DifferentKind_ReplacesWithNewIdAndReleases()
    {
        var q = new JobQueue(); var a = q.Add(new JobSpec(new(1, 2, 3), JobKind.Dig, Blocks.Air)); q.Claim(a, 7);
        var b = q.Add(new JobSpec(new(1, 2, 3), JobKind.BuildWall, Blocks.WoodPlanks));
        Assert.NotEqual(a.Id, b.Id); Assert.Equal(0, b.ClaimedBy); Assert.Equal(0, a.ClaimedBy);
        Assert.False(q.TryGetById(a.Id, out _)); Assert.True(q.TryGetById(b.Id, out var f)); Assert.Same(b, f);
    }
    [Fact] public void Claim_Release_Status_AreTracked()
    {
        var q = new JobQueue(); var a = q.Add(new JobSpec(new(1, 2, 3), JobKind.Dig, Blocks.Air));
        q.Claim(a, 4); Assert.Equal(4, a.ClaimedBy); q.Release(a); Assert.Equal(0, a.ClaimedBy);
        q.SetStatus(a, JobStatus.Unreachable, 9); Assert.Equal(JobStatus.Unreachable, a.Status); Assert.Equal(9, a.StatusVersion);
        Assert.Equal(new JobSpec(new(1, 2, 3), JobKind.Dig, Blocks.Air), a.Spec);
    }
    [Fact] public void Remove_DropsFromIdAndChunkIndex()
    {
        var q = new JobQueue(); var a = q.Add(new JobSpec(new(1, 2, 3), JobKind.Dig, Blocks.Air));
        Assert.True(q.Remove(new Int3(1, 2, 3))); Assert.False(q.TryGetById(a.Id, out _)); Assert.Empty(q.InChunk(new Int3(0, 0, 0)));
    }
    [Fact] public void InChunk_ReturnsOnlyThatChunk()
    {
        var q = new JobQueue(); q.Add(new JobSpec(new(1, 2, 3), JobKind.Dig, Blocks.Air)); q.Add(new JobSpec(new(40, 2, 3), JobKind.Dig, Blocks.Air));
        Assert.Single(q.InChunk(new Int3(0, 0, 0))); Assert.Single(q.InChunk(new Int3(1, 0, 0))); Assert.Empty(q.InChunk(new Int3(2, 0, 0)));
    }
    [Fact] public void ByRing_ReturnsNearerChunksFirst()
    {
        var q = new JobQueue();
        q.Add(new JobSpec(new(5 * 32 + 1, 40, 1), JobKind.Dig, Blocks.Air));   // chunk x=5: ring 2 from chunk 3
        q.Add(new JobSpec(new(3 * 32 + 1, 40, 1), JobKind.Dig, Blocks.Air));   // ring 0
        q.Add(new JobSpec(new(4 * 32 + 1, 40, 1), JobKind.Dig, Blocks.Air));   // ring 1
        q.Add(new JobSpec(new(3 * 32 + 1, 5 * 32 + 1, 1), JobKind.Dig, Blocks.Air)); // chunk y=5: ring 4
        var xs = q.ByRing(new Int3(3 * 32 + 5, 40, 5), 2).Select(j => j.Position.X / 32).ToList();
        Assert.Equal(new[] { 3, 4, 5 }, xs);
        Assert.Equal(4, q.ByRing(new Int3(3 * 32 + 5, 40, 5), 4).Count());
    }
    [Fact] public void ConsumeChangedChunks_ReportsTouchedChunksOnce()
    {
        var q = new JobQueue(); var a = q.Add(new JobSpec(new(1, 2, 3), JobKind.Dig, Blocks.Air));
        q.Add(new JobSpec(new(2, 2, 3), JobKind.Dig, Blocks.Air)); q.Add(new JobSpec(new(3, 2, 3), JobKind.Dig, Blocks.Air)); q.Claim(a, 1);
        var got = new List<Int3>(); q.ConsumeChangedChunks(got);
        Assert.Equal(new[] { new Int3(0, 0, 0) }, got);
        got.Clear(); q.ConsumeChangedChunks(got); Assert.Empty(got);
        q.AddProgress(a, 3); q.ConsumeChangedChunks(got); Assert.Empty(got);
        q.Release(a); q.ConsumeChangedChunks(got); Assert.Single(got);
    }

    [Fact] public void ByRing_AllRings_YieldEveryJobOnceInRingOrder()
    {
        var q = new JobQueue();
        var rng = new Random(4);
        for (int i = 0; i < 400; i++) q.Add(new JobSpec(new(rng.Next(512), rng.Next(256), rng.Next(512)), JobKind.Dig, Blocks.Air));
        var from = new Int3(200, 100, 300);
        var c = JobQueue.ChunkOf(from);
        int Ring(Job j) { var d = JobQueue.ChunkOf(j.Position) - c; return Math.Max(Math.Abs(d.X), Math.Max(Math.Abs(d.Y), Math.Abs(d.Z))); }
        var all = q.ByRing(from, q.MaxRing).ToList();
        Assert.Equal(q.Count, all.Distinct().Count());
        Assert.Equal(q.Count, all.Count);
        Assert.Equal(all.Select(Ring).OrderBy(r => r), all.Select(Ring));
        var ready = new List<Job>();
        for (int r = 0; r <= q.MaxRing; r++)
        {
            int before = ready.Count;
            q.ReadyInRing(from, r, [JobKind.Dig], ready);
            Assert.All(ready.Skip(before), j => Assert.Equal(r, Ring(j)));
        }
    }

    [Fact] public void Dig_WaitsForDigsOneUpInTheNeighbouringColumns()
    {
        // So no column of a dig runs more than one level ahead of its neighbours, leaving a pillar no one can reach.
        var q = new JobQueue();
        var low = q.Add(new JobSpec(new(10, 20, 10), JobKind.Dig, Blocks.Air));
        var side = q.Add(new JobSpec(new(11, 21, 9), JobKind.CarveStairs, Blocks.Stone));
        Assert.True(side.WaitingFor == 0);
        Assert.Equal(1, low.WaitingFor);
        var far = q.Add(new JobSpec(new(12, 21, 10), JobKind.Dig, Blocks.Air)); // two columns over: no wait
        var level = q.Add(new JobSpec(new(9, 20, 10), JobKind.Dig, Blocks.Air)); // same level: no wait
        Assert.Equal((1, 0, 0), (low.WaitingFor, far.WaitingFor, level.WaitingFor));
        q.Remove(side.Position);
        Assert.Equal(0, low.WaitingFor);
        Assert.Equal(3, q.ReadyCount);
    }

    [Fact] public void Readiness_DigsTopDown_BuildsBottomUp_TrackedThroughChanges()
    {
        // Random adds, replacements and removals in a small box; after each, readiness must match the rule worked out
        // from scratch, and the ready lists must hold exactly the ready jobs.
        var q = new JobQueue();
        var rng = new Random(9);
        var kinds = new[] { JobKind.Dig, JobKind.CarveStairs, JobKind.BuildWall, JobKind.BuildFloor };
        for (int step = 0; step < 3000; step++)
        {
            var p = new Int3(30 + rng.Next(4), 30 + rng.Next(8), 30 + rng.Next(4)); // across a chunk border in x, y and z
            if (rng.Next(3) == 0) q.Remove(p);
            else q.Add(new JobSpec(p, kinds[rng.Next(kinds.Length)], (ushort)rng.Next(1, 3)));
            if (step % 50 != 0) continue;

            bool IsDig(JobKind k) => k is JobKind.Dig or JobKind.CarveStairs;
            bool Ready(Job j)
            {
                var o = j.Position;
                if (IsDig(j.Kind))
                {
                    bool DigAt(Int3 c) => q.TryGet(c, out var a) && IsDig(a.Kind);
                    if (Enumerable.Range(1, 3).Any(k => DigAt(o + new Int3(0, k, 0)))) return false;
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                            if ((dx != 0 || dz != 0) && DigAt(o + new Int3(dx, 1, dz))) return false;
                    return true;
                }
                for (int k = 1; k <= 4; k++)
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                            if (q.TryGet(o + new Int3(dx, -k, dz), out var b) && !IsDig(b.Kind)) return false;
                return true;
            }
            var expected = q.All.Where(Ready).ToHashSet();
            var listed = new List<Job>();
            for (int r = 0; r <= q.MaxRing; r++) q.ReadyInRing(new Int3(31, 33, 31), r, Enum.GetValues<JobKind>(), listed);
            Assert.Equal(expected.Count, q.ReadyCount);
            Assert.All(kinds, k => Assert.Equal(expected.Count(j => j.Kind == k), q.ReadyOfKind(k)));
            Assert.Equal(expected.Count, listed.Count);
            Assert.True(expected.SetEquals(listed));
            Assert.All(q.All, j => Assert.Equal(expected.Contains(j), WorkRules.IsReady(null!, j)));
        }
    }
}
