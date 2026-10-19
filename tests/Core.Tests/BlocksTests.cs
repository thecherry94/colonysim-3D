using ColonySim.Core;

namespace ColonySim.Core.Tests;

public class BlocksTests
{
    static BlockDef D(ushort id) => Blocks.Registry.Get(id);

    [Fact] public void Air_IsIdZero_WithNoShape() { Assert.Equal(0, Blocks.Air); Assert.Equal(BlockShape.None, D(Blocks.Air).Shape); Assert.False(D(Blocks.Air).IsSolid); }
    [Fact] public void Bedrock_IsSolidButNotDiggable() { Assert.True(D(Blocks.Bedrock).IsSolid); Assert.False(D(Blocks.Bedrock).IsDiggable); }
    [Fact] public void Water_IsLiquid_NotSolid_NotDiggable() { var w = D(Blocks.Water); Assert.True(w.IsLiquid); Assert.False(w.IsSolid); Assert.False(w.IsDiggable); Assert.False(w.Occludes); }
    [Fact] public void Grass_UsesTopSideBottomTextures()
    {
        var g = D(Blocks.Grass); var r = Blocks.Registry;
        Assert.Equal(r.TextureIndex("grass_top"), g.TexTop);
        Assert.Equal(r.TextureIndex("grass_side"), g.TexSide);
        Assert.Equal(r.TextureIndex("dirt"), g.TexBottom);
    }
    [Fact] public void Stairs_OneBlockPerMaterialAndFacing()
    {
        var ids = new HashSet<ushort>();
        foreach (var m in Enum.GetValues<BuildMaterial>())
            foreach (var f in Enum.GetValues<Facing>())
            {
                var d = D(Blocks.Stairs(m, f));
                Assert.Equal(BlockShape.Stairs, d.Shape); Assert.Equal(f, d.Facing);
                Assert.True(d.IsSolid); Assert.False(d.Occludes);
                ids.Add(d.Id);
            }
        Assert.Equal(8, ids.Count);
    }
    [Fact] public void BuildBlock_MapsMaterials() { Assert.Equal(Blocks.WoodPlanks, Blocks.BuildBlock(BuildMaterial.Wood)); Assert.Equal(Blocks.StoneBricks, Blocks.BuildBlock(BuildMaterial.Stone)); }
    [Fact] public void Names_AreUnique_AndLookupRoundTrips()
    {
        foreach (var d in Blocks.Registry.All) { Assert.True(Blocks.Registry.TryGetByName(d.Name, out var id)); Assert.Equal(d.Id, id); }
        Assert.Equal(Blocks.Registry.All.Count, Blocks.Registry.All.Select(d => d.Name).Distinct().Count());
    }
    [Fact] public void TextureNames_AreUnique() => Assert.Equal(Blocks.Registry.TextureNames.Count, Blocks.Registry.TextureNames.Distinct().Count());
    [Fact] public void FacingOffsets() { Assert.Equal(new Int3(0,0,-1), Facing.North.Offset()); Assert.Equal(new Int3(1,0,0), Facing.East.Offset()); }

    [Fact] public void Registry_Has99Blocks() => Assert.Equal(99, Blocks.Registry.All.Count);
    [Fact] public void Facing_ClockwiseAndOpposite()
    { Assert.Equal(Facing.East, Facing.North.Clockwise()); Assert.Equal(Facing.North, Facing.West.Clockwise()); Assert.Equal(Facing.South, Facing.North.Opposite()); Assert.Equal(Facing.West, FacingExtensions.FromOffset(-1, 0)); }
    [Fact] public void Facing_FromOffset_RejectsNonUnit() { Assert.Throws<ArgumentException>(() => FacingExtensions.FromOffset(1, 1)); Assert.Throws<ArgumentException>(() => FacingExtensions.FromOffset(0, 0)); }
    [Fact] public void NaturalSlopes_OnePerBaseShapeFacing()
    {
        var ids = new HashSet<ushort>();
        foreach (var b in new[] { Blocks.Grass, Blocks.Dirt, Blocks.Sand, Blocks.Stone })
            foreach (var s in new[] { BlockShape.Ramp, BlockShape.CornerOuter, BlockShape.CornerInner })
                foreach (var f in Enum.GetValues<Facing>())
                {
                    var d = Blocks.Registry.Get(Blocks.NaturalSlope(b, s, f));
                    Assert.Equal(s, d.Shape); Assert.Equal(f, d.Facing); Assert.True(d.IsSolid); Assert.False(d.Occludes);
                    Assert.True(d.IsNatural); Assert.True(d.IsSlope); Assert.Equal(b, d.BaseBlock);
                    Assert.Equal(Blocks.Registry.Get(b).TexTop, d.TexTop); ids.Add(d.Id);
                }
        Assert.Equal(48, ids.Count);
    }
    [Fact] public void NaturalSlope_RejectsBadArguments()
    {
        Assert.Throws<ArgumentException>(() => Blocks.NaturalSlope(Blocks.DeepStone, BlockShape.Ramp, Facing.North));
        Assert.Throws<ArgumentException>(() => Blocks.NaturalSlope(Blocks.Grass, BlockShape.Stairs, Facing.North));
    }
    [Fact] public void SlopeBaseFor_MapsTopBlocks() { Assert.Equal(Blocks.Grass, Blocks.SlopeBaseFor(Blocks.Grass)); Assert.Equal(Blocks.Stone, Blocks.SlopeBaseFor(Blocks.CoalOre)); Assert.Equal(Blocks.Stone, Blocks.SlopeBaseFor(Blocks.DeepStone)); }
    [Fact] public void AscendsToward_StairsRampsAndCorners()
    {
        var r = Blocks.Registry;
        Assert.True(r.Get(Blocks.BuiltRamp(BuildMaterial.Wood, Facing.East)).AscendsToward(Facing.East));
        Assert.False(r.Get(Blocks.BuiltRamp(BuildMaterial.Wood, Facing.East)).AscendsToward(Facing.North));
        var corner = r.Get(Blocks.NaturalSlope(Blocks.Grass, BlockShape.CornerInner, Facing.North));
        Assert.True(corner.AscendsToward(Facing.North)); Assert.True(corner.AscendsToward(Facing.East)); Assert.False(corner.AscendsToward(Facing.South));
        Assert.False(r.Get(Blocks.Stone).AscendsToward(Facing.North));
    }
    [Fact] public void Ladder_IsNotSolid_ButDiggable_NotNatural()
    { var d = Blocks.Registry.Get(Blocks.Ladder(Facing.West)); Assert.Equal(BlockShape.Ladder, d.Shape); Assert.False(d.IsSolid); Assert.True(d.IsDiggable); Assert.False(d.IsNatural); Assert.Equal(Facing.West, d.Facing); Assert.True(d.IsLadder); }
    [Fact] public void CarvedStairs_MapDugMaterial()
    {
        Assert.Equal("carved_dirt_stairs_north", Blocks.Registry.Get(Blocks.CarvedStairs(Blocks.Grass, Facing.North)).Name);
        Assert.Equal("carved_stone_stairs_east", Blocks.Registry.Get(Blocks.CarvedStairs(Blocks.IronOre, Facing.East)).Name);
        Assert.Equal("carved_deep_stone_stairs_west", Blocks.Registry.Get(Blocks.CarvedStairs(Blocks.DeepStone, Facing.West)).Name);
        Assert.Equal(BlockShape.Stairs, Blocks.Registry.Get(Blocks.CarvedStairs(Blocks.Dirt, Facing.South)).Shape);
    }
    [Theory]
    [InlineData("grass", 1.0f, true)] [InlineData("stone", 2.5f, true)] [InlineData("deep_stone", 3.5f, true)] [InlineData("iron_ore", 3.0f, true)]
    [InlineData("oak_log", 2.0f, false)] [InlineData("oak_leaves", 0.5f, false)] [InlineData("wood_planks", 1.5f, false)] [InlineData("cobblestone", 2.5f, true)]
    [InlineData("stone_ramp_north", 3.0f, false)] [InlineData("ladder_east", 1.0f, false)] [InlineData("dirt_corner_inner_west", 1.0f, true)]
    public void HardnessAndNatural(string name, float hardness, bool natural)
    { Assert.True(Blocks.Registry.TryGetByName(name, out var id)); var d = Blocks.Registry.Get(id); Assert.Equal(hardness, d.Hardness); Assert.Equal(natural, d.IsNatural); }
}
