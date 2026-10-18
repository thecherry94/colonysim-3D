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
}
