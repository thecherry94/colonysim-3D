using System.Diagnostics;
using System.Reflection;
using Xunit.Abstractions;

namespace ColonySim.Core.Tests;

public class PathFinderTests(ITestOutputHelper output)
{
    // Flat map: stone to y = 150, so the standing (feet) y is 151.

    /// <summary>The cheapest path cost by plain Dijkstra over <see cref="NavRules.GetMoves"/>: the reference optimum.</summary>
    static float Dijkstra(NavRules rules, Int3 from, Int3 to)
    {
        var dist = new Dictionary<Int3, float> { [from] = 0 };
        var queue = new PriorityQueue<Int3, float>();
        queue.Enqueue(from, 0);
        var moves = new Move[NavRules.MaxMoves];
        while (queue.TryDequeue(out var c, out float d))
        {
            if (d > dist[c]) continue;
            if (c == to) return d;
            int n = rules.GetMoves(c, moves);
            for (int i = 0; i < n; i++)
            {
                float nd = d + moves[i].Cost;
                if (dist.TryGetValue(moves[i].To, out float old) && old <= nd) continue;
                dist[moves[i].To] = nd;
                queue.Enqueue(moves[i].To, nd);
            }
        }
        return float.PositiveInfinity;
    }

    /// <summary>Found, starts at From, every move is a real move, ends at To, and the costs add up.</summary>
    static void AssertValidPath(Navigation nav, PathRequest r)
    {
        Assert.True(r.Status == PathStatus.Found, $"{r.From} -> {r.To}: {r.Status}");
        Int3 at = r.From;
        float cost = 0;
        foreach (var m in r.Moves)
        {
            Assert.True(nav.Rules.TryGetMove(at, m.To, out var real), $"{r.From} -> {r.To}: no move {at} -> {m.To}");
            Assert.Equal(real, m);
            cost += m.Cost;
            at = m.To;
        }
        Assert.Equal(r.To, at);
        Assert.Equal(cost, r.Cost, 3);
    }

    static bool Uses(PathRequest r, MoveKind kind) => r.Moves.Any(m => m.Kind == kind);

    [Fact] public void Straight_And_AroundWall_AreOptimal()
    {
        var m = TestWorlds.FlatMap();
        var finder = new PathFinder(m.Nav);
        var straight = finder.FindNow(new(5, 151, 5), new(20, 151, 5));
        AssertValidPath(m.Nav, straight);
        Assert.Equal(15, straight.Moves.Count);
        Assert.Equal(15f, straight.Cost, 3);
        Assert.Equal(m.Nav.Version, straight.NavVersion);

        for (int z = 0; z <= 20; z++)
            for (int y = 151; y <= 152; y++) m.World.SetBlock(10, y, z, Blocks.WoodPlanks);
        m.Nav.Update();
        var around = finder.FindNow(new(5, 151, 10), new(15, 151, 10));
        AssertValidPath(m.Nav, around);
        Assert.Contains(around.Moves, mv => mv.To.Z > 20);
        Assert.Equal(Dijkstra(m.Nav.Rules, around.From, around.To), around.Cost, 3);

        var self = finder.FindNow(new(5, 151, 5), new(5, 151, 5));
        Assert.Equal((PathStatus.Found, 0, 0f), (self.Status, self.Moves.Count, self.Cost));
    }

    [Fact] public void UsesStairsAndLadders_BetweenFloors()
    {
        var m = TestWorlds.FlatMap();
        // Two separate wooden floors at y = 154 (feet 155): one reached by a stair run, one by a ladder.
        for (int x = 14; x <= 24; x++)
            for (int z = 0; z <= 10; z++)
            {
                m.World.SetBlock(x, 154, z, Blocks.WoodPlanks);
                m.World.SetBlock(x, 154, z + 30, Blocks.WoodPlanks);
            }
        for (int i = 0; i < 4; i++) m.World.SetBlock(10 + i, 151 + i, 5, Blocks.Stairs(BuildMaterial.Wood, Facing.East));
        for (int y = 151; y <= 154; y++) m.World.SetBlock(25, y, 35, Blocks.Ladder(Facing.West));
        var finder = new PathFinder(m.Nav);

        var up = finder.FindNow(new(5, 151, 5), new(20, 155, 5));
        AssertValidPath(m.Nav, up);
        Assert.True(Uses(up, MoveKind.Step));
        Assert.Equal(Dijkstra(m.Nav.Rules, up.From, up.To), up.Cost, 3);

        var ladder = finder.FindNow(new(5, 151, 35), new(20, 155, 35));
        AssertValidPath(m.Nav, ladder);
        Assert.True(Uses(ladder, MoveKind.Ladder));
        Assert.Equal(Dijkstra(m.Nav.Rules, ladder.From, ladder.To), ladder.Cost, 3);

        var down = finder.FindNow(new(20, 155, 5), new(5, 151, 5));
        AssertValidPath(m.Nav, down);
        Assert.Equal(up.Cost, down.Cost, 3);
    }

    [Fact] public void PrefersStairs_OverHop()
    {
        var m = TestWorlds.FlatMap();
        // A natural ledge one block high from x = 10 on (feet 152): hoppable anywhere, or a stair at (9, 151, 8).
        for (int x = 10; x < 64; x++)
            for (int z = 0; z < 64; z++) m.World.SetBlock(x, 151, z, Blocks.Stone);
        m.World.SetBlock(9, 151, 8, Blocks.Stairs(BuildMaterial.Stone, Facing.East));
        var r = new PathFinder(m.Nav).FindNow(new(8, 151, 5), new(12, 152, 5));
        AssertValidPath(m.Nav, r);
        const float hopRoute = 1 + 6 + 1 + 1; // walk, hop, walk, walk
        Assert.True(r.Cost < hopRoute, $"cost {r.Cost}");
        Assert.True(Uses(r, MoveKind.Step));
        Assert.False(Uses(r, MoveKind.Hop));
    }

    [Fact] public void CliffOfFour_IsUnreachable_UntilStairsBuilt()
    {
        var m = TestWorlds.FlatMap();
        for (int x = 10; x < 64; x++)
            for (int z = 0; z < 64; z++)
                for (int y = 151; y <= 154; y++) m.World.SetBlock(x, y, z, Blocks.Stone);
        var finder = new PathFinder(m.Nav);
        Int3 low = new(5, 151, 5), high = new(15, 155, 5);
        var r = finder.FindNow(low, high);
        Assert.Equal((PathStatus.Unreachable, 0), (r.Status, r.NodesExpanded));

        for (int i = 0; i < 4; i++) m.World.SetBlock(6 + i, 151 + i, 5, Blocks.Stairs(BuildMaterial.Stone, Facing.East));
        m.Nav.Update();
        r = finder.FindNow(low, high);
        AssertValidPath(m.Nav, r);
        Assert.Equal(4, r.Moves.Count(mv => mv.Kind == MoveKind.Step));
    }

    [Fact] public void Path_IntoCave_OnlyAfterReveal()
    {
        var m = TestWorlds.FlatMap();
        // A laddered shaft dug from the surface down to y = 144, and below it a cave room (y 140..142) with the
        // ladder continuing into it: all real, but the cave and the last shaft cell are never revealed.
        for (int y = 150; y >= 144; y--)
        {
            TestWorlds.Dig(m, 20, y, 20);
            m.World.SetBlock(20, y, 20, Blocks.Ladder(Facing.North));
        }
        for (int x = 20; x <= 30; x++)
            for (int z = 20; z <= 30; z++)
                for (int y = 140; y <= 142; y++) m.World.SetBlock(x, y, z, Blocks.Air);
        for (int y = 140; y <= 143; y++) m.World.SetBlock(20, y, 20, Blocks.Ladder(Facing.North));
        var finder = new PathFinder(m.Nav);
        Int3 surface = new(5, 151, 5), cave = new(28, 140, 28);
        Assert.Equal(PathStatus.Unreachable, finder.FindNow(surface, cave).Status);

        Discovery.RevealFrom(m.World, 20, 144, 20);
        m.Nav.Update();
        var r = finder.FindNow(surface, cave);
        AssertValidPath(m.Nav, r);
        Assert.True(Uses(r, MoveKind.Ladder));
    }

    [Fact] public void EveryMove_IsValid()
    {
        var pool = new List<ushort> { Blocks.Stone, Blocks.Stone, Blocks.Stone, Blocks.Stone, Blocks.WoodPlanks, Blocks.Water };
        foreach (var f in Enum.GetValues<Facing>())
        {
            pool.Add(Blocks.Stairs(BuildMaterial.Wood, f));
            pool.Add(Blocks.Ladder(f));
            pool.Add(Blocks.NaturalSlope(Blocks.Stone, BlockShape.Ramp, f));
        }
        var w = TestWorlds.SolidBelow();
        var rng = new Random(9);
        for (int y = 151; y <= 153; y++)
            for (int z = 0; z < 64; z++)
                for (int x = 0; x < 64; x++)
                    if (rng.NextDouble() < 0.25) w.SetBlock(x, y, z, pool[rng.Next(pool.Count)], markDirty: false);
        Discovery.InitialFlood(w);
        var map = new ColonyMap(new MapSettings(1, 64, 64, new TerrainParams()), w);
        var nav = map.Nav;
        var finder = new PathFinder(nav);
        var cells = new List<Int3>();
        for (int z = 0; z < 64; z++)
            for (int x = 0; x < 64; x++)
            {
                int y = nav.Rules.TopStandable(x, z);
                if (y > 0) cells.Add(new(x, y, z));
            }

        int found = 0, unreachable = 0, longOnes = 0;
        for (int i = 0; i < 150; i++)
        {
            Int3 a = cells[rng.Next(cells.Count)], b = cells[rng.Next(cells.Count)];
            var r = finder.FindNow(a, b);
            if (nav.ComponentOf(a) != nav.ComponentOf(b))
            {
                Assert.Equal(PathStatus.Unreachable, r.Status);
                unreachable++;
                continue;
            }
            AssertValidPath(nav, r);
            found++;
            if (Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Z - b.Z)) > 32) longOnes++;
            else Assert.Equal(Dijkstra(nav.Rules, a, b), r.Cost, 3);
        }
        output.WriteLine($"{found} found ({longOnes} long), {unreachable} unreachable");
        Assert.True(found >= 50 && longOnes >= 10, $"{found} found, {longOnes} long");
    }

    [Fact] public void LongPath_WithinFactorOfOptimal()
    {
        // Ruling R12: Small seeds 1..5, 20 home-component pairs > 100 apart each, against unrestricted (optimal) A*.
        var perSeed = new List<double>[5];
        Parallel.For(0, 5, k =>
        {
            int seed = k + 1;
            var map = MapGenerator.Generate(MapSettings.Create(seed, MapSize.Small));
            var nav = map.Nav;
            var site = map.StartSite;
            int home = nav.HomeComponent(new Int3(site.X, site.Level, site.Z));
            var finder = new PathFinder(nav);
            var rng = new Random(seed);
            var ratios = perSeed[k] = new List<double>();
            while (ratios.Count < 20)
            {
                Int3 a = RandomCellIn(map, home, rng), b = RandomCellIn(map, home, rng);
                if (Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Z - b.Z)) <= 100) continue;
                var fast = finder.FindNow(a, b);
                var best = finder.FindNow(a, b, hierarchical: false);
                AssertValidPath(nav, fast);
                AssertValidPath(nav, best);
                ratios.Add(fast.Cost / best.Cost);
            }
        });
        var all = perSeed.SelectMany(r => r).OrderBy(r => r).ToList();
        double mean = all.Average(), p95 = all[(int)Math.Ceiling(0.95 * all.Count) - 1], max = all[^1];
        for (int k = 0; k < 5; k++) output.WriteLine($"seed {k + 1}: mean {perSeed[k].Average():F3}, max {perSeed[k].Max():F3}");
        output.WriteLine($"{all.Count} pairs: mean {mean:F3}, p95 {p95:F3}, max {max:F3}");
        Assert.True(mean <= 1.15, $"mean {mean:F3}");
        Assert.True(p95 <= 1.25, $"p95 {p95:F3}");
        Assert.True(max <= 1.40, $"max {max:F3}");
    }

    /// <summary>A random column's top standable cell, in the given component.</summary>
    static Int3 RandomCellIn(ColonyMap map, int component, Random rng)
    {
        while (true)
        {
            int x = rng.Next(map.World.SizeX), z = rng.Next(map.World.SizeZ);
            int y = map.Nav.Rules.TopStandable(x, z);
            if (y > 0 && map.Nav.ComponentOf(new(x, y, z)) == component) return new(x, y, z);
        }
    }

    [Fact] public void Service_TimeSlices_AndCompletes()
    {
        var m = TestWorlds.FlatMap();
        // A serpentine: wooden walls every 8 cells, each with one gap, alternating ends.
        for (int x = 8; x < 64; x += 8)
            for (int z = 0; z < 64; z++)
            {
                if (z == ((x / 8) % 2 == 1 ? 62 : 1)) continue;
                for (int y = 151; y <= 152; y++) m.World.SetBlock(x, y, z, Blocks.WoodPlanks);
            }
        var nav = m.Nav;
        Int3 from = new(2, 151, 32), to = new(61, 151, 32);
        var service = new PathService(nav);
        var r = service.Request(from, to);
        var cancelled = service.Request(to, from);
        var unreachable = service.Request(from, new(2, 160, 32));
        Assert.Equal(PathStatus.Unreachable, unreachable.Status);
        Assert.Equal(2, service.Pending);
        service.Cancel(cancelled);
        Assert.Equal(1, service.Pending);

        int calls = 0;
        while (r.Status == PathStatus.Pending)
        {
            service.Process(500);
            calls++;
            Assert.True(calls < 1000);
        }
        output.WriteLine($"{calls} calls, {r.NodesExpanded} nodes");
        Assert.True(calls >= 3, $"finished in {calls} calls");
        Assert.Equal(0, service.Pending);
        Assert.Equal(PathStatus.Pending, cancelled.Status);
        var now = new PathFinder(nav).FindNow(from, to);
        AssertValidPath(nav, r);
        Assert.Equal(now.Moves, r.Moves);
        Assert.Equal(now.Cost, r.Cost);
    }

    [Fact] public void Service_FinishesOnNewData_WhenNavigationChanges()
    {
        var m = TestWorlds.FlatMap();
        for (int z = 0; z < 63; z++)
            for (int y = 151; y <= 152; y++) m.World.SetBlock(32, y, z, Blocks.WoodPlanks); // a wall, open only at z = 63
        var nav = m.Nav;
        var service = new PathService(nav);
        var r = service.Request(new(2, 151, 10), new(61, 151, 10));
        service.Process(20);
        Assert.Equal(PathStatus.Pending, r.Status);

        for (int y = 151; y <= 152; y++) m.World.SetBlock(32, y, 10, Blocks.Air); // a door right on the way
        nav.Update();
        while (r.Status == PathStatus.Pending) service.Process(500);
        AssertValidPath(nav, r);
        Assert.Equal(nav.Version, r.NavVersion);
        Assert.Equal(59f, r.Cost, 3);
    }

    [Fact] public void Service_GoalWalledOffMidSearch_UnreachableAtOnce()
    {
        // Once for a long (hierarchical) request, once for a short (plain A*) one.
        foreach (var from in new[] { new Int3(2, 151, 32), new Int3(40, 151, 32) })
        {
            var m = TestWorlds.FlatMap();
            var nav = m.Nav;
            var service = new PathService(nav);
            Int3 goal = new(61, 151, 32);
            var r = service.Request(from, goal);
            service.Process(5);
            Assert.Equal(PathStatus.Pending, r.Status);

            // Wall the goal's corner of the map off (wooden walls can't be hopped or climbed).
            for (int y = 151; y <= 152; y++)
            {
                for (int z = 29; z <= 35; z++) m.World.SetBlock(58, y, z, Blocks.WoodPlanks);
                for (int x = 58; x < 64; x++)
                {
                    m.World.SetBlock(x, y, 29, Blocks.WoodPlanks);
                    m.World.SetBlock(x, y, 35, Blocks.WoodPlanks);
                }
            }
            nav.Update();
            service.Process(500);
            Assert.Equal(PathStatus.Unreachable, r.Status);
            Assert.Equal(0, service.Pending);
            Assert.True(r.NodesExpanded <= 5, $"{r.NodesExpanded} nodes");
        }
    }

    [Fact] public void Service_EditDuringPlainSearch_PathValidOnNewData()
    {
        var m = TestWorlds.FlatMap();
        var nav = m.Nav;
        var service = new PathService(nav);
        var r = service.Request(new(5, 151, 10), new(30, 151, 10)); // 25 apart: plain cell A*
        service.Process(5);
        Assert.Equal(PathStatus.Pending, r.Status);

        for (int z = 0; z <= 30; z++)
            for (int y = 151; y <= 152; y++) m.World.SetBlock(20, y, z, Blocks.WoodPlanks); // across the straight way
        nav.Update();
        while (r.Status == PathStatus.Pending) service.Process(500);
        AssertValidPath(nav, r); // every move checked against the world after the edit
        Assert.Contains(r.Moves, mv => mv.To.Z > 30);
        Assert.Equal(nav.Version, r.NavVersion);
    }

    [Fact] public void NodeLimit_GivesTooLong()
    {
        var m = TestWorlds.FlatMap();
        var finder = new PathFinder(m.Nav, nodeLimit: 20);
        foreach (var to in new[] { new Int3(60, 151, 60), new Int3(30, 151, 30) }) // long, then short
        {
            var r = finder.FindNow(new(2, 151, 2), to);
            Assert.Equal((PathStatus.TooLong, 20), (r.Status, r.NodesExpanded));
            Assert.Empty(r.Moves);
        }
    }

    /// <summary>The timing tests, alone in the Perf collection so no other test competes for the cores.</summary>
    [Collection(nameof(NavigationTests.Perf))]
    public class Perf(ITestOutputHelper output)
    {
        static bool Optimized => typeof(PathFinder).Assembly.GetCustomAttribute<DebuggableAttribute>() is not { IsJITOptimizerDisabled: true };

        [Fact, Trait("Category", "Perf")] public void Large_LongPaths_Fast()
        {
            var map = MapGenerator.Generate(MapSettings.Create(12345, MapSize.Large));
            var nav = map.Nav;
            var site = map.StartSite;
            int home = nav.HomeComponent(new Int3(site.X, site.Level, site.Z));
            var finder = new PathFinder(nav);
            var rng = new Random(11);
            // Warm-up: a second of searching, so tiered JIT has promoted the search loops, as in a running game.
            var warm = Stopwatch.StartNew();
            var warmRng = new Random(1);
            while (warm.ElapsedMilliseconds < 1000) finder.FindNow(RandomCellIn(map, home, warmRng), RandomCellIn(map, home, warmRng));
            var ms = new List<double>();
            var nodes = new List<int>();
            while (ms.Count < 50)
            {
                Int3 a = RandomCellIn(map, home, rng), b = RandomCellIn(map, home, rng);
                if (Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Z - b.Z)) < 200) continue;
                var sw = Stopwatch.StartNew();
                var r = finder.FindNow(a, b);
                ms.Add(sw.Elapsed.TotalMilliseconds);
                nodes.Add(r.NodesExpanded);
                Assert.True(r.Status == PathStatus.Found, $"{a} -> {b}: {r.Status}");
            }
            output.WriteLine($"Large, 50 paths >= 200 apart: mean {ms.Average():F2} ms, max {ms.Max():F2} ms, "
                + $"nodes mean {nodes.Average():F0}, max {nodes.Max()}"
                + (Optimized ? "" : " [Debug build: limits not checked, run with -c Release]"));
            if (!Optimized) return;
            Assert.True(ms.Average() < 5, $"mean {ms.Average():F2} ms");
            Assert.True(ms.Max() < 40, $"max {ms.Max():F2} ms");
        }
    }
}

