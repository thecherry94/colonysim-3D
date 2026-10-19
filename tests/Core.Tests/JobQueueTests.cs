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
}
