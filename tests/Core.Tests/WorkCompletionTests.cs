namespace ColonySim.Core.Tests;

public class WorkCompletionTests
{
    [Fact] public void Complete_DigIntoHiddenCave_RemovesDigJobsInsideRevealedCave()
    {
        var m = TestWorlds.FlatMap();
        m.World.SetBlock(20, 100, 20, Blocks.Air, false); m.World.SetBlock(21, 100, 20, Blocks.Air, false);
        var wall = m.Jobs.Add(new JobSpec(new(20, 101, 20), JobKind.Dig, Blocks.Air));
        m.Jobs.Add(new JobSpec(new(21, 100, 20), JobKind.Dig, Blocks.Air));
        m.Jobs.Add(new JobSpec(new(20, 100, 20), JobKind.CarveStairs, Blocks.CarvedStairs(Blocks.Stone, Facing.North)));
        Assert.True(WorkCompletion.Complete(m, wall));
        Assert.Equal(0, m.Jobs.Count); Assert.True(m.World.IsDiscovered(21, 100, 20));
    }
    [Fact] public void Complete_Dig_SetsAirAndRemovesJob()
    {
        var m = TestWorlds.FlatMap(); var j = m.Jobs.Add(new JobSpec(new(5, 150, 5), JobKind.Dig, Blocks.Air));
        Assert.True(WorkCompletion.Complete(m, j)); Assert.Equal(Blocks.Air, m.World.GetBlock(5, 150, 5)); Assert.Equal(0, m.Jobs.Count);
    }
    [Fact] public void Complete_CarveOnOpenCell_JustRemovesJob()
    {
        var m = TestWorlds.FlatMap();
        var j = m.Jobs.Add(new JobSpec(new(5, 151, 5), JobKind.CarveStairs, Blocks.CarvedStairs(Blocks.Stone, Facing.East)));
        Assert.False(WorkCompletion.Complete(m, j)); Assert.Equal(Blocks.Air, m.World.GetBlock(5, 151, 5)); Assert.Equal(0, m.Jobs.Count);
    }
    [Fact] public void Complete_CarveOnSolid_PlacesBlock()
    {
        var m = TestWorlds.FlatMap(); var b = Blocks.CarvedStairs(Blocks.Stone, Facing.East);
        var j = m.Jobs.Add(new JobSpec(new(5, 150, 5), JobKind.CarveStairs, b));
        Assert.True(WorkCompletion.Complete(m, j)); Assert.Equal(b, m.World.GetBlock(5, 150, 5));
    }
    [Fact] public void Complete_BuildOnOccupied_RemovesAndReturnsFalse()
    {
        var m = TestWorlds.FlatMap(); var j = m.Jobs.Add(new JobSpec(new(5, 150, 5), JobKind.BuildWall, Blocks.WoodPlanks));
        Assert.False(WorkCompletion.Complete(m, j)); Assert.Equal(Blocks.Stone, m.World.GetBlock(5, 150, 5)); Assert.Equal(0, m.Jobs.Count);
    }
    [Fact] public void Complete_BuildOnWater_Places()
    {
        var m = TestWorlds.FlatMap(); m.World.SetBlock(5, 151, 5, Blocks.Water);
        var j = m.Jobs.Add(new JobSpec(new(5, 151, 5), JobKind.BuildFloor, Blocks.WoodPlanks));
        Assert.True(WorkCompletion.Complete(m, j)); Assert.Equal(Blocks.WoodPlanks, m.World.GetBlock(5, 151, 5));
    }
}
