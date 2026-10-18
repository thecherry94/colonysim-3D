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
