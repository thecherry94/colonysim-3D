namespace ColonySim.Core.Tests;

public class DesignatorTests
{
    static DragBox B(int x0, int y0, int z0, int x1, int y1, int z1) => new(new(x0, y0, z0), new(x1, y1, z1));
    static IReadOnlyList<JobSpec> P(ColonyMap m, DesignationTool t, DragBox b, BuildMaterial mat = BuildMaterial.Wood) => Designator.Preview(m, t, mat, b);

    [Fact] public void Dig_Volume() { var j = P(TestWorlds.FlatMap(), DesignationTool.Dig, B(10, 150, 10, 12, 148, 12)); Assert.Equal(27, j.Count); Assert.All(j, x => Assert.Equal(JobKind.Dig, x.Kind)); }
    [Fact] public void Dig_SkipsAirAndBedrock() { var m = TestWorlds.FlatMap(); Assert.Empty(P(m, DesignationTool.Dig, B(0, 160, 0, 5, 160, 5))); Assert.All(P(m, DesignationTool.Dig, B(0, 0, 0, 1, 1, 1)), j => Assert.Equal(1, j.Position.Y)); }
    [Fact] public void Dig_HiddenCaveCellsCountAsRock() { var m = TestWorlds.FlatMap(); m.World.SetBlock(30, 100, 30, Blocks.Air, false); Assert.Single(P(m, DesignationTool.Dig, B(30, 100, 30, 30, 100, 30))); }
    [Fact] public void Wall_IsOutline() { var j = P(TestWorlds.FlatMap(), DesignationTool.Wall, B(10, 151, 10, 14, 151, 13)); Assert.Equal(14, j.Count); Assert.All(j, x => Assert.Equal(Blocks.WoodPlanks, x.Block)); }
    [Fact] public void Wall_RepeatsPerLevel() => Assert.Equal(42, P(TestWorlds.FlatMap(), DesignationTool.Wall, B(10, 151, 10, 14, 153, 13)).Count);
    [Fact] public void Floor_Fills() { var j = P(TestWorlds.FlatMap(), DesignationTool.Floor, B(10, 151, 10, 14, 151, 13), BuildMaterial.Stone); Assert.Equal(20, j.Count); Assert.All(j, x => Assert.Equal(Blocks.StoneBricks, x.Block)); }
    [Fact] public void Build_SkipsOccupied() => Assert.Empty(P(TestWorlds.FlatMap(), DesignationTool.Floor, B(10, 150, 10, 14, 150, 13)));
    [Fact] public void Stairs_RiseAlongDominantAxis()
    {
        var j = P(TestWorlds.FlatMap(), DesignationTool.Stairs, B(10, 151, 10, 14, 151, 11));
        Assert.Equal(10, j.Count);
        Assert.Contains(j, x => x.Position == new Int3(14, 155, 11) && x.Block == Blocks.Stairs(BuildMaterial.Wood, Facing.East));
    }
    [Fact] public void Stairs_NegativeX_FacesWest() => Assert.All(P(TestWorlds.FlatMap(), DesignationTool.Stairs, B(14, 151, 10, 10, 151, 10)), x => Assert.Equal(Blocks.Stairs(BuildMaterial.Wood, Facing.West), x.Block));
    [Fact] public void Stairs_SingleCell_FacesSouth() => Assert.Equal(Blocks.Stairs(BuildMaterial.Wood, Facing.South), Assert.Single(P(TestWorlds.FlatMap(), DesignationTool.Stairs, B(5, 151, 5, 5, 151, 5))).Block);
    [Fact] public void Apply_QueuesThenCancelRemoves()
    {
        var m = TestWorlds.FlatMap(); var box = B(10, 150, 10, 12, 150, 12);
        Assert.Equal(9, Designator.Apply(m, DesignationTool.Dig, BuildMaterial.Wood, box)); Assert.Equal(9, m.Jobs.Count);
        Assert.Equal(4, Designator.Apply(m, DesignationTool.Cancel, BuildMaterial.Wood, B(10, 150, 10, 11, 150, 11))); Assert.Equal(5, m.Jobs.Count);
    }
    [Fact] public void Box_OutsideMapBounds_IsClipped()
    {
        var j = P(TestWorlds.FlatMap(), DesignationTool.Dig, B(-5, 148, -5, 2, 152, 2));
        Assert.Equal(27, j.Count); Assert.All(j, x => Assert.True(x.Position.X >= 0 && x.Position.Z >= 0));
    }
    [Fact] public void HugeBox_IsCappedAtMaxCellsPerDrag()
    {
        var m = new ColonyMap(MapSettings.Create(1, MapSize.Large), TestWorlds.SolidBelow(512));
        Assert.Equal(Designator.MaxCellsPerDrag, P(m, DesignationTool.Dig, B(0, 150, 0, 511, 100, 511)).Count);
    }

    [Fact] public void Wall_PastMapEdge_UsesUnclippedOutline()
    {
        // Outline edges at x=-3 and z=10/13 are partly off-map: only z rows 10 and 13 (x 0..4) and the x=4 column survive.
        var j = P(TestWorlds.FlatMap(), DesignationTool.Wall, B(-3, 151, 10, 4, 151, 13));
        Assert.Equal(12, j.Count);
        Assert.DoesNotContain(j, x => x.Position.X == 0 && x.Position.Z is not (10 or 13));
    }
    [Fact] public void Cancel_OverHugeBox_ReturnsOnlyQueuedJobs()
    {
        var m = new ColonyMap(MapSettings.Create(1, MapSize.Large), TestWorlds.SolidBelow(512));
        foreach (var p in new[] { new Int3(5, 100, 5), new Int3(300, 50, 400), new Int3(511, 255, 511) })
            m.Jobs.Add(new JobSpec(p, JobKind.Dig, Blocks.Air));
        Assert.Equal(3, P(m, DesignationTool.Cancel, B(0, 0, 0, 511, 255, 511)).Count);
    }

    [Fact] public void DigStairs_DescendsAlongDrag_WithHeadroom()
    {
        var jobs = P(TestWorlds.FlatMap(), DesignationTool.DigStairs, B(10, 150, 10, 14, 150, 10));
        var carves = jobs.Where(j => j.Kind == JobKind.CarveStairs).Select(j => j.Position).ToList();
        Assert.Equal(Enumerable.Range(0, 5).Select(i => new Int3(10 + i, 150 - i, 10)), carves);
        Assert.All(jobs.Where(j => j.Kind == JobKind.CarveStairs), j => Assert.Equal(Blocks.CarvedStairs(Blocks.Stone, Facing.West), j.Block));
        Assert.Contains(new JobSpec(new(12, 149, 10), JobKind.Dig, Blocks.Air), jobs);
        Assert.Contains(new JobSpec(new(12, 150, 10), JobKind.Dig, Blocks.Air), jobs);
        Assert.DoesNotContain(jobs, j => j.Position == new Int3(10, 151, 10));
    }
    [Fact] public void DigStairs_StopsAtBedrockLevel()
    {
        var jobs = P(TestWorlds.FlatMap(), DesignationTool.DigStairs, B(10, 3, 10, 14, 3, 10));
        Assert.Equal(3, jobs.Count(j => j.Kind == JobKind.CarveStairs));   // y = 3, 2, 1
    }
    [Fact] public void Ramp_RisesOneLevelPerStep_FacingDragDirection()
    {
        var j = P(TestWorlds.FlatMap(), DesignationTool.Ramp, B(10, 151, 10, 14, 151, 10));
        Assert.Equal(5, j.Count); Assert.All(j, x => Assert.Equal(JobKind.BuildRamp, x.Kind));
        Assert.Contains(new JobSpec(new(14, 155, 10), JobKind.BuildRamp, Blocks.BuiltRamp(BuildMaterial.Wood, Facing.East)), j);
    }
    [Fact] public void Ladder_FacesAdjacentWall()
    {
        var m = TestWorlds.FlatMap();
        for (int y = 148; y <= 150; y++) m.World.SetBlock(20, y, 20, Blocks.Air);
        Discovery.RevealFrom(m.World, 20, 150, 20);
        var j = P(m, DesignationTool.Ladder, B(20, 148, 20, 20, 150, 20));
        Assert.Equal(3, j.Count); Assert.All(j, x => Assert.Equal(JobKind.BuildLadder, x.Kind));
        Assert.Equal(Blocks.Ladder(Facing.North), j.First(x => x.Position.Y == 148).Block);   // North checked first, solid there
        m.World.SetBlock(20, 149, 19, Blocks.Air); m.World.SetBlock(20, 149, 21, Blocks.Air); m.World.SetBlock(19, 149, 20, Blocks.Air);
        Discovery.RevealFrom(m.World, 20, 149, 19); Discovery.RevealFrom(m.World, 20, 149, 21); Discovery.RevealFrom(m.World, 19, 149, 20);
        Assert.Equal(Blocks.Ladder(Facing.East), P(m, DesignationTool.Ladder, B(20, 149, 20, 20, 149, 20)).Single().Block);
    }
    [Fact] public void Ladder_NoWall_FacesSouth()
    {
        var j = P(TestWorlds.FlatMap(), DesignationTool.Ladder, B(5, 152, 5, 5, 152, 5));
        Assert.Equal(Blocks.Ladder(Facing.South), Assert.Single(j).Block);
    }
}
