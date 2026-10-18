namespace ColonySim.Core.Tests;

public class DiscoveryTests
{
    // Sealed cave: air box x,z 20..24, y 100..103 inside stone (surface 150).
    static VoxelWorld WithCave()
    {
        var w = TestWorlds.SolidBelow();
        for (int x = 20; x <= 24; x++) for (int z = 20; z <= 24; z++) for (int y = 100; y <= 103; y++) w.SetBlock(x, y, z, Blocks.Air, false);
        return w;
    }
    [Fact] public void InitialFlood_DiscoversSky_NotSealedCave() { var w = WithCave(); Discovery.InitialFlood(w); Assert.True(w.IsDiscovered(5, 200, 5)); Assert.False(w.IsDiscovered(22, 101, 22)); }
    [Fact] public void InitialFlood_DiscoversCaveOpenToSky()
    {
        var w = WithCave(); for (int y = 104; y <= 150; y++) w.SetBlock(22, y, 22, Blocks.Air, false);
        Discovery.InitialFlood(w); Assert.True(w.IsDiscovered(20, 100, 20));
    }
    [Fact] public void RevealFrom_DiggingIntoCave_RevealsAllOfIt()
    {
        var w = WithCave(); Discovery.InitialFlood(w);
        w.SetBlock(22, 104, 22, Blocks.Air);
        Assert.True(Discovery.RevealFrom(w, 22, 104, 22) >= 101);
        Assert.True(w.IsDiscovered(22, 104, 22)); Assert.True(w.IsDiscovered(20, 100, 24));
    }
    [Fact] public void RevealFrom_SolidCell_DoesNothing() { var w = WithCave(); Assert.Equal(0, Discovery.RevealFrom(w, 5, 50, 5)); }
    [Fact] public void InitialFlood_DiscoversCaveEnteredSidewaysUnderOverhang()
    {
        var w = TestWorlds.SolidBelow();
        // Cliff: column x<30 is cut down to y=120; a tunnel runs sideways into the cliff face at x>=30.
        for (int x = 0; x < 30; x++) for (int z = 0; z < 64; z++) for (int y = 121; y <= 150; y++) w.SetBlock(x, y, z, Blocks.Air, false);
        for (int x = 30; x < 40; x++) w.SetBlock(x, 125, 10, Blocks.Air, false);
        Discovery.InitialFlood(w);
        Assert.True(w.IsDiscovered(39, 125, 10));
        Assert.False(w.IsDiscovered(41, 125, 10));
    }
}
