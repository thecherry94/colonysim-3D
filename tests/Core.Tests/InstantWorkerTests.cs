namespace ColonySim.Core.Tests;

public class InstantWorkerTests
{
    static JobSpec Dig(int x, int y, int z) => new(new(x, y, z), JobKind.Dig, Blocks.Air);
    static JobSpec Wall(int x, int y, int z) => new(new(x, y, z), JobKind.BuildWall, Blocks.WoodPlanks);

    [Fact] public void DigsTopDown_ThenBuildsBottomUp()
    {
        var m = TestWorlds.FlatMap();
        m.Jobs.Add(Wall(1, 152, 1)); m.Jobs.Add(Wall(1, 151, 1)); m.Jobs.Add(Dig(5, 148, 5)); m.Jobs.Add(Dig(5, 150, 5)); m.Jobs.Add(Dig(5, 149, 5));
        InstantWorker.Step(m, 1); Assert.Equal(Blocks.Air, m.World.GetBlock(5, 150, 5)); Assert.Equal(Blocks.Stone, m.World.GetBlock(5, 149, 5));
        InstantWorker.Step(m, 2); Assert.Equal(Blocks.Air, m.World.GetBlock(5, 148, 5)); Assert.Equal(Blocks.Air, m.World.GetBlock(1, 151, 1));
        InstantWorker.Step(m, 1); Assert.Equal(Blocks.WoodPlanks, m.World.GetBlock(1, 151, 1));
        Assert.Equal(1, InstantWorker.Step(m, 5)); Assert.Equal(0, m.Jobs.Count);
    }
    [Fact] public void DigIntoHiddenCave_RevealsIt()
    {
        var m = TestWorlds.FlatMap(); m.World.SetBlock(20, 100, 20, Blocks.Air, false); m.World.SetBlock(21, 100, 20, Blocks.Air, false);
        m.Jobs.Add(Dig(20, 101, 20)); InstantWorker.Step(m, 1);
        Assert.True(m.World.IsDiscovered(21, 100, 20));
    }
    [Fact] public void Step_BuildOnOccupiedCell_IsDiscarded() { var m = TestWorlds.FlatMap(); m.Jobs.Add(Wall(3, 150, 3)); Assert.Equal(1, InstantWorker.Step(m, 1)); Assert.Equal(Blocks.Stone, m.World.GetBlock(3, 150, 3)); Assert.Equal(0, m.Jobs.Count); }
    [Fact] public void Step_DigOnAirCell_CompletesAndReveals()
    {
        var m = TestWorlds.FlatMap(); m.World.SetBlock(9, 90, 9, Blocks.Air, false);
        m.Jobs.Add(Dig(9, 90, 9)); InstantWorker.Step(m, 1); Assert.True(m.World.IsDiscovered(9, 90, 9));
    }

    [Fact] public void ClaimedJobs_AreCompletedToo()
    {
        var m = TestWorlds.FlatMap(); var j = m.Jobs.Add(Dig(5, 150, 5)); m.Jobs.Claim(j, 3);
        InstantWorker.Step(m, 1); Assert.Equal(Blocks.Air, m.World.GetBlock(5, 150, 5)); Assert.Equal(0, m.Jobs.Count);
    }
    [Fact] public void CarveStairs_CountsAsDig_AndPlacesBlock()
    {
        var m = TestWorlds.FlatMap(); var b = Blocks.CarvedStairs(Blocks.Stone, Facing.West);
        m.Jobs.Add(Wall(1, 151, 1)); m.Jobs.Add(new JobSpec(new(5, 150, 5), JobKind.CarveStairs, b));
        InstantWorker.Step(m, 1); Assert.Equal(b, m.World.GetBlock(5, 150, 5)); Assert.Equal(1, m.Jobs.Count);
    }
}
