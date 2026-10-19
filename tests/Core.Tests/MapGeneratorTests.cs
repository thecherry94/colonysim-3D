namespace ColonySim.Core.Tests;

public class MapGeneratorTests
{
    static readonly MapSettings S = new(42, 128, 128, new TerrainParams());
    static readonly Lazy<ColonyMap> M = new(() => MapGenerator.Generate(S));

    [Fact] public void IsDeterministic() => Assert.Equal(TestWorlds.WorldHash(M.Value.World), TestWorlds.WorldHash(MapGenerator.Generate(S).World));
    [Fact] public void SeedsDiffer() => Assert.NotEqual(TestWorlds.WorldHash(M.Value.World), TestWorlds.WorldHash(MapGenerator.Generate(S with { Seed = 43 }).World));
    [Fact] public void Caves_Exist_AreHidden_AndStaySealedOutsideMountains()
    {
        var w = M.Value.World; var t = S.Terrain; int hidden = 0;
        for (int x = 0; x < 128; x++) for (int z = 0; z < 128; z++)
        {
            int h = w.GetSurfaceHeight(x, z);
            for (int y = 6; y < h; y++) if (w.GetBlock(x, y, z) == Blocks.Air && !w.IsDiscovered(x, y, z)) hidden++;
            if (h < t.MountainRockLine && h > t.WaterLevel)
                for (int y = h - t.CaveRoof + 1; y <= h; y++) Assert.NotEqual(Blocks.Air, w.GetBlock(x, y, z));
        }
        Assert.True(hidden > 500);
    }
    [Fact] public void Ores_AppearOnlyInTheirBands()
    {
        var w = M.Value.World; var seen = new HashSet<ushort>();
        for (int x = 0; x < 128; x++) for (int z = 0; z < 128; z++) for (int y = 0; y < 256; y++)
        {
            var b = w.GetBlock(x, y, z); var band = OrePlacer.Bands.FirstOrDefault(o => o.Block == b);
            if (band is null) continue; seen.Add(b);
            // Cave-side iron may climb above its band, up to the surface minus the cave roof.
            int max = b == Blocks.IronOre ? Math.Max(band.MaxY, w.GetSurfaceHeight(x, z) - S.Terrain.CaveRoof) : band.MaxY;
            Assert.InRange(y, band.MinY, max);
        }
        Assert.Equal(OrePlacer.Bands.Count, seen.Count);
    }
    [Fact] public void Trees_StandOnGround()
    {
        var w = M.Value.World; int trunks = 0;
        for (int x = 0; x < 128; x++) for (int z = 0; z < 128; z++) for (int y = 1; y < 256; y++)
        {
            var b = w.GetBlock(x, y, z); var below = w.GetBlock(x, y - 1, z);
            if ((b == Blocks.OakLog || b == Blocks.PineLog) && below != b) { trunks++; Assert.True(below == Blocks.Grass || below == Blocks.Dirt); }
        }
        Assert.True(trunks > 10);
    }
    [Fact] public void LeavesNoDirtyChunks()
    {
        var dirty = new List<Int3>(); MapGenerator.Generate(new MapSettings(3, 64, 64, new TerrainParams())).World.ConsumeDirtyChunks(dirty);
        Assert.Empty(dirty);
    }
    [Fact] public void ReportsProgress_EndingAtOne()
    {
        var reports = new List<GenerationProgress>();
        MapGenerator.Generate(new MapSettings(1, 64, 64, new TerrainParams()), new SyncProgress<GenerationProgress>(reports.Add));
        Assert.Equal(1f, reports[^1].Fraction);
    }
    [Fact, Trait("Category", "Perf")] public void Large_GeneratesUnder30s()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew(); MapGenerator.Generate(MapSettings.Create(7, MapSize.Large));
        Assert.True(sw.Elapsed.TotalSeconds < 30, $"{sw.Elapsed.TotalSeconds:F1}s");
    }
}
