namespace ColonySim.Core.Tests;

public class DesignatorTests
{
    static DragBox B(int x0, int y0, int z0, int x1, int y1, int z1) => new(new(x0, y0, z0), new(x1, y1, z1));
    static IReadOnlyList<Job> P(ColonyMap m, DesignationTool t, DragBox b, BuildMaterial mat = BuildMaterial.Wood) => Designator.Preview(m, t, mat, b);

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
            m.Jobs.Add(new Job(p, JobKind.Dig, Blocks.Air));
        Assert.Equal(3, P(m, DesignationTool.Cancel, B(0, 0, 0, 511, 255, 511)).Count);
    }
}
