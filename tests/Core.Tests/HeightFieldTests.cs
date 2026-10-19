namespace ColonySim.Core.Tests;

public class HeightFieldTests
{
    static MapSettings S(int seed = 42, TerrainParams? t = null) => new(seed, 128, 128, t ?? new TerrainParams());
    [Fact] public void IsDeterministic() => Assert.Equal(HeightField.Generate(S()).Heights, HeightField.Generate(S()).Heights);
    [Fact] public void SeedsDiffer() => Assert.NotEqual(HeightField.Generate(S(1)).Heights, HeightField.Generate(S(2)).Heights);
    [Fact] public void StaysInBounds() { foreach (var h in HeightField.Generate(S()).Heights) Assert.InRange(h, 122, 236); }
    [Fact] public void River_CrossesWholeMap()
    {
        var s = S(); var r = HeightField.Generate(s);
        for (int a = 0; a < 128; a++)
        {
            bool wet = false;
            for (int b = 0; b < 128 && !wet; b++) { int x = r.RiverAlongX ? a : b, z = r.RiverAlongX ? b : a; wet = r.Heights[x + z * 128] < s.Terrain.WaterLevel; }
            Assert.True(wet, $"no water at {a}");
        }
    }
    [Fact] public void River_CrossesWholeMap_ManySeeds()
    {
        for (int seed = 0; seed < 40; seed++)
        {
            var s = S(seed); var r = HeightField.Generate(s);
            for (int a = 0; a < 128; a++)
            {
                bool wet = false;
                for (int b = 0; b < 128 && !wet; b++) { int x = r.RiverAlongX ? a : b, z = r.RiverAlongX ? b : a; wet = r.Heights[x + z * 128] < s.Terrain.WaterLevel; }
                Assert.True(wet, $"seed {seed}: no water at {a}");
            }
        }
    }
    [Fact] public void NoRiver_HighBase_IsDry() { var t = new TerrainParams { HasRiver = false, BaseHeight = 160 }; Assert.All(HeightField.Generate(S(t: t)).Heights, h => Assert.True(h > t.WaterLevel)); }
    [Fact] public void HighCoverage_MakesMountainsAboveRockLine() { var t = new TerrainParams { MountainCoverage = 0.6f }; Assert.True(HeightField.Generate(S(t: t)).Heights.Max() >= t.MountainRockLine); }
    [Fact] public void River_WidthMeasuredAcrossFlow()
    {
        int steep = 0;
        for (int seed = 1; seed <= 12; seed++)
        {
            // High flat land keeps bank cells from rounding down to bed level, so the bed run is the river's own width.
            var s = new MapSettings(seed, 256, 256, new TerrainParams { BaseHeight = 200, HillAmplitude = 0f, MountainCoverage = 0f }); var r = HeightField.Generate(s);
            int n = 256, w = s.Terrain.RiverWidth;
            int H(int a, int b) => r.RiverAlongX ? r.Heights[a + b * n] : r.Heights[b + a * n];
            // Bed run across the flow: the cells at the column's lowest height. Its midpoint tracks the river center.
            var lo = new int[n]; var hi = new int[n];
            for (int a = 0; a < n; a++)
            {
                int min = int.MaxValue; for (int b = 0; b < n; b++) min = Math.Min(min, H(a, b));
                lo[a] = -1;
                for (int b = 0; b < n; b++) if (H(a, b) == min) { if (lo[a] < 0) lo[a] = b; hi[a] = b; }
            }
            for (int a = 6; a < n - 6; a++)
            {
                double slope = ((lo[a + 4] + hi[a + 4]) - (lo[a - 4] + hi[a - 4])) / 16.0;
                if (Math.Abs(slope) <= 0.5) continue;
                // Only where the river runs straight: the midpoints are a crude slope estimate, and bends skew it.
                double before = ((lo[a] + hi[a]) - (lo[a - 4] + hi[a - 4])) / 8.0, after = ((lo[a + 4] + hi[a + 4]) - (lo[a] + hi[a])) / 8.0;
                if (Math.Abs(before - after) > 0.15) continue;
                steep++;
                // The bank's flat foot rounds a cell or so per side down to bed height, hence the 25% slack above the ideal width (the old along-axis measure falls well below it).
                int run = hi[a] - lo[a] + 1;
                double across = w * Math.Sqrt(1 + slope * slope);
                Assert.True(run <= Math.Ceiling(across * 1.25) + 1, $"seed {seed} a={a}: run {run}, slope {slope:F2}");
                Assert.True(run >= across - 1.5, $"seed {seed} a={a}: run {run} too narrow for slope {slope:F2}");
            }
        }
        Assert.True(steep > 50, $"only {steep} steep samples");
    }
    [Fact] public void FillChunkColumn_LayersAndWater()
    {
        var s = S(); var r = HeightField.Generate(s); var w = new VoxelWorld(128, 128); var l = new TerrainLayers(s);
        for (int cx = 0; cx < 4; cx++) for (int cz = 0; cz < 4; cz++) ColumnFiller.FillChunkColumn(w, l, s, r.Heights, cx, cz);
        for (int x = 0; x < 128; x += 7) for (int z = 0; z < 128; z += 7)
        {
            int h = r.Heights[x + z * 128]; Assert.Equal(h, w.GetSurfaceHeight(x, z));
            Assert.Equal(l.BaseMaterial(x, h, z, h), w.GetBlock(x, h, z));
            Assert.Equal(h < s.Terrain.WaterLevel ? Blocks.Water : Blocks.Air, w.GetBlock(x, h + 1, z));
            Assert.Equal(Blocks.Air, w.GetBlock(x, Math.Max(h, s.Terrain.WaterLevel) + 1, z));
        }
    }
}
