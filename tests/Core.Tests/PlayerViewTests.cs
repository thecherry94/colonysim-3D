namespace ColonySim.Core.Tests;

public class PlayerViewTests
{
    static PlayerView View(VoxelWorld w) => new(w, new TerrainLayers(MapSettings.Create(1, MapSize.Small)));
    [Fact] public void HiddenCave_ShowsGroundMaterial()
    {
        var w = TestWorlds.SolidBelow(); w.SetBlock(5, 100, 5, Blocks.Air, false); w.SetBlock(6, 149, 6, Blocks.Air, false);
        for (int x = 0; x < 64; x++) for (int z = 0; z < 64; z++) w.SetSurfaceHeight(x, z, 150);
        Discovery.InitialFlood(w); var v = View(w);
        Assert.Equal(Blocks.Stone, v.GetVisibleBlock(5, 100, 5)); Assert.Equal(Blocks.Dirt, v.GetVisibleBlock(6, 149, 6));
    }
    [Fact] public void FogOff_ShowsActualAir() { var w = TestWorlds.SolidBelow(); w.SetBlock(5, 100, 5, Blocks.Air, false); var v = View(w); v.FogEnabled = false; Assert.Equal(Blocks.Air, v.GetVisibleBlock(5, 100, 5)); }
    [Fact] public void DiscoveredSky_IsAir() { var w = TestWorlds.SolidBelow(); Discovery.InitialFlood(w); Assert.Equal(Blocks.Air, View(w).GetVisibleBlock(5, 200, 5)); }
    [Fact] public void OutOfBounds_IsAir() => Assert.Equal(Blocks.Air, View(TestWorlds.SolidBelow()).GetVisibleBlock(-1, 100, 5));
}
