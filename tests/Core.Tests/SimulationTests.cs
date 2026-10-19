using System.Diagnostics;
using System.Reflection;
using Xunit.Abstractions;

namespace ColonySim.Core.Tests;

public class SimulationTests
{
    // Flat map: stone to y = 150, so the standing (feet) y is 151; home is the middle column.

    static bool IsSolid(ColonyMap m, Int3 c) => Blocks.Registry.Get(m.World.GetBlock(c.X, c.Y, c.Z)).IsSolid;

    static void Fill(ColonyMap m, int x0, int x1, int y0, int y1, int z0, int z1, ushort block)
    {
        for (int y = y0; y <= y1; y++)
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++) m.World.SetBlock(x, y, z, block);
    }

    [Fact] public void Generated_Small_HasThreeColonistsAtStartSite()
    {
        var stages = new List<string>();
        var map = MapGenerator.Generate(MapSettings.Create(3, MapSize.Small), new SyncProgress<GenerationProgress>(p => stages.Add(p.Stage)));
        Assert.Contains("Arriving", stages);
        var site = map.StartSite;
        var home = new Int3(site.X, site.Level, site.Z);
        Assert.Equal(home, map.Colony.Home);
        var colonists = map.Colony.Colonists;
        Assert.Equal(3, colonists.Count);
        Assert.Equal(3, colonists.Select(c => c.Cell).Distinct().Count());
        Assert.Equal(3, colonists.Select(c => c.Name).Distinct().Count());
        Assert.Equal(4, map.Colony.NextColonistId);
        foreach (var c in colonists)
        {
            Assert.True(map.Nav.Rules.IsStandable(c.Cell), $"{c.Name} at {c.Cell}");
            Int3 d = c.Cell - home;
            Assert.True(Math.Max(Math.Abs(d.X), Math.Max(Math.Abs(d.Y), Math.Abs(d.Z))) <= 6, $"{c.Name} at {c.Cell}, home {home}");
        }
        var sim = new Simulation(map);
        Assert.All(colonists, c => Assert.False(c.Stuck, c.Name));
        Assert.Equal(sim.HomeComponent, map.Nav.ComponentOf(colonists[0].Cell));
    }

    [Fact] public void GoTo_WalksThereInExpectedTicks()
    {
        var map = TestWorlds.FlatMap();
        var sim = new Simulation(map);
        var c = sim.Spawn(new Int3(5, 151, 10));
        Assert.Equal(new Int3(5, 151, 10), c.Cell);
        Assert.Same(c, map.Colony.Colonists.Single());
        var target = new Int3(15, 151, 10);
        sim.GoTo(c, target);
        Assert.Equal(ColonistActivity.Walking, c.Activity);

        // One tick to find the path, then 10 walks of 6 ticks.
        for (int t = 0; t < 60; t++) sim.Tick();
        Assert.NotEqual(target, c.Cell);
        Assert.Equal(ColonistActivity.Walking, c.Activity);
        Assert.Equal(new Motion(new Int3(14, 151, 10), target, MoveKind.Walk, 6, 5), c.Motion);
        sim.Tick();
        Assert.Equal(target, c.Cell);
        Assert.Null(c.Motion);
        Assert.Equal(ColonistActivity.Idle, c.Activity);
        Assert.Equal(61, map.Colony.Tick);
    }

    [Fact] public void GoTo_Unreachable_GoesIdle()
    {
        var map = TestWorlds.FlatMap();
        var sim = new Simulation(map);
        var c = sim.Spawn(new Int3(5, 151, 10));
        sim.GoTo(c, new Int3(15, 140, 10)); // inside the rock
        for (int t = 0; t < 3; t++) sim.Tick();
        Assert.Equal((new Int3(5, 151, 10), ColonistActivity.Idle), (c.Cell, c.Activity));
    }

    [Fact] public void GroundRemovedUnderColonist_Falls()
    {
        var map = TestWorlds.FlatMap();
        var sim = new Simulation(map);
        var c = sim.Spawn(new Int3(20, 151, 20));
        for (int y = 150; y >= 148; y--) TestWorlds.Dig(map, 20, y, 20);

        // 3 blocks at 2 ticks each, after the tick that starts the fall.
        for (int t = 0; t < 6; t++)
        {
            sim.Tick();
            Assert.Equal(ColonistActivity.Falling, c.Activity);
            Assert.Equal(MoveKind.Fall, c.Motion?.Kind);
        }
        sim.Tick();
        Assert.Equal(new Int3(20, 148, 20), c.Cell);
        Assert.Null(c.Motion);
        Assert.Equal(ColonistActivity.Idle, c.Activity);
        Assert.False(c.Stuck); // it can climb out
    }

    [Fact] public void FallingWithNowhereToLand_IsStuck()
    {
        var map = TestWorlds.FlatMap();
        var sim = new Simulation(map);
        var c = sim.Spawn(new Int3(20, 151, 20));
        map.World.SetBlock(20, 152, 20, Blocks.Stone); // over its feet: no longer standable, and solid below
        sim.Tick();
        Assert.Equal((new Int3(20, 151, 20), ColonistActivity.Idle, true), (c.Cell, c.Activity, c.Stuck));
        Assert.Null(c.Motion);
    }

    [Fact] public void WallBuiltOnPath_ColonistRepaths()
    {
        var map = TestWorlds.FlatMap();
        var sim = new Simulation(map);
        var c = sim.Spawn(new Int3(5, 151, 30));
        var target = new Int3(40, 151, 30);
        sim.GoTo(c, target);
        for (int t = 0; t < 20; t++) sim.Tick();
        Assert.True(c.Cell.X is > 5 and < 20, $"{c.Cell}");

        // Four high, so it can't be climbed: around it, either end.
        Fill(map, 20, 20, 151, 154, 22, 38, Blocks.Stone);
        int ticks = 0;
        while (!(c.Cell == target && c.Activity == ColonistActivity.Idle) && ticks++ < 2000)
        {
            sim.Tick();
            Assert.False(IsSolid(map, c.Cell) || IsSolid(map, c.Cell + Int3.Up), $"inside a solid cell at {c.Cell}");
        }
        Assert.Equal(target, c.Cell);
        Assert.Equal(ColonistActivity.Idle, c.Activity);
    }

    [Fact] public void WallBuiltOnMotionTarget_StepsBack()
    {
        var map = TestWorlds.FlatMap();
        var sim = new Simulation(map);
        var c = sim.Spawn(new Int3(5, 151, 30));
        var target = new Int3(15, 151, 30);
        sim.GoTo(c, target);
        for (int t = 0; t < 3; t++) sim.Tick();
        Assert.Equal(new Int3(6, 151, 30), c.Motion?.To);
        Fill(map, 6, 6, 151, 152, 30, 30, Blocks.Stone);
        sim.Tick();
        Assert.Equal(new Int3(5, 151, 30), c.Cell);
        for (int t = 0; t < 200 && c.Activity != ColonistActivity.Idle; t++)
        {
            sim.Tick();
            Assert.False(IsSolid(map, c.Cell) || IsSolid(map, c.Cell + Int3.Up), $"inside a solid cell at {c.Cell}");
        }
        Assert.Equal(target, c.Cell);
    }

    [Fact] public void SealedPocket_IsStuck()
    {
        var map = TestWorlds.FlatMap();
        var sim = new Simulation(map);
        // A pocket dug in the rock, out of reach of the surface.
        TestWorlds.Dig(map, 10, 140, 10);
        TestWorlds.Dig(map, 10, 141, 10);
        var pocket = sim.Spawn(new Int3(10, 140, 10));
        var free = sim.Spawn(new Int3(40, 151, 40));
        var walled = sim.Spawn(new Int3(20, 151, 20));
        Assert.Equal(new Int3(10, 140, 10), pocket.Cell);
        Assert.True(pocket.Stuck);
        Assert.False(free.Stuck);
        Assert.False(walled.Stuck);

        // A ring four high around the third: stuck once navigation catches up.
        Fill(map, 19, 21, 151, 154, 19, 21, Blocks.Stone);
        Fill(map, 20, 20, 151, 154, 20, 20, Blocks.Air);
        sim.Tick();
        Assert.True(walled.Stuck);
        Assert.True(pocket.Stuck);
        Assert.False(free.Stuck);

        // Opened again.
        Fill(map, 21, 21, 151, 152, 20, 20, Blocks.Air);
        sim.Tick();
        Assert.False(walled.Stuck);
    }

    [Fact] public void Walk_ReportsArrivedOrGaveUp()
    {
        var map = TestWorlds.FlatMap();
        var sim = new Simulation(map);
        var c = sim.Spawn(new Int3(5, 151, 10));
        sim.GoTo(c, new Int3(8, 151, 10));
        Assert.Equal(WalkResult.None, c.LastWalk);
        for (int t = 0; t < 40; t++) sim.Tick();
        Assert.Equal((new Int3(8, 151, 10), WalkResult.Arrived), (c.Cell, c.LastWalk));
        sim.GoTo(c, new Int3(15, 140, 10)); // inside the rock
        for (int t = 0; t < 3; t++) sim.Tick();
        Assert.Equal(WalkResult.GaveUp, c.LastWalk);
    }

    [Fact] public void FallDuringWalk_DoesNotUseUpARepath()
    {
        var map = TestWorlds.FlatMap();
        var sim = new Simulation(map);
        var c = sim.Spawn(new Int3(5, 151, 10));
        var target = new Int3(20, 151, 10);
        sim.GoTo(c, target);
        for (int t = 0; t < 20; t++) sim.Tick();
        // A pit opens under the cell it is stepping onto, wide enough that its path doesn't go on from the bottom, with
        // a way out: it falls, lands and walks on from there.
        int x = c.Motion!.Value.To.X;
        for (int y = 150; y >= 148; y--)
            for (int dx = 0; dx <= 2; dx++) TestWorlds.Dig(map, x + dx, y, 10);
        Run(sim, () => c.Activity == ColonistActivity.Falling, 20);
        Run(sim, () => c.Activity != ColonistActivity.Falling, 20);
        Assert.Equal(new Int3(x, 148, 10), c.Cell);
        Assert.Equal(0, c.Walk?.Repaths);
        Run(sim, () => c.Activity == ColonistActivity.Idle, 2000);
        Assert.Equal((target, WalkResult.Arrived), (c.Cell, c.LastWalk));
    }

    [Fact] public void NoHome_OnlyColonistsWithNowhereToStandAreStuck()
    {
        // Home deep in the rock: nothing standable near it.
        var world = TestWorlds.SolidBelow();
        Discovery.InitialFlood(world);
        var map = new ColonyMap(new MapSettings(1, 64, 64, new TerrainParams()), world, site: new StartSite(32, 32, 60));
        var sim = new Simulation(map);
        Assert.Equal(-1, sim.HomeComponent);
        var c = sim.Spawn(new Int3(20, 151, 20));
        Assert.False(c.Stuck);
        map.World.SetBlock(20, 152, 20, Blocks.Stone); // over its feet
        sim.Tick();
        Assert.True(c.Stuck);
    }

    static void Run(Simulation sim, Func<bool> until, int maxTicks)
    {
        for (int t = 0; t < maxTicks && !until(); t++) sim.Tick();
        Assert.True(until(), $"not done after {maxTicks} ticks");
    }

    /// <summary>The timing test, alone in the Perf collection so no other test competes for the cores.</summary>
    [Collection(nameof(NavigationTests.Perf))]
    public class Perf(ITestOutputHelper output)
    {
        static bool Optimized => typeof(Simulation).Assembly.GetCustomAttribute<DebuggableAttribute>() is not { IsJITOptimizerDisabled: true };

        [Fact, Trait("Category", "Perf")] public void HundredColonists_TickUnder2ms()
        {
            var map = TestWorlds.FlatMap(256);
            var sim = new Simulation(map);
            var rng = new Random(3);
            Int3 RandomCell() => new(rng.Next(256), 151, rng.Next(256));
            for (int i = 0; i < 100; i++) sim.Spawn(RandomCell());

            var ms = new List<double>();
            int arrivals = 0;
            for (int t = 0; t < 1200; t++)
            {
                foreach (var c in map.Colony.Colonists)
                    if (c.Activity == ColonistActivity.Idle)
                    {
                        if (t > 0) arrivals++;
                        sim.GoTo(c, RandomCell());
                    }
                var sw = Stopwatch.StartNew();
                sim.Tick();
                if (t >= 200) ms.Add(sw.Elapsed.TotalMilliseconds); // after warm-up
            }
            output.WriteLine($"100 colonists, 1000 ticks: mean {ms.Average():F3} ms, max {ms.Max():F2} ms, {arrivals} arrivals"
                + (Optimized ? "" : " [Debug build: limits not checked, run with -c Release]"));
            Assert.True(arrivals > 0);
            if (!Optimized) return;
            Assert.True(ms.Average() < 2, $"mean {ms.Average():F3} ms");
        }
    }
}
