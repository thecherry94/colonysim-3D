using System.Numerics;

namespace ColonySim.Core.Tests;

public class MeshingTests
{
    static PlayerView V(VoxelWorld w) { var v = new PlayerView(w, new TerrainLayers(MapSettings.Create(1, MapSize.Small))); return v; }
    static int Faces(MeshBuffers b) => b.Indices.Count / 6;

    [Fact] public void SingleBlock_SixFaces() { var w = new VoxelWorld(32, 32); w.SetBlock(5, 5, 5, Blocks.Stone); Discovery.InitialFlood(w); var m = ChunkMesher.Build(V(w), new Int3(0, 0, 0)); Assert.Equal(6, Faces(m.Opaque)); Assert.Equal(24, m.Opaque.Vertices.Count); }
    [Fact] public void AdjacentBlocks_CullSharedFaces() { var w = new VoxelWorld(32, 32); w.SetBlock(5, 5, 5, Blocks.Stone); w.SetBlock(6, 5, 5, Blocks.Stone); Discovery.InitialFlood(w); Assert.Equal(10, Faces(ChunkMesher.Build(V(w), new Int3(0, 0, 0)).Opaque)); }
    [Fact] public void Triangles_AreClockwiseFromOutside()
    {
        var w = new VoxelWorld(32, 32); w.SetBlock(5, 5, 5, Blocks.Stone); var b = ChunkMesher.Build(V(w), new Int3(0, 0, 0)).Opaque;
        for (int i = 0; i < b.Indices.Count; i += 3)
        {
            var (a, c, d) = (b.Vertices[b.Indices[i]], b.Vertices[b.Indices[i + 1]], b.Vertices[b.Indices[i + 2]]);
            Assert.True(Vector3.Dot(Vector3.Cross(c - a, d - a), b.Normals[b.Indices[i]]) < 0);
        }
    }
    [Fact] public void GrassTop_UsesGrassTopLayer()
    {
        var w = new VoxelWorld(32, 32); w.SetBlock(5, 5, 5, Blocks.Grass); Discovery.InitialFlood(w); var b = ChunkMesher.Build(V(w), new Int3(0, 0, 0)).Opaque;
        int top = b.Normals.FindIndex(n => n == Vector3.UnitY); Assert.Equal(Blocks.Registry.TextureIndex("grass_top"), (int)b.Uv2s[top].X);
    }
    [Fact] public void HiddenCave_HasNoInternalFaces_UntilFogOff()
    {
        var w = TestWorlds.SolidBelow(32, 60); w.SetBlock(10, 30, 10, Blocks.Air, false); Discovery.InitialFlood(w);
        var v = V(w); int fogged = Faces(ChunkMesher.Build(v, new Int3(0, 0, 0)).Opaque);
        v.FogEnabled = false; Assert.Equal(fogged + 6, Faces(ChunkMesher.Build(v, new Int3(0, 0, 0)).Opaque));
    }
    [Fact] public void Water_FacesAirOnly() { var w = new VoxelWorld(32, 32); w.SetBlock(5, 5, 5, Blocks.Water); w.SetBlock(5, 4, 5, Blocks.Stone); Discovery.InitialFlood(w); Assert.Equal(5, Faces(ChunkMesher.Build(V(w), new Int3(0, 0, 0)).Liquid)); }
    [Fact] public void MapEdge_EmitsSideFaces() { var w = new VoxelWorld(32, 32); w.SetBlock(0, 5, 5, Blocks.Stone); Assert.Contains(-Vector3.UnitX, ChunkMesher.Build(V(w), new Int3(0, 0, 0)).Opaque.Normals); }
    [Fact] public void AO_DarkensCornerNextToWall()
    {
        var w = new VoxelWorld(32, 32); w.SetBlock(5, 5, 5, Blocks.Stone); w.SetBlock(6, 6, 5, Blocks.Stone);
        var b = ChunkMesher.Build(V(w), new Int3(0, 0, 0)).Opaque; var tops = Enumerable.Range(0, b.Normals.Count).Where(i => b.Normals[i] == Vector3.UnitY && b.Vertices[i].Y == 6);
        Assert.Contains(tops, i => b.Uv2s[i].Y < 1f);
    }
    [Fact] public void Cap_OneQuadPerSolidColumn_NoneWhereOpen()
    {
        var w = TestWorlds.SolidBelow(32, 60); Discovery.InitialFlood(w); var v = V(w);
        Assert.Equal(32 * 32, Faces(CapMesher.Build(v, 0, 0, 40).Opaque));
        Assert.True(CapMesher.Build(v, 0, 0, 60).Opaque.IsEmpty);
        w.SetBlock(3, 40, 3, Blocks.Air); Discovery.RevealFrom(w, 3, 40, 3);
        Assert.Equal(32 * 32 - 1, Faces(CapMesher.Build(v, 0, 0, 40).Opaque));
    }

    [Fact] public void BlockOnWorldFloor_HasNoBottomFace()
    {
        var w = new VoxelWorld(32, 32); w.SetBlock(5, 0, 5, Blocks.Stone); Discovery.InitialFlood(w);
        var b = ChunkMesher.Build(V(w), new Int3(0, 0, 0)).Opaque;
        Assert.Equal(5, Faces(b)); Assert.DoesNotContain(-Vector3.UnitY, b.Normals);
    }
    [Fact] public void UniformAirChunk_IsEmpty()
    {
        var w = TestWorlds.SolidBelow(32, 60); Discovery.InitialFlood(w); var m = ChunkMesher.Build(V(w), new Int3(0, 3, 0));
        Assert.True(m.Opaque.IsEmpty); Assert.True(m.Liquid.IsEmpty);
    }
    [Fact] public void SideFaces_HaveVZeroAtTop()
    {
        var w = new VoxelWorld(32, 32); w.SetBlock(5, 5, 5, Blocks.Stone); Discovery.InitialFlood(w); var b = ChunkMesher.Build(V(w), new Int3(0, 0, 0)).Opaque;
        for (int i = 0; i < b.Vertices.Count; i++)
            if (b.Normals[i].Y == 0) Assert.Equal(b.Vertices[i].Y == 6 ? 0f : 1f, b.Uvs[i].Y);
    }
    [Fact] public void Stairs_AreTwoBoxes_WithUpperHalfTowardFacing()
    {
        var w = new VoxelWorld(32, 32); w.SetBlock(5, 5, 5, Blocks.Stairs(BuildMaterial.Wood, Facing.East)); Discovery.InitialFlood(w);
        var b = ChunkMesher.Build(V(w), new Int3(0, 0, 0)).Opaque;
        // slab: bottom + 4 sides + uncovered half top; upper: top + back + 2 half sides + riser
        Assert.Equal(11, Faces(b));
        var tops = Enumerable.Range(0, b.Vertices.Count).Where(i => b.Normals[i] == Vector3.UnitY).Select(i => b.Vertices[i]).ToList();
        Assert.All(tops.Where(p => p.Y == 6), p => Assert.InRange(p.X, 5.5f, 6f));
        Assert.All(tops.Where(p => p.Y == 5.5f), p => Assert.InRange(p.X, 5f, 5.5f));
        for (int i = 0; i < b.Indices.Count; i += 3)
        {
            var (p, q, r) = (b.Vertices[b.Indices[i]], b.Vertices[b.Indices[i + 1]], b.Vertices[b.Indices[i + 2]]);
            Assert.True(Vector3.Dot(Vector3.Cross(q - p, r - p), b.Normals[b.Indices[i]]) < 0);
        }
    }
    [Fact] public void Stairs_BoundaryFacesCullAgainstOccluders()
    {
        var w = new VoxelWorld(32, 32); w.SetBlock(5, 5, 5, Blocks.Stairs(BuildMaterial.Stone, Facing.North)); w.SetBlock(5, 5, 4, Blocks.Stone); Discovery.InitialFlood(w);
        var stairs = ChunkMesher.Build(V(w), new Int3(0, 0, 0)).Opaque;
        // Stairs lose slab front-side and upper back faces against the stone (-Z); the stone keeps all 6 faces (stairs never occlude).
        Assert.Equal(9 + 6, Faces(stairs));
    }
    [Fact] public void Cap_WaterUnderWaterOrSolid_GoesToLiquid()
    {
        var w = TestWorlds.SolidBelow(32, 60); w.SetBlock(3, 40, 3, Blocks.Water, false); w.SetBlock(4, 40, 4, Blocks.Water, false); w.SetBlock(4, 41, 4, Blocks.Water, false);
        var v = V(w); v.FogEnabled = false; var m = CapMesher.Build(v, 0, 0, 40);
        Assert.Equal(2, Faces(m.Liquid)); Assert.Equal(32 * 32 - 2, Faces(m.Opaque));
        Assert.All(m.Liquid.Vertices, p => Assert.Equal(41f, p.Y));
    }
}
