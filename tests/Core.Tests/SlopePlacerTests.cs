namespace ColonySim.Core.Tests;

public class SlopePlacerTests
{
    static readonly MapSettings S = new(1, 64, 64, new TerrainParams());

    static void Raise(VoxelWorld w, int x0, int z0, int x1, int z1, int by = 1)
    {
        for (int z = z0; z < z1; z++) for (int x = x0; x < x1; x++)
        {
            int h = w.GetSurfaceHeight(x, z);
            for (int y = h + 1; y <= h + by; y++) w.SetBlock(x, y, z, Blocks.Stone, markDirty: false);
            w.SetSurfaceHeight(x, z, h + by);
        }
    }

    static ushort Slope(BlockShape shape, Facing f) => Blocks.NaturalSlope(Blocks.Stone, shape, f);

    [Fact] public void Slope_OneStep_PlacesRampFacingUp()
    {
        var w = TestWorlds.SolidBelow(64, 150);
        Raise(w, 20, 0, 64, 64);
        SlopePlacer.Place(w, S);
        Assert.Equal(Slope(BlockShape.Ramp, Facing.East), w.GetBlock(19, 151, 10));
        Assert.Equal(Blocks.Air, w.GetBlock(18, 151, 10));
        Assert.Equal(Blocks.Air, w.GetBlock(25, 152, 10));
    }

    [Fact] public void Slope_RampsFaceEveryDirection()
    {
        var w = TestWorlds.SolidBelow(64, 150);
        Raise(w, 20, 20, 30, 30);
        SlopePlacer.Place(w, S);
        Assert.Equal(Slope(BlockShape.Ramp, Facing.West), w.GetBlock(30, 151, 25));
        Assert.Equal(Slope(BlockShape.Ramp, Facing.South), w.GetBlock(25, 151, 19));
        Assert.Equal(Slope(BlockShape.Ramp, Facing.North), w.GetBlock(25, 151, 30));
    }

    [Fact] public void Slope_OuterAndInnerCorners()
    {
        var sq = TestWorlds.SolidBelow(64, 150);
        Raise(sq, 20, 20, 30, 30); // 10x10 square
        SlopePlacer.Place(sq, S);
        // Outside its NE corner only the diagonal (29, 20) touches the cell's SW corner.
        Assert.Equal(Slope(BlockShape.CornerOuter, Facing.South), sq.GetBlock(30, 151, 19));
        // Outside its SW corner the diagonal (20, 29) touches the cell's NE corner.
        Assert.Equal(Slope(BlockShape.CornerOuter, Facing.North), sq.GetBlock(19, 151, 30));

        var l = TestWorlds.SolidBelow(64, 150);
        Raise(l, 20, 20, 30, 30);
        Raise(l, 30, 20, 40, 25); // an arm east of the square's north half: notch at (30, 25)
        SlopePlacer.Place(l, S);
        // N, W and NW are raised, SE is open: the N and W sides are fully raised.
        Assert.Equal(Slope(BlockShape.CornerInner, Facing.West), l.GetBlock(30, 151, 25));
    }

    [Fact] public void Slope_TwoBlockCliff_StaysSharp()
    {
        var w = TestWorlds.SolidBelow(64, 150);
        Raise(w, 20, 0, 64, 64, by: 2);
        SlopePlacer.Place(w, S);
        AssertNoSlopes(w);
    }

    [Fact] public void Slope_NotNextToTreeTrunk_NotInWater()
    {
        var w = TestWorlds.SolidBelow(64, 150);
        Raise(w, 20, 10, 21, 11);
        for (int y = 152; y <= 156; y++) w.SetBlock(20, y, 10, Blocks.OakLog, markDirty: false);
        SlopePlacer.Place(w, S);
        AssertNoSlopes(w);

        // Control: the same step without the trunk does get slopes around it.
        var c = TestWorlds.SolidBelow(64, 150);
        Raise(c, 20, 10, 21, 11);
        SlopePlacer.Place(c, S);
        Assert.Equal(Slope(BlockShape.Ramp, Facing.East), c.GetBlock(19, 151, 10));

        // Water over the lower ground: no slope in the water.
        var wet = TestWorlds.SolidBelow(64, 150);
        Raise(wet, 20, 0, 64, 64);
        for (int z = 0; z < 64; z++) for (int x = 0; x < 20; x++) wet.SetBlock(x, 151, z, Blocks.Water, markDirty: false);
        SlopePlacer.Place(wet, S);
        AssertNoSlopes(wet);
    }

    [Fact] public void Slope_UsesSurfaceMaterial()
    {
        var w = TestWorlds.SolidBelow(64, 150, Blocks.Dirt);
        for (int z = 0; z < 64; z++) for (int x = 0; x < 64; x++) w.SetBlock(x, 150, z, Blocks.Grass, markDirty: false);
        Raise(w, 20, 0, 64, 64);
        for (int z = 0; z < 64; z++) for (int x = 20; x < 64; x++) w.SetBlock(x, 151, z, Blocks.Sand, markDirty: false);
        SlopePlacer.Place(w, S);
        Assert.Equal(Blocks.NaturalSlope(Blocks.Grass, BlockShape.Ramp, Facing.East), w.GetBlock(19, 151, 10));
    }

    [Fact] public void Generated_SlopesOnlyOnOpenSurface()
    {
        var w = MapGenerator.Generate(new MapSettings(1, 256, 256, new TerrainParams())).World;
        int slopes = 0;
        for (int z = 0; z < 256; z++) for (int x = 0; x < 256; x++)
        {
            int h = w.GetSurfaceHeight(x, z);
            var def = Blocks.Registry.Get(w.GetBlock(x, h + 1, z));
            if (!def.IsSlope) continue;
            slopes++;
            var below = Blocks.Registry.Get(w.GetBlock(x, h, z));
            Assert.True(below.IsNatural && below.IsSolid);
            ushort above = w.GetBlock(x, h + 2, z);
            Assert.True(above == Blocks.Air || above == Blocks.OakLeaves || above == Blocks.PineLeaves, $"above slope ({x},{h + 1},{z})");
        }
        Assert.True(slopes > 100, $"{slopes} slopes");
    }

    static void AssertNoSlopes(VoxelWorld w)
    {
        for (int z = 0; z < w.SizeZ; z++) for (int x = 0; x < w.SizeX; x++) for (int y = 148; y < 160; y++)
            Assert.False(Blocks.Registry.Get(w.GetBlock(x, y, z)).IsSlope, $"slope at ({x},{y},{z})");
    }
}
