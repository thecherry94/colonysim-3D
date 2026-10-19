namespace ColonySim.Core.Tests;

public class StartSiteTests
{
    static MapSettings Small(int seed) => new(seed, 256, 256, new TerrainParams());

    static double Dist(int x, int z, int cx, int cz) => Math.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz));

    [Fact] public void Flatten_CoreIsLevel_EdgeBlends()
    {
        var s = Small(3);
        var heights = HeightField.Generate(s).Heights;
        var original = (int[])heights.Clone();
        var (sx, sz) = StartSiteFinder.Find(heights, s);
        int t = StartSiteFinder.Flatten(heights, s, sx, sz);

        int core = 0;
        for (int z = 0; z < 256; z++) for (int x = 0; x < 256; x++)
        {
            double r = Dist(x, z, sx, sz);
            if (r <= StartSiteFinder.CoreRadius) { Assert.Equal(t, heights[x + z * 256]); core++; }
            if (r > 20) Assert.Equal(original[x + z * 256], heights[x + z * 256]);
            if (r > 14) continue;
            foreach (var (nx, nz) in new[] { (x + 1, z), (x, z + 1) })
            {
                if (nx >= 256 || nz >= 256 || Dist(nx, nz, sx, sz) > 14) continue;
                int before = Math.Abs(original[x + z * 256] - original[nx + nz * 256]);
                int after = Math.Abs(heights[x + z * 256] - heights[nx + nz * 256]);
                Assert.True(after <= Math.Max(before, 1) + 1, $"({x},{z})->({nx},{nz}) {before} -> {after}");
            }
        }
        Assert.True(core > 100);
    }

    [Fact] public void Flatten_TargetStaysAboveWater()
    {
        var s = Small(1);
        var heights = new int[256 * 256];
        Array.Fill(heights, s.Terrain.WaterLevel - 3);
        Assert.Equal(s.Terrain.WaterLevel + 2, StartSiteFinder.Flatten(heights, s, 128, 128));
    }

    [Fact] public void Find_IsDeterministic_AvoidsWaterAndMountains()
    {
        for (int seed = 1; seed <= 5; seed++)
        {
            var s = Small(seed);
            var heights = HeightField.Generate(s).Heights;
            var a = StartSiteFinder.Find(heights, s);
            Assert.Equal(a, StartSiteFinder.Find(heights, s));
            for (int z = 0; z < 256; z++) for (int x = 0; x < 256; x++)
            {
                double r = Dist(x, z, a.X, a.Z);
                int h = heights[x + z * 256];
                if (r <= 12) Assert.True(h > s.Terrain.WaterLevel + 1, $"seed {seed}: water within 12");
                if (r <= 8) Assert.True(h < s.Terrain.MountainRockLine, $"seed {seed}: mountain within 8");
            }
        }
    }

    [Fact] public void Find_FallsBackToMapCenter_WhenEverythingIsWater()
    {
        var s = Small(1);
        var heights = new int[256 * 256];
        Array.Fill(heights, s.Terrain.WaterLevel - 3);
        Assert.Equal((128, 128), StartSiteFinder.Find(heights, s));
    }

    [Fact] public void Generated_StartSite_IsClearOfTreesAndBoulders()
    {
        var map = MapGenerator.Generate(Small(1));
        var site = map.StartSite;
        Assert.NotEqual(default, site);
        var w = map.World;
        Assert.Equal(site.Level - 1, w.GetSurfaceHeight(site.X, site.Z));
        int r = StartSiteFinder.ClearRadius;
        for (int z = site.Z - r; z <= site.Z + r; z++) for (int x = site.X - r; x <= site.X + r; x++)
        {
            if (Dist(x, z, site.X, site.Z) > r) continue;
            for (int y = site.Level; y <= site.Level + 12; y++)
            {
                ushort b = w.GetBlock(x, y, z);
                Assert.False(b == Blocks.OakLog || b == Blocks.PineLog || b == Blocks.OakLeaves || b == Blocks.PineLeaves || b == Blocks.Cobblestone,
                    $"block {Blocks.Registry.Get(b).Name} at ({x},{y},{z})");
            }
        }
    }
}
