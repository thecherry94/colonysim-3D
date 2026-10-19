using System.Diagnostics;
using System.Reflection;
using Xunit.Abstractions;

namespace ColonySim.Core.Tests;

public class NavRulesTests(ITestOutputHelper output)
{
    // Flat map: stone to y = 150, so the standing (feet) y is 151.
    static (ColonyMap Map, NavRules Nav) Flat()
    {
        var m = TestWorlds.FlatMap();
        return (m, new NavRules(m.View));
    }

    static Move MoveOf(NavRules nav, Int3 from, Int3 to)
    {
        Assert.True(nav.TryGetMove(from, to, out var mv), $"no move {from} -> {to}");
        return mv;
    }

    static void NoMove(NavRules nav, Int3 from, Int3 to) => Assert.False(nav.TryGetMove(from, to, out _), $"unexpected move {from} -> {to}");

    [Fact] public void Standable_OnGround_NotWithOneHeadroom()
    {
        var (m, nav) = Flat();
        Assert.True(nav.IsStandable(new(5, 151, 5)));
        Assert.False(nav.IsStandable(new(5, 152, 5)));
        m.World.SetBlock(5, 152, 5, Blocks.Stone);
        Assert.False(nav.IsStandable(new(5, 151, 5)));
    }

    [Fact] public void Walk_CostAndTicks()
    {
        var (_, nav) = Flat();
        var mv = MoveOf(nav, new(5, 151, 5), new(6, 151, 5));
        Assert.Equal((MoveKind.Walk, 1.0f, 6), (mv.Kind, mv.Cost, mv.Ticks));
        mv = MoveOf(nav, new(5, 151, 5), new(6, 151, 6));
        Assert.Equal((MoveKind.Diagonal, 1.4142f, 8), (mv.Kind, mv.Cost, mv.Ticks));
    }

    [Fact] public void Diagonal_BlockedCorner_NotAllowed()
    {
        var (m, nav) = Flat();
        m.World.SetBlock(6, 151, 5, Blocks.Stone);
        NoMove(nav, new(5, 151, 5), new(6, 151, 6));
        Assert.Equal(MoveKind.Walk, MoveOf(nav, new(5, 151, 5), new(5, 151, 6)).Kind);
    }

    [Fact] public void Diagonal_BlockedCornerHead_NotAllowed()
    {
        var (m, nav) = Flat();
        m.World.SetBlock(5, 152, 6, Blocks.WoodPlanks);
        NoMove(nav, new(5, 151, 5), new(6, 151, 6));
    }

    [Fact] public void Stairs_AscendOnlyAlongFacing()
    {
        var (m, nav) = Flat();
        m.World.SetBlock(6, 151, 5, Blocks.Stairs(BuildMaterial.Wood, Facing.East));
        var mv = MoveOf(nav, new(5, 151, 5), new(6, 152, 5));
        Assert.Equal((MoveKind.Step, 1.5f, 8), (mv.Kind, mv.Cost, mv.Ticks));
        NoMove(nav, new(6, 151, 4), new(6, 152, 5));
    }

    [Fact] public void Stairs_DescendReverse()
    {
        var (m, nav) = Flat();
        m.World.SetBlock(6, 151, 5, Blocks.Stairs(BuildMaterial.Wood, Facing.East));
        Assert.Equal(MoveKind.Step, MoveOf(nav, new(6, 152, 5), new(5, 151, 5)).Kind);
        NoMove(nav, new(6, 152, 5), new(6, 151, 4));
    }

    [Fact] public void Corner_AscendsAlongBothDirections()
    {
        var (m, nav) = Flat();
        // Natural corner facing North ascends North and East; from the other sides it is only a (natural) hop.
        m.World.SetBlock(6, 151, 5, Blocks.NaturalSlope(Blocks.Stone, BlockShape.CornerOuter, Facing.North));
        Assert.Equal(MoveKind.Step, MoveOf(nav, new(5, 151, 5), new(6, 152, 5)).Kind);
        Assert.Equal(MoveKind.Step, MoveOf(nav, new(6, 151, 6), new(6, 152, 5)).Kind);
        Assert.Equal(MoveKind.Hop, MoveOf(nav, new(7, 151, 5), new(6, 152, 5)).Kind);
        Assert.Equal(MoveKind.Hop, MoveOf(nav, new(6, 151, 4), new(6, 152, 5)).Kind);
        Assert.Equal(MoveKind.Step, MoveOf(nav, new(6, 152, 5), new(5, 151, 5)).Kind);
        Assert.Equal(MoveKind.Step, MoveOf(nav, new(6, 152, 5), new(6, 151, 6)).Kind);
        Assert.Equal(MoveKind.Hop, MoveOf(nav, new(6, 152, 5), new(7, 151, 5)).Kind);
    }

    [Fact] public void Ladder_ShaftUpDownAndExitAtTop()
    {
        var (m, nav) = Flat();
        for (int y = 147; y <= 150; y++)
        {
            TestWorlds.Dig(m, 5, y, 5);
            m.World.SetBlock(5, y, 5, Blocks.Ladder(Facing.North));
        }
        for (int y = 147; y <= 150; y++)
        {
            var up = MoveOf(nav, new(5, y, 5), new(5, y + 1, 5));
            Assert.Equal((MoveKind.Ladder, 2.0f, 10), (up.Kind, up.Cost, up.Ticks));
            Assert.Equal(MoveKind.Ladder, MoveOf(nav, new(5, y + 1, 5), new(5, y, 5)).Kind);
        }
        Assert.Equal(MoveKind.Walk, MoveOf(nav, new(5, 151, 5), new(6, 151, 5)).Kind);
        NoMove(nav, new(5, 147, 5), new(5, 146, 5));
    }

    [Fact] public void Hop_OnNaturalOnly()
    {
        var (m, nav) = Flat();
        m.World.SetBlock(6, 151, 5, Blocks.Stone);
        var mv = MoveOf(nav, new(5, 151, 5), new(6, 152, 5));
        Assert.Equal((MoveKind.Hop, 6.0f, 20), (mv.Kind, mv.Cost, mv.Ticks));
        Assert.Equal(MoveKind.Hop, MoveOf(nav, new(6, 152, 5), new(5, 151, 5)).Kind);

        m.World.SetBlock(6, 151, 5, Blocks.WoodPlanks);
        NoMove(nav, new(5, 151, 5), new(6, 152, 5));
        NoMove(nav, new(6, 152, 5), new(5, 151, 5));
    }

    [Fact] public void Hop_UnderRoof_InCave_Allowed()
    {
        var (m, nav) = Flat();
        // Cave room inside the stone: lower floor at y = 140 (3 tall), a natural stone step at (6,140,5) with 2 tall space above it.
        for (int y = 140; y <= 142; y++) TestWorlds.Dig(m, 5, y, 5);
        for (int y = 141; y <= 142; y++) TestWorlds.Dig(m, 6, y, 5);
        Assert.Equal(MoveKind.Hop, MoveOf(nav, new(5, 140, 5), new(6, 141, 5)).Kind);
        Assert.Equal(MoveKind.Hop, MoveOf(nav, new(6, 141, 5), new(5, 140, 5)).Kind);
    }

    [Fact] public void Hop_NeedsHeadClearance()
    {
        var (m, nav) = Flat();
        m.World.SetBlock(6, 151, 5, Blocks.Stone);
        m.World.SetBlock(5, 153, 5, Blocks.Stone);
        Assert.True(nav.IsStandable(new(5, 151, 5)));
        NoMove(nav, new(5, 151, 5), new(6, 152, 5));
        NoMove(nav, new(6, 152, 5), new(5, 151, 5));
    }

    [Fact] public void Climb_TwoAndThree_FourNot()
    {
        var (m, nav) = Flat();
        for (int k = 2; k <= 4; k++)
        {
            int z = 5 + 10 * k;
            for (int j = 0; j < k; j++) m.World.SetBlock(6, 151 + j, z, Blocks.Stone);
            Int3 bottom = new(5, 151, z), top = new(6, 151 + k, z);
            if (k == 4) { NoMove(nav, bottom, top); NoMove(nav, top, bottom); continue; }
            var up = MoveOf(nav, bottom, top);
            Assert.Equal((MoveKind.Climb, 10f + 4 * k, 30 * k), (up.Kind, up.Cost, up.Ticks));
            var down = MoveOf(nav, top, bottom);
            Assert.Equal((MoveKind.Climb, 10f + 4 * k, 30 * k), (down.Kind, down.Cost, down.Ticks));
        }
    }

    [Fact] public void Climb_NeedsHeadClearance()
    {
        var (m, nav) = Flat();
        for (int j = 0; j < 3; j++) m.World.SetBlock(6, 151 + j, 5, Blocks.Stone);
        m.World.SetBlock(5, 155, 5, Blocks.WoodPlanks); // C + 4up
        NoMove(nav, new(5, 151, 5), new(6, 154, 5));
        NoMove(nav, new(6, 154, 5), new(5, 151, 5));
    }

    [Fact] public void Climb_BuiltWall_NotAllowed()
    {
        var (m, nav) = Flat();
        for (int j = 0; j < 2; j++) m.World.SetBlock(6, 151 + j, 5, Blocks.WoodPlanks);
        NoMove(nav, new(5, 151, 5), new(6, 153, 5));
        NoMove(nav, new(6, 153, 5), new(5, 151, 5));
        // Natural rock topped by a built block is not climbable either.
        m.World.SetBlock(6, 151, 15, Blocks.Stone); m.World.SetBlock(6, 152, 15, Blocks.StoneBricks);
        NoMove(nav, new(5, 151, 15), new(6, 153, 15));
    }

    [Fact] public void Water_TriplesCostAndTicks()
    {
        var (m, nav) = Flat();
        m.World.SetBlock(6, 151, 5, Blocks.Water);
        var into = MoveOf(nav, new(5, 151, 5), new(6, 151, 5));
        Assert.Equal((MoveKind.Walk, 3.0f, 18), (into.Kind, into.Cost, into.Ticks));
        var outOf = MoveOf(nav, new(6, 151, 5), new(5, 151, 5));
        Assert.Equal((MoveKind.Walk, 1.0f, 6), (outOf.Kind, outOf.Cost, outOf.Ticks));
    }

    [Fact] public void UndiscoveredAir_IsNotPassable()
    {
        var (m, nav) = Flat();
        m.World.SetBlock(5, 140, 5, Blocks.Air);
        Assert.False(nav.IsPassable(new(5, 140, 5)));
        m.View.FogEnabled = false; // the debug toggle does not change what navigation knows
        Assert.False(nav.IsPassable(new(5, 140, 5)));
        Discovery.RevealFrom(m.World, 5, 140, 5);
        Assert.True(nav.IsPassable(new(5, 140, 5)));
    }

    [Fact] public void OutOfBounds_IsNotPassable()
    {
        var (_, nav) = Flat();
        Assert.False(nav.IsPassable(new(-1, 151, 5)));
        Assert.False(nav.IsPassable(new(5, 256, 5)));
        Assert.False(nav.IsStandable(new(64, 151, 5)));
        NoMove(nav, new(0, 151, 5), new(-1, 151, 5));
    }

    [Fact] public void Moves_AreSymmetric_OnRandomWorld()
    {
        var pool = new List<ushort> { Blocks.Stone, Blocks.Stone, Blocks.Stone, Blocks.Stone, Blocks.WoodPlanks, Blocks.Water };
        foreach (var f in Enum.GetValues<Facing>())
        {
            pool.Add(Blocks.Stairs(BuildMaterial.Wood, f)); pool.Add(Blocks.Stairs(BuildMaterial.Stone, f));
            pool.Add(Blocks.BuiltRamp(BuildMaterial.Wood, f)); pool.Add(Blocks.CarvedStairs(Blocks.Stone, f));
            pool.Add(Blocks.Ladder(f)); pool.Add(Blocks.Ladder(f));
            foreach (var s in new[] { BlockShape.Ramp, BlockShape.CornerOuter, BlockShape.CornerInner })
                pool.Add(Blocks.NaturalSlope(Blocks.Stone, s, f));
        }
        var w = TestWorlds.SolidBelow();
        var rng = new Random(20261019);
        for (int y = 150; y <= 156; y++)
            for (int z = 0; z < 64; z++)
                for (int x = 0; x < 64; x++)
                    w.SetBlock(x, y, z, rng.NextDouble() < 0.5 ? Blocks.Air : pool[rng.Next(pool.Count)], markDirty: false);
        Discovery.InitialFlood(w);
        var map = new ColonyMap(new MapSettings(1, 64, 64, new TerrainParams()), w);
        var nav = new NavRules(map.View);

        Span<Move> moves = stackalloc Move[NavRules.MaxMoves];
        var kinds = new int[Enum.GetValues<MoveKind>().Length];
        var failures = new List<string>();
        int standable = 0;
        for (int y = 145; y <= 160; y++)
            for (int z = 0; z < 64; z++)
                for (int x = 0; x < 64; x++)
                {
                    Int3 a = new(x, y, z);
                    if (!nav.IsStandable(a)) continue;
                    standable++;
                    int n = nav.GetMoves(a, moves);
                    for (int i = 0; i < n; i++)
                    {
                        var mv = moves[i];
                        kinds[(int)mv.Kind]++;
                        for (int j = 0; j < i; j++)
                            if (moves[j].To == mv.To) failures.Add($"duplicate destination {a} -> {mv.To}");
                        if (!nav.TryGetMove(mv.To, a, out var back)) failures.Add($"{a} -> {mv.To} ({mv.Kind}) has no way back");
                        else if (back.Kind != mv.Kind) failures.Add($"{a} -> {mv.To} is {mv.Kind}, back is {back.Kind}");
                        else if (nav.Core.ReverseCost(a, mv) != back.Cost)
                            failures.Add($"{a} -> {mv.To}: ReverseCost {nav.Core.ReverseCost(a, mv)} but the move back costs {back.Cost}");
                    }
                }
        output.WriteLine($"standable {standable}; " + string.Join(", ", Enum.GetValues<MoveKind>().Select(k => $"{k} {kinds[(int)k]}")));
        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(20)));
        foreach (var k in new[] { MoveKind.Walk, MoveKind.Diagonal, MoveKind.Step, MoveKind.Ladder, MoveKind.Hop, MoveKind.Climb })
            Assert.True(kinds[(int)k] > 0, $"random world produced no {k} moves");
    }

    [Fact] public void TopStandable_AndFindStandableNear()
    {
        var (m, nav) = Flat();
        Assert.Equal(151, nav.TopStandable(5, 5));
        m.World.SetBlock(7, 160, 7, Blocks.Stone);
        Assert.Equal(161, nav.TopStandable(7, 7));
        for (int y = 151; y <= 255; y++) m.World.SetBlock(9, y, 9, Blocks.Stone);
        Assert.Equal(-1, nav.TopStandable(9, 9));

        Assert.True(nav.FindStandableNear(new(5, 155, 5), 5, out var c));
        Assert.Equal(new Int3(5, 151, 5), c);
        Assert.False(nav.FindStandableNear(new(30, 100, 30), 3, out _));

        // Equal distance: higher y wins (surface over a dug cave cell).
        TestWorlds.Dig(m, 20, 147, 20); TestWorlds.Dig(m, 20, 148, 20);
        Assert.True(nav.IsStandable(new(20, 147, 20)));
        Assert.True(nav.FindStandableNear(new(20, 149, 20), 2, out c));
        Assert.Equal(new Int3(20, 151, 20), c);

        // Equal distance and y: lower x wins, then lower z.
        for (int y = 151; y <= 153; y++) m.World.SetBlock(30, y, 30, Blocks.Stone);
        Assert.True(nav.FindStandableNear(new(30, 152, 30), 1, out c));
        Assert.Equal(new Int3(29, 151, 30), c);
        TestWorlds.Dig(m, 29, 150, 30);
        Assert.True(nav.FindStandableNear(new(30, 152, 30), 1, out c));
        Assert.Equal(new Int3(30, 151, 29), c);
    }

    [Fact, Trait("Category", "Perf")] public void GetMoves_1M_Under400ms()
    {
        var map = MapGenerator.Generate(MapSettings.Create(7, MapSize.Small));
        var nav = new NavRules(map.View);
        var cells = new List<Int3>();
        for (int z = 0; z < map.World.SizeZ; z += 4)
            for (int x = 0; x < map.World.SizeX; x += 4)
            {
                int y = nav.TopStandable(x, z);
                if (y >= 0) cells.Add(new(x, y, z));
            }
        Span<Move> moves = stackalloc Move[NavRules.MaxMoves];
        long total = 0;
        for (int i = 0; i < 100_000; i++) total += nav.GetMoves(cells[i % cells.Count], moves); // warm-up
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 1_000_000; i++) total += nav.GetMoves(cells[i % cells.Count], moves);
        sw.Stop();
        // The budget is for optimized code: a Debug build (the dotnet test default) runs about 13x slower, so there
        // the time is only recorded. Run with -c Release to check it.
        var debuggable = typeof(NavRules).Assembly.GetCustomAttribute<DebuggableAttribute>();
        bool optimized = debuggable == null || !debuggable.IsJITOptimizerDisabled;
        output.WriteLine($"GetMoves x1M: {sw.Elapsed.TotalMilliseconds:F1} ms over {cells.Count} cells ({total} moves)"
            + (optimized ? "" : " [Debug build: not checked against the 400 ms budget, run with -c Release]"));
        if (optimized) Assert.True(sw.Elapsed.TotalMilliseconds < 400, $"{sw.Elapsed.TotalMilliseconds:F1} ms");
    }
}
