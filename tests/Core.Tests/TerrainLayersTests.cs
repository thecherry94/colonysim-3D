namespace ColonySim.Core.Tests;

public class TerrainLayersTests
{
    static TerrainLayers L(int seed = 1) => new(MapSettings.Create(seed, MapSize.Small));
    [Fact] public void Layers_TopToBottom()
    {
        var l = L();
        Assert.Equal(Blocks.Air, l.BaseMaterial(10, 151, 10, 150));
        Assert.Equal(Blocks.Grass, l.BaseMaterial(10, 150, 10, 150));
        Assert.Equal(Blocks.Dirt, l.BaseMaterial(10, 149, 10, 150));
        Assert.Equal(Blocks.Stone, l.BaseMaterial(10, 140, 10, 150));
        Assert.Equal(Blocks.DeepStone, l.BaseMaterial(10, 40, 10, 150));
        Assert.Equal(Blocks.Bedrock, l.BaseMaterial(10, 0, 10, 150));
    }
    [Fact] public void ShoreColumns_AreSand() { var l = L(); Assert.Equal(Blocks.Sand, l.BaseMaterial(5, 131, 5, 131)); Assert.Equal(Blocks.Sand, l.BaseMaterial(5, 129, 5, 131)); }
    [Fact] public void HighColumns_AreBareStone() => Assert.Equal(Blocks.Stone, L().BaseMaterial(5, 180, 5, 180));
    [Fact] public void IsDeterministic()
    {
        var a = L(9); var b = L(9);
        for (int x = 0; x < 40; x++) for (int y = 0; y < 160; y += 3) Assert.Equal(a.BaseMaterial(x, y, 7, 150), b.BaseMaterial(x, y, 7, 150));
    }
    [Fact] public void MapSettings_Create_UsesSize() { var s = MapSettings.Create(3, MapSize.Large); Assert.Equal(512, s.SizeX); Assert.Equal(512, s.SizeZ); Assert.Equal(130, s.Terrain.WaterLevel); }
}
