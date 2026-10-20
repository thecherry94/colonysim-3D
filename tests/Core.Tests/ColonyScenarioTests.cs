namespace ColonySim.Core.Tests;

/// <summary>
/// Whole jobs of work, done by colonists from start to end: everything designated gets done, nobody ends up stuck and
/// nobody falls. Flat stone to y = 150 (standing y 151), home at (32, 151, 32).
/// </summary>
public class ColonyScenarioTests
{
    const int Surface = 150;
    static readonly Int3 Home = new(32, Surface + 1, 32);

    // The pit: 12 × 12 just east of home, 8 levels deep, entered by a staircase dug down from the west along z = 30.
    const int PitX = 36, PitZ = 26, PitSize = 12, PitDepth = 8;

    /// <summary>A colony of <paramref name="colonists"/> at home on flat stone, the world first shaped by <paramref name="shape"/> (unrevealed).</summary>
    static Simulation Colony(int seed, int colonists, Action<VoxelWorld>? shape = null)
    {
        var world = TestWorlds.SolidBelow(64, Surface);
        shape?.Invoke(world);
        Discovery.InitialFlood(world);
        var map = new ColonyMap(new MapSettings(seed, 64, 64, new TerrainParams()), world, site: new StartSite(Home.X, Home.Z, Home.Y));
        var sim = new Simulation(map);
        for (int i = 0; i < colonists; i++) sim.Spawn(Home);
        return sim;
    }

    /// <summary>The pit's dig and its entrance staircase, as the player would drag them.</summary>
    static void DesignatePit(ColonyMap map)
    {
        Designator.Apply(map, DesignationTool.Dig, BuildMaterial.Wood, new DragBox(
            new(PitX, Surface, PitZ), new(PitX + PitSize - 1, Surface - PitDepth + 1, PitZ + PitSize - 1)));
        Designator.Apply(map, DesignationTool.DigStairs, BuildMaterial.Wood, new DragBox(
            new(PitX - PitDepth, Surface, PitZ + 4), new(PitX - 1, Surface, PitZ + 4)));
    }

    /// <summary>Ticks until no job is left (or <paramref name="maxTicks"/>); returns the longest fall anyone took, in blocks.</summary>
    static int RunToEnd(Simulation sim, int maxTicks)
    {
        int longestFall = 0;
        var falling = new HashSet<Colonist>();
        for (int t = 0; t < maxTicks && sim.Map.Jobs.Count > 0; t++)
        {
            sim.Tick();
            foreach (var c in sim.Colony.Colonists)
            {
                if (c.Motion is { Kind: MoveKind.Fall } m && falling.Add(c)) longestFall = Math.Max(longestFall, m.From.Y - m.To.Y);
                else if (c.Motion is not { Kind: MoveKind.Fall }) falling.Remove(c);
            }
        }
        return longestFall;
    }

    static string Left(ColonyMap map) =>
        string.Join("; ", map.Jobs.All.OrderByDescending(j => j.Position.Y).Take(12).Select(j => $"{j.Kind} {j.Position} {j.Status}"));

    static void AssertNobodyStuck(Simulation sim) =>
        Assert.All(sim.Colony.Colonists, c => Assert.False(c.Stuck, $"{c.Name} stuck at {c.Cell}"));

    [Theory, InlineData(1), InlineData(2), InlineData(3)]
    public void Pit_12x12x8_WithEntryStairs_DigsOut_NobodyStuck(int seed)
    {
        var sim = Colony(seed, 3);
        var map = sim.Map;
        DesignatePit(map);
        int longestFall = RunToEnd(sim, 100_000); // about 83 minutes at 1×; it takes about half that
        Assert.True(map.Jobs.Count == 0, $"{map.Jobs.Count} jobs left: {Left(map)}");
        AssertNobodyStuck(sim);
        Assert.True(longestFall <= 1, $"someone fell {longestFall}");
        for (int y = Surface; y > Surface - PitDepth; y--) Assert.Equal(Blocks.Air, map.World.GetBlock(PitX + 5, y, PitZ + 5));
    }

    [Theory, InlineData(1), InlineData(2)]
    public void PitOverHiddenCave_NobodyFallsIn(int seed)
    {
        // A cave nobody knows of under the pit's middle 8 × 8, its floor 9 below the pit's: digging the pit breaks into
        // it. Fog reads as solid, so whoever stood on the last block over it when it was dug would drop 13.
        const int caveFloor = Surface - PitDepth - 9;
        var sim = Colony(seed, 3, w =>
        {
            for (int y = caveFloor + 1; y <= Surface - 5; y++)
                for (int z = PitZ + 2; z < PitZ + PitSize - 2; z++)
                    for (int x = PitX + 2; x < PitX + PitSize - 2; x++) w.SetBlock(x, y, z, Blocks.Air, markDirty: false);
        });
        var map = sim.Map;
        Assert.Equal(Blocks.Stone, map.View.GetKnownBlock(PitX + 5, Surface - 5, PitZ + 5)); // hidden
        DesignatePit(map);
        int longestFall = RunToEnd(sim, 100_000);
        Assert.Equal(Blocks.Air, map.View.GetKnownBlock(PitX + 5, Surface - 5, PitZ + 5)); // broken into
        Assert.True(longestFall <= 1, $"someone fell {longestFall}");
        AssertNobodyStuck(sim);
        // Everything is dug that anyone can reach. Blocks of the last layer over the cave whose neighbours all opened
        // into it first are left standing alone over a 9-deep drop: nowhere within reach to dig them from can be got to.
        sim.Tick();
        Assert.All(map.Jobs.All, j =>
        {
            Assert.Equal((JobKind.Dig, Surface - 4), (j.Kind, j.Position.Y));
            Assert.DoesNotContain(WorkRules.WorkSpots(map, j), s => map.Nav.ComponentOf(s) == sim.HomeComponent);
        });
        Assert.True(map.Jobs.Count <= 12, $"{map.Jobs.Count} jobs left: {Left(map)}");
    }

    [Theory, InlineData(1), InlineData(2), InlineData(3)]
    public void WallsAndFloor_BuiltByColonists(int seed)
    {
        // A 5 × 5 room, walls three high with a doorway (two high, under a lintel) on the north side, and a plank floor
        // over it at the walls' top: a ceiling, built from inside the room.
        var sim = Colony(seed, 3);
        var map = sim.Map;
        int x0 = 40, z0 = 40, top = Surface + 3;
        Designator.Apply(map, DesignationTool.Wall, BuildMaterial.Wood, new DragBox(new(x0, Surface + 1, z0), new(x0 + 4, top, z0 + 4)));
        Designator.Apply(map, DesignationTool.Cancel, BuildMaterial.Wood, new DragBox(new(x0 + 2, Surface + 1, z0), new(x0 + 2, Surface + 2, z0)));
        Designator.Apply(map, DesignationTool.Floor, BuildMaterial.Wood, new DragBox(new(x0 + 1, top, z0 + 1), new(x0 + 3, top, z0 + 3)));
        Assert.Equal(16 * 3 - 2 + 9, map.Jobs.Count);
        int longestFall = RunToEnd(sim, 20_000);
        Assert.True(map.Jobs.Count == 0, $"{map.Jobs.Count} jobs left: {Left(map)}");
        AssertNobodyStuck(sim);
        Assert.True(longestFall <= 1, $"someone fell {longestFall}");
        Assert.Equal(Blocks.WoodPlanks, map.World.GetBlock(x0, top, z0 + 4));
        Assert.Equal(Blocks.WoodPlanks, map.World.GetBlock(x0 + 2, top, z0 + 2));
        Assert.Equal(Blocks.Air, map.World.GetBlock(x0 + 2, Surface + 1, z0));
        // Everyone outside, or able to get out through the doorway.
        Assert.All(sim.Colony.Colonists, c => Assert.Equal(sim.HomeComponent, map.Nav.ComponentOf(c.Cell)));
    }
}
