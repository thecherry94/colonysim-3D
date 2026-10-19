namespace ColonySim.Core.Tests;

public class CaveTests
{
    static readonly int[] Seeds = { 1, 2, 3 };
    static readonly Dictionary<int, ColonyMap> Maps = new();

    static ColonyMap Map(int seed)
    {
        lock (Maps)
        {
            if (!Maps.TryGetValue(seed, out var m)) Maps[seed] = m = MapGenerator.Generate(MapSettings.Create(seed, MapSize.Small));
            return m;
        }
    }

    const int ShallowMax = 40;

    static bool InShallowBand(VoxelWorld w, TerrainParams t, int x, int y, int z)
    {
        int d = w.GetSurfaceHeight(x, z) - y;
        return d >= t.CaveRoof && d <= ShallowMax;
    }

    [Fact] public void ShallowTunnels_AreTallerThanDeep()
    {
        long shallowRuns = 0, shallowCells = 0, deepRuns = 0, deepCells = 0;
        foreach (int seed in Seeds)
        {
            var m = Map(seed); var w = m.World; var t = m.Settings.Terrain;
            for (int x = 0; x < w.SizeX; x++) for (int z = 0; z < w.SizeZ; z++)
            {
                int h = w.GetSurfaceHeight(x, z);
                if (h <= t.WaterLevel) continue;
                bool inS = false, inD = false;
                for (int y = 6; y < h; y++)
                {
                    bool air = w.GetBlock(x, y, z) == Blocks.Air;
                    bool s = air && InShallowBand(w, t, x, y, z), d = air && y < t.DeepStoneLevel;
                    if (s) { shallowCells++; if (!inS) shallowRuns++; }
                    if (d) { deepCells++; if (!inD) deepRuns++; }
                    inS = s; inD = d;
                }
            }
        }
        Assert.True(shallowRuns > 100 && deepRuns > 100, $"runs {shallowRuns}/{deepRuns}");
        double shallow = (double)shallowCells / shallowRuns, deep = (double)deepCells / deepRuns;
        Assert.True(shallow >= 3.0, $"shallow mean run {shallow:F2}");
        Assert.True(shallow > deep, $"shallow {shallow:F2} vs deep {deep:F2}");
    }

    [Fact] public void ShallowCaves_AreMostlyWalkable()
    {
        foreach (int seed in Seeds)
        {
            var m = Map(seed); var w = m.World; var t = m.Settings.Terrain;
            TestWorlds.RevealAll(w);
            var nav = new NavRules(m.View);
            // Standable cells under the cave roof line (cave interiors, any depth), flooded through real moves.
            bool Cave(Int3 c) => c.Y <= w.GetSurfaceHeight(c.X, c.Z) - t.CaveRoof;
            var comp = new Dictionary<Int3, int>(); var sizes = new List<int>();
            var moves = new Move[NavRules.MaxMoves]; var stack = new Stack<Int3>();
            int total = 0, inBig = 0;
            var shallowCells = new List<Int3>();
            for (int x = 0; x < w.SizeX; x++) for (int z = 0; z < w.SizeZ; z++)
                for (int y = 7; y <= w.GetSurfaceHeight(x, z) - t.CaveRoof; y++)
                    if (InShallowBand(w, t, x, y, z) && nav.IsStandable(new(x, y, z))) shallowCells.Add(new(x, y, z));
            foreach (var start in shallowCells)
            {
                if (comp.ContainsKey(start)) continue;
                int id = sizes.Count, size = 0; comp[start] = id; stack.Push(start);
                while (stack.Count > 0)
                {
                    var c = stack.Pop(); size++;
                    int n = nav.GetMoves(c, moves);
                    for (int i = 0; i < n; i++)
                    {
                        var to = moves[i].To;
                        if (!Cave(to) || comp.ContainsKey(to)) continue;
                        comp[to] = id; stack.Push(to);
                    }
                }
                sizes.Add(size);
            }
            foreach (var c in shallowCells) { total++; if (sizes[comp[c]] >= 300) inBig++; }
            Assert.True(total > 1000, $"seed {seed}: only {total} standable shallow cave cells");
            Assert.True(inBig >= 0.7 * total, $"seed {seed}: {inBig}/{total} = {(double)inBig / total:P0} in components >= 300");
        }
    }

    [Fact] public void ShallowCaveWalls_RichInCoalAndIron()
    {
        foreach (int seed in Seeds)
        {
            var m = Map(seed); var w = m.World; var t = m.Settings.Terrain;
            int solid = 0, ore = 0;
            for (int x = 1; x < w.SizeX - 1; x++) for (int z = 1; z < w.SizeZ - 1; z++)
                for (int y = 7; y < w.GetSurfaceHeight(x, z); y++)
                {
                    if (w.GetBlock(x, y, z) != Blocks.Air || !InShallowBand(w, t, x, y, z)) continue;
                    foreach (var f in Faces)
                    {
                        ushort b = w.GetBlock(x + f.X, y + f.Y, z + f.Z);
                        if (b == Blocks.Air || b == Blocks.Water) continue;
                        solid++;
                        if (b == Blocks.CoalOre || b == Blocks.IronOre) ore++;
                    }
                }
            Assert.True(solid > 2000, $"seed {seed}: only {solid} wall faces");
            Assert.True(ore >= 0.08 * solid, $"seed {seed}: coal+iron share {(double)ore / solid:P1}");
        }
    }

    static readonly Int3[] Faces = { new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1) };

    [Fact] public void CavesStillSealed()
    {
        foreach (int seed in Seeds)
        {
            var m = Map(seed); var w = m.World; var t = m.Settings.Terrain;
            for (int x = 0; x < w.SizeX; x++) for (int z = 0; z < w.SizeZ; z++)
            {
                int h = w.GetSurfaceHeight(x, z);
                if (h >= t.MountainRockLine || h <= t.WaterLevel) continue;
                for (int y = h - t.CaveRoof + 1; y <= h; y++) Assert.NotEqual(Blocks.Air, w.GetBlock(x, y, z));
            }
        }
    }
}
