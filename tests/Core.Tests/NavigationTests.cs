using System.Diagnostics;
using System.Reflection;
using Xunit.Abstractions;

namespace ColonySim.Core.Tests;

public class NavigationTests
{
    // Every edit kind the random tests mix: digs dominate so the terrain opens up into pits, shafts and ledges.
    static (ushort Block, bool Dig) RandomEdit(Random rng)
    {
        var f = (Facing)rng.Next(4);
        var m = (BuildMaterial)rng.Next(2);
        return rng.Next(20) switch
        {
            < 7 => (Blocks.Air, true),
            < 10 => (Blocks.Stone, false),
            < 12 => (Blocks.WoodPlanks, false),
            < 14 => (Blocks.Stairs(m, f), false),
            < 16 => (Blocks.BuiltRamp(m, f), false),
            < 18 => (Blocks.Ladder(f), false),
            < 19 => (Blocks.Water, false),
            _ => (Blocks.NaturalSlope(Blocks.Stone, BlockShape.Ramp, f), false),
        };
    }

    /// <summary>Sets the block so the known world shows it: non-solid blocks are discovered (digs also reveal, as digging does).</summary>
    static void Apply(ColonyMap m, Int3 c, ushort block, bool dig)
    {
        if (dig) { TestWorlds.Dig(m, c.X, c.Y, c.Z); return; }
        m.World.SetBlock(c.X, c.Y, c.Z, block);
        if (!Blocks.Registry.Get(block).IsSolid) m.World.SetDiscovered(c.X, c.Y, c.Z);
    }

    /// <summary>Labels every standable cell by a plain BFS over <see cref="NavRules.GetMoves"/>.</summary>
    static Dictionary<Int3, int> BruteForce(ColonyMap m)
    {
        var rules = new NavRules(m.View);
        var label = new Dictionary<Int3, int>();
        var moves = new Move[NavRules.MaxMoves];
        var queue = new Queue<Int3>();
        int next = 0;
        for (int y = 1; y <= 254; y++)
            for (int z = 0; z < m.World.SizeZ; z++)
                for (int x = 0; x < m.World.SizeX; x++)
                {
                    var s = new Int3(x, y, z);
                    if (label.ContainsKey(s) || !rules.IsStandable(s)) continue;
                    label[s] = next;
                    queue.Enqueue(s);
                    while (queue.TryDequeue(out var c))
                    {
                        int n = rules.GetMoves(c, moves);
                        for (int i = 0; i < n; i++)
                            if (label.TryAdd(moves[i].To, next)) queue.Enqueue(moves[i].To);
                    }
                    next++;
                }
        return label;
    }

    /// <summary>Asserts that <paramref name="componentOf"/> partitions the standable cells exactly like the brute-force flood.</summary>
    static void AssertSamePartition(Dictionary<Int3, int> truth, Func<Int3, int> componentOf, string context)
    {
        var toTruth = new Dictionary<int, int>();
        var fromTruth = new Dictionary<int, int>();
        foreach (var (c, t) in truth)
        {
            int k = componentOf(c);
            Assert.True(k >= 0, $"{context}: standable {c} has no component");
            Assert.True(toTruth.TryAdd(k, t) || toTruth[k] == t, $"{context}: {c} joined to a component it can't reach");
            Assert.True(fromTruth.TryAdd(t, k) || fromTruth[t] == k, $"{context}: {c} split from a component it reaches");
        }
    }

    /// <summary>Sections lie in one chunk and one component, anchors are standable members, and every link is a real move.</summary>
    static void AssertSectionsConsistent(Navigation nav, Dictionary<Int3, int> truth)
    {
        var seen = new HashSet<int>();
        foreach (var c in truth.Keys)
        {
            int s = nav.SectionOf(c);
            Assert.InRange(s, 0, nav.SectionCount - 1);
            if (!seen.Add(s)) continue;
            Int3 a = nav.SectionAnchor(s);
            Assert.Equal(s, nav.SectionOf(a));
            Assert.Equal(VoxelWorld.ChunkOf(c.X, c.Y, c.Z), VoxelWorld.ChunkOf(a.X, a.Y, a.Z));
            var targets = new HashSet<int>();
            foreach (var l in nav.SectionLinks(s))
            {
                Assert.True(targets.Add(l.ToSection), $"duplicate link {s} -> {l.ToSection}");
                Assert.Equal(s, nav.SectionOf(l.From));
                Assert.Equal(l.ToSection, nav.SectionOf(l.To));
                Assert.True(nav.Rules.TryGetMove(l.From, l.To, out var mv), $"link {l.From} -> {l.To} is no move");
                Assert.Equal(mv.Cost, l.Cost, 3);
            }
        }
    }

    [Theory]
    [InlineData(150, 146, 156)] // the brief's case: x and z chunk borders
    [InlineData(159, 155, 165)] // the same on the y = 160 chunk border
    public void Components_MatchBruteForce_AfterRandomEdits(int surface, int y0, int y1)
    {
        var m = TestWorlds.FlatMap(64, surface);
        var nav = m.Nav;
        var rng = new Random(surface);
        for (int i = 1; i <= 400; i++)
        {
            var c = new Int3(rng.Next(28, 36), rng.Next(y0, y1 + 1), rng.Next(28, 36));
            var (block, dig) = RandomEdit(rng);
            Apply(m, c, block, dig);
            if (i % 20 != 0) continue;
            nav.Update();
            var truth = BruteForce(m);
            AssertSamePartition(truth, nav.ComponentOf, $"after edit {i}");
            AssertSectionsConsistent(nav, truth);
        }
    }

    [Fact] public void StairsAcrossChunkYBorder_Connect()
    {
        var m = TestWorlds.FlatMap(64, 157); // ground feet at y = 158
        for (int x = 15; x <= 25; x++)
            for (int z = 5; z <= 15; z++)
                m.World.SetBlock(x, 162, z, Blocks.WoodPlanks); // upper floor, feet at y = 163
        var nav = m.Nav;
        Int3 ground = new(5, 158, 10), upper = new(20, 163, 10);
        Assert.NotEqual(nav.ComponentOf(ground), nav.ComponentOf(upper));

        for (int i = 0; i <= 4; i++) m.World.SetBlock(10 + i, 158 + i, 10, Blocks.Stairs(BuildMaterial.Wood, Facing.East));
        nav.Update();
        Assert.Equal(nav.ComponentOf(ground), nav.ComponentOf(upper));
        AssertSamePartition(BruteForce(m), nav.ComponentOf, "stairs");
    }

    [Fact] public void LadderAcrossChunkYBorder_Connects()
    {
        var m = TestWorlds.FlatMap(64, 157);
        for (int x = 12; x <= 20; x++)
            for (int z = 5; z <= 15; z++)
                m.World.SetBlock(x, 163, z, Blocks.WoodPlanks); // feet at y = 164
        var nav = m.Nav;
        Int3 ground = new(5, 158, 10), upper = new(16, 164, 10);
        Assert.NotEqual(nav.ComponentOf(ground), nav.ComponentOf(upper));

        for (int y = 158; y <= 163; y++) m.World.SetBlock(11, y, 10, Blocks.Ladder(Facing.East));
        nav.Update();
        Assert.Equal(nav.ComponentOf(ground), nav.ComponentOf(upper));
        AssertSamePartition(BruteForce(m), nav.ComponentOf, "ladder");
    }

    [Fact] public void Wall_SplitsComponent_RemovalRejoins()
    {
        var m = TestWorlds.FlatMap();
        var nav = m.Nav;
        Int3 west = new(10, 151, 10), east = new(50, 151, 40);
        Assert.Equal(nav.ComponentOf(west), nav.ComponentOf(east));

        for (int z = 0; z < 64; z++)
            for (int y = 151; y <= 153; y++) m.World.SetBlock(40, y, z, Blocks.WoodPlanks);
        nav.Update();
        Assert.NotEqual(nav.ComponentOf(west), nav.ComponentOf(east));

        for (int y = 151; y <= 153; y++) m.World.SetBlock(40, y, 32, Blocks.Air);
        nav.Update();
        Assert.Equal(nav.ComponentOf(west), nav.ComponentOf(east));
        Assert.Equal(-1, nav.ComponentOf(new Int3(10, 152, 10)));
        Assert.Equal(-1, nav.SectionOf(new Int3(10, 152, 10)));
    }

    [Fact] public void Preview_PredictsComponentsAfterEdit()
    {
        var m = TestWorlds.FlatMap();
        var nav = m.Nav;
        var rng = new Random(7);
        for (int i = 0; i < 150; i++) // rough up the border area first
        {
            var (block, dig) = RandomEdit(rng);
            Apply(m, new Int3(rng.Next(28, 36), rng.Next(146, 157), rng.Next(28, 36)), block, dig);
        }
        nav.Update();
        for (int i = 0; i < 50; i++)
        {
            var c = new Int3(rng.Next(28, 36), rng.Next(146, 157), rng.Next(28, 36));
            var (block, dig) = RandomEdit(rng);
            int version = nav.Version, before = nav.ComponentOf(new Int3(2, 151, 2));
            var preview = nav.Preview(c, block);
            Assert.Equal(version, nav.Version);
            Assert.Equal(before, nav.ComponentOf(new Int3(2, 151, 2)));
            // Every standable cell lies in y 140..170 here; the preview is read before the edit (Update invalidates it).
            var predicted = new Dictionary<Int3, int>();
            for (int y = 140; y <= 170; y++)
                for (int z = 0; z < 64; z++)
                    for (int x = 0; x < 64; x++)
                        predicted[new(x, y, z)] = preview.ComponentOf(new(x, y, z));

            Apply(m, c, block, dig);
            nav.Update();
            var truth = BruteForce(m);
            AssertSamePartition(truth, nav.ComponentOf, $"edit {i}");
            AssertSamePartition(truth, p => predicted[p], $"preview {i}");
            foreach (var (p, k) in predicted)
                Assert.True(truth.ContainsKey(p) == k >= 0, $"preview {i}: standability of {p}");
        }
    }

    [Fact] public void Preview_SeesEditsNotYetUpdated()
    {
        var m = TestWorlds.FlatMap();
        var nav = m.Nav;
        for (int z = 0; z < 64; z++)
            for (int y = 151; y <= 153; y++)
                if (z != 20) m.World.SetBlock(40, y, z, Blocks.WoodPlanks);
        // Not updated yet: the preview must still see the wall, so closing its one gap splits the map.
        var preview = nav.Preview(new Int3(40, 151, 20), Blocks.WoodPlanks);
        Assert.NotEqual(preview.ComponentOf(new Int3(10, 151, 10)), preview.ComponentOf(new Int3(50, 151, 10)));
    }

    [Fact] public void Preview_IsStale_AfterNextPreviewOrUpdate()
    {
        var m = TestWorlds.FlatMap();
        var nav = m.Nav;
        var first = nav.Preview(new Int3(1, 150, 1), Blocks.Air);
        Assert.True(first.ComponentOf(new Int3(5, 151, 5)) >= 0);
        var second = nav.Preview(new Int3(2, 150, 2), Blocks.Air);
        Assert.Throws<InvalidOperationException>(() => first.ComponentOf(new Int3(5, 151, 5)));
        TestWorlds.Dig(m, 3, 150, 3);
        nav.Update();
        Assert.Throws<InvalidOperationException>(() => second.ComponentOf(new Int3(5, 151, 5)));
    }

    [Fact] public void HomeComponent_IsComponentOfNearestStandable()
    {
        var m = TestWorlds.FlatMap();
        var nav = m.Nav;
        Assert.Equal(nav.ComponentOf(new Int3(10, 151, 10)), nav.HomeComponent(new Int3(10, 155, 10)));
        Assert.Equal(-1, nav.HomeComponent(new Int3(10, 100, 10)));
    }

    [Fact] public void Version_BumpsOnlyOnChange()
    {
        var m = TestWorlds.FlatMap();
        var nav = m.Nav;
        int v = nav.Version;
        nav.Update();
        Assert.Equal(v, nav.Version);

        m.World.SetBlock(20, 60, 20, Blocks.IronOre); // hidden deep in the rock: navigation can't tell
        nav.Update();
        Assert.Equal(v, nav.Version);

        TestWorlds.Dig(m, 20, 150, 20);
        nav.Update();
        Assert.True(nav.Version > v);
        v = nav.Version;
        nav.Update();
        Assert.Equal(v, nav.Version);
    }

    [Fact] public void ColonyMap_Nav_IsLazyAndShared()
    {
        var m = TestWorlds.FlatMap();
        Assert.Same(m.Nav, m.Nav);
    }

    /// <summary>The timing tests, in a collection that runs alone so no other test competes for the cores.</summary>
    [Collection(nameof(Perf))]
    public class Perf(ITestOutputHelper output)
    {
        static bool Optimized => typeof(Navigation).Assembly.GetCustomAttribute<DebuggableAttribute>() is not { IsJITOptimizerDisabled: true };

        const string DebugNote = " [Debug build: limits not checked, run with -c Release]";

        [Fact, Trait("Category", "Perf")] public void Large_BuildUnder3s_Under64MB()
        {
            var map = MapGenerator.Generate(MapSettings.Create(12345, MapSize.Large));
            long before = GC.GetTotalMemory(true);
            var sw = Stopwatch.StartNew();
            var nav = new Navigation(map);
            sw.Stop();
            long bytes = GC.GetTotalMemory(true) - before;
            double mb = bytes / (1024.0 * 1024.0);
            output.WriteLine($"Large build: {sw.Elapsed.TotalMilliseconds:F0} ms, {mb:F1} MB, {nav.SectionCount} sections"
                + (Optimized ? "" : DebugNote));
            GC.KeepAlive(nav);
            if (!Optimized) return;
            Assert.True(sw.Elapsed.TotalMilliseconds < 3000, $"{sw.Elapsed.TotalMilliseconds:F0} ms");
            Assert.True(mb < 64, $"{mb:F1} MB");
        }

        [Fact, Trait("Category", "Perf")] public void Large_SingleEditUpdate_Avg3ms_Preview5ms()
        {
            var map = MapGenerator.Generate(MapSettings.Create(12345, MapSize.Large));
            var nav = map.Nav;
            var rng = new Random(5);
            var update = new List<double>();
            var preview = new List<double>();
            while (update.Count < 200)
            {
                int x = rng.Next(map.World.SizeX), z = rng.Next(map.World.SizeZ);
                int y = nav.Rules.TopStandable(x, z) - 1;
                if (y < 1) continue;
                var cell = new Int3(x, y, z);
                var sw = Stopwatch.StartNew();
                var p = nav.Preview(cell, Blocks.Air);
                p.ComponentOf(cell);
                preview.Add(sw.Elapsed.TotalMilliseconds);

                TestWorlds.Dig(map, x, y, z);
                sw.Restart();
                nav.Update();
                update.Add(sw.Elapsed.TotalMilliseconds);
            }
            output.WriteLine($"Large, 200 surface digs: Update avg {update.Average():F2} ms (max {update.Max():F2}), "
                + $"Preview avg {preview.Average():F2} ms (max {preview.Max():F2})" + (Optimized ? "" : DebugNote));
            if (!Optimized) return;
            Assert.True(update.Average() < 3, $"Update avg {update.Average():F2} ms");
            Assert.True(preview.Average() < 5, $"Preview avg {preview.Average():F2} ms");
        }
    }
}

[CollectionDefinition(nameof(NavigationTests.Perf), DisableParallelization = true)]
public class NavigationPerfCollection;
