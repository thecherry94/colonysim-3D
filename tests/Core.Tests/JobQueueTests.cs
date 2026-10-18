namespace ColonySim.Core.Tests;

public class JobQueueTests
{
    [Fact] public void Add_ReplacesAtSamePosition_AndBumpsVersion()
    {
        var q = new JobQueue(); var p = new Int3(1, 2, 3);
        q.Add(new Job(p, JobKind.Dig, Blocks.Air)); var v = q.Version;
        q.Add(new Job(p, JobKind.BuildWall, Blocks.WoodPlanks));
        Assert.Equal(1, q.Count); Assert.True(q.Version > v); Assert.True(q.TryGet(p, out var j)); Assert.Equal(JobKind.BuildWall, j.Kind);
    }
    [Fact] public void Remove_MissingReturnsFalse() => Assert.False(new JobQueue().Remove(new Int3(0, 0, 0)));
}
