using System.Numerics;

namespace ColonySim.Core.Tests;

public class MeshingTests
{
    static PlayerView V(VoxelWorld w) { var v = new PlayerView(w, new TerrainLayers(MapSettings.Create(1, MapSize.Small))); return v; }
    static int Faces(MeshBuffers b) => b.Indices.Count / 6;
    // Cap quads are axis-aligned rectangles: corner 0 is the min corner, corner 2 the max corner.
    static IEnumerable<(Vector3 Min, Vector3 Max)> CapRects(MeshBuffers b) =>
        Enumerable.Range(0, b.Vertices.Count / 4).Select(q => (b.Vertices[q * 4], b.Vertices[q * 4 + 2]));
    static int CapArea(MeshBuffers b) => (int)CapRects(b).Sum(r => (r.Max.X - r.Min.X) * (r.Max.Z - r.Min.Z));
    static bool CapCovers(MeshBuffers b, int x, int z) => CapRects(b).Any(r => x >= r.Min.X && x < r.Max.X && z >= r.Min.Z && z < r.Max.Z);

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
    [Fact] public void Cap_CoversEverySolidColumn_NoneWhereOpen()
    {
        var w = TestWorlds.SolidBelow(32, 60); Discovery.InitialFlood(w); var v = V(w);
        Assert.Equal(32 * 32, CapArea(CapMesher.Build(v, 0, 0, 40).Opaque));
        Assert.True(CapMesher.Build(v, 0, 0, 60).Opaque.IsEmpty);
        w.SetBlock(3, 40, 3, Blocks.Air); Discovery.RevealFrom(w, 3, 40, 3);
        var m = CapMesher.Build(v, 0, 0, 40).Opaque;
        Assert.Equal(32 * 32 - 1, CapArea(m));
        Assert.False(CapCovers(m, 3, 3));
    }
    [Fact] public void Cap_MergesUniformCutIntoOneQuad_WithTiledUvs()
    {
        var w = TestWorlds.SolidBelow(32, 60); Discovery.InitialFlood(w);
        var b = CapMesher.Build(V(w), 0, 0, 40).Opaque;
        Assert.Equal(1, Faces(b));
        Assert.Equal(new Vector2(32, 32), new Vector2(b.Uvs.Max(u => u.X), b.Uvs.Max(u => u.Y)));
        for (int i = 0; i < b.Indices.Count; i += 3)
        {
            var (p, q, r) = (b.Vertices[b.Indices[i]], b.Vertices[b.Indices[i + 1]], b.Vertices[b.Indices[i + 2]]);
            Assert.True(Vector3.Dot(Vector3.Cross(q - p, r - p), Vector3.UnitY) < 0);
        }
    }
    [Fact] public void Cap_KeepsDifferentBlocksApart()
    {
        var w = TestWorlds.SolidBelow(32, 60); w.SetBlock(7, 40, 9, Blocks.CoalOre); w.SetBlock(8, 40, 9, Blocks.CoalOre); Discovery.InitialFlood(w);
        var b = CapMesher.Build(V(w), 0, 0, 40).Opaque;
        Assert.Equal(32 * 32, CapArea(b));
        float coal = Blocks.Registry.Get(Blocks.CoalOre).TexTop;
        var coalQuads = Enumerable.Range(0, b.Vertices.Count / 4).Where(q => b.Uv2s[q * 4].X == coal).ToList();
        Assert.Single(coalQuads);
        Assert.Equal(new Vector3(7, 41, 9), b.Vertices[coalQuads[0] * 4]);
        Assert.Equal(new Vector3(9, 41, 10), b.Vertices[coalQuads[0] * 4 + 2]);
    }

    [Fact] public void Cap_EmptyColumn_AllocatesNoBuffers()
    {
        var w = TestWorlds.SolidBelow(32, 60); Discovery.InitialFlood(w);
        var m = CapMesher.Build(V(w), 0, 0, 60);
        Assert.Equal(0, m.Opaque.Vertices.Capacity); Assert.Equal(0, m.Opaque.Indices.Capacity);
        Assert.Equal(0, m.Liquid.Vertices.Capacity); Assert.Equal(0, m.Liquid.Indices.Capacity);
    }
    [Fact] public void Cap_BuffersSizedExactlyToQuadCount()
    {
        var w = TestWorlds.SolidBelow(32, 60); w.SetBlock(3, 40, 3, Blocks.Air); Discovery.RevealFrom(w, 3, 40, 3); Discovery.InitialFlood(w);
        var b = CapMesher.Build(V(w), 0, 0, 40).Opaque;
        Assert.Equal(b.Vertices.Count, b.Vertices.Capacity); Assert.Equal(b.Uv2s.Count, b.Uv2s.Capacity); Assert.Equal(b.Indices.Count, b.Indices.Capacity);
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
    // --- Ramps, slope corners and ladders: stone at y <= 5, the shape alone at (5,6,5). ---

    static readonly Vector3 ShapeCell = new(5, 6, 5);

    static MeshBuffers MeshShape(ushort id)
    {
        var w = TestWorlds.SolidBelow(32, 5); w.SetBlock(5, 6, 5, id); Discovery.InitialFlood(w);
        return ChunkMesher.Build(V(w), new Int3(0, 0, 0)).Opaque;
    }

    static IEnumerable<(Vector3 A, Vector3 B, Vector3 C, Vector3 N)> Triangles(MeshBuffers b) =>
        Enumerable.Range(0, b.Indices.Count / 3).Select(t =>
            (b.Vertices[b.Indices[t * 3]], b.Vertices[b.Indices[t * 3 + 1]], b.Vertices[b.Indices[t * 3 + 2]], b.Normals[b.Indices[t * 3]]));

    // A triangle belongs to the shape when the solid just behind it lies inside the shape's cell; this excludes the
    // neighbors' faces that touch the cell, such as the ground's top face underneath.
    static IEnumerable<(Vector3 A, Vector3 B, Vector3 C, Vector3 N)> ShapeTriangles(MeshBuffers b) => Triangles(b).Where(t =>
    {
        var behind = (t.A + t.B + t.C) / 3 - t.N * 1e-3f - ShapeCell;
        return behind.X > 0 && behind.X < 1 && behind.Y > 0 && behind.Y < 1 && behind.Z > 0 && behind.Z < 1;
    });

    static int ShapeTris(MeshBuffers b) => ShapeTriangles(b).Count();
    static IEnumerable<Vector3> ShapeVertices(MeshBuffers b) => ShapeTriangles(b).SelectMany(t => new[] { t.A, t.B, t.C });
    static int Tris(MeshBuffers b, Vector3 n) => ShapeTriangles(b).Count(t => Vector3.Distance(t.N, n) < 1e-4f);

    /// <summary>Area of the up-facing triangles at height y inside the unit square at (x, z).</summary>
    static float TopFaceAt(MeshBuffers b, int x, int y, int z) => Triangles(b)
        .Where(t => t.N == Vector3.UnitY && new[] { t.A, t.B, t.C }.All(p => p.Y == y && p.X >= x && p.X <= x + 1 && p.Z >= z && p.Z <= z + 1))
        .Sum(t => Vector3.Cross(t.B - t.A, t.C - t.A).Length() / 2);

    [Fact] public void RampNorth_SlopedTopHighSideTwoTrianglesNoBottom()
    {
        var b = MeshShape(Blocks.BuiltRamp(BuildMaterial.Stone, Facing.North));
        Assert.Equal(2, Tris(b, Vector3.Normalize(new Vector3(0, 1, 1))));   // up + toward the low (south) side
        Assert.Equal(2, Tris(b, -Vector3.UnitZ));                             // high side
        Assert.Equal(1, Tris(b, Vector3.UnitX)); Assert.Equal(1, Tris(b, -Vector3.UnitX));
        Assert.Equal(0, Tris(b, -Vector3.UnitY));                             // bottom culled by stone
        Assert.Equal(6, ShapeTris(b));                                        // and no low side
    }
    [Fact] public void CornerOuterNorth_OneSlopedTriangleAndTwoSideTriangles()
    {
        var b = MeshShape(Blocks.NaturalSlope(Blocks.Grass, BlockShape.CornerOuter, Facing.North));
        Assert.Equal(1, Tris(b, -Vector3.UnitZ)); Assert.Equal(1, Tris(b, Vector3.UnitX)); Assert.Equal(3, ShapeTris(b));
        // the raised corner sits between the north and east sides
        Assert.Contains(ShapeVertices(b), p => p == new Vector3(6, 7, 5));
        Assert.All(ShapeVertices(b).Where(p => p.Y == 7), p => Assert.Equal(new Vector3(6, 7, 5), p));
    }
    [Fact] public void CornerInnerNorth_EightTriangles()
    {
        var b = MeshShape(Blocks.NaturalSlope(Blocks.Grass, BlockShape.CornerInner, Facing.North));
        Assert.Equal(2, Tris(b, -Vector3.UnitZ)); Assert.Equal(2, Tris(b, Vector3.UnitX)); Assert.Equal(8, ShapeTris(b));
        Assert.Equal(1, Tris(b, Vector3.UnitY));                              // flat top half
        // the lowered corner is opposite the north-east one
        Assert.DoesNotContain(ShapeVertices(b), p => p == new Vector3(5, 7, 6));
    }
    [Fact] public void CornerInner_FlatTopCulledByBlockAbove()
    {
        var w = TestWorlds.SolidBelow(32, 5); w.SetBlock(5, 6, 5, Blocks.NaturalSlope(Blocks.Grass, BlockShape.CornerInner, Facing.East));
        w.SetBlock(5, 7, 5, Blocks.Stone); Discovery.InitialFlood(w);
        var b = ChunkMesher.Build(V(w), new Int3(0, 0, 0)).Opaque;
        Assert.Equal(0, Tris(b, Vector3.UnitY)); Assert.Equal(7, ShapeTris(b));
    }
    [Fact] public void Ladder_SixBoxesAgainstItsWall()
    {
        var b = MeshShape(Blocks.Ladder(Facing.East));
        Assert.Equal(72, ShapeTris(b));
        Assert.All(ShapeVertices(b), v => Assert.InRange(v.X, 5.85f, 6f));
        Assert.Equal(5.88f, ShapeVertices(b).Min(v => v.X), 4); Assert.Equal(5.98f, ShapeVertices(b).Max(v => v.X), 4);
        Assert.All(ShapeTriangles(b), t => Assert.Equal(1f, b.Uv2s[b.Vertices.IndexOf(t.A)].Y));
    }
    [Fact] public void Slope_GroundTopFaceUnderneathStillEmitted()
    {
        var b = MeshShape(Blocks.BuiltRamp(BuildMaterial.Wood, Facing.East));
        Assert.Equal(1f, TopFaceAt(b, 5, 6, 5), 4);
    }
    [Fact] public void NewShapes_WindClockwise_AndNormalsMatchGeometry()
    {
        foreach (var f in new[] { Facing.North, Facing.East, Facing.South, Facing.West })
        {
            foreach (var id in new[]
            {
                Blocks.BuiltRamp(BuildMaterial.Wood, f), Blocks.NaturalSlope(Blocks.Dirt, BlockShape.CornerOuter, f),
                Blocks.NaturalSlope(Blocks.Stone, BlockShape.CornerInner, f), Blocks.Ladder(f),
            })
            {
                var tris = ShapeTriangles(MeshShape(id)).ToList();
                Assert.NotEmpty(tris);
                foreach (var t in tris)
                {
                    var cross = Vector3.Cross(t.B - t.A, t.C - t.A);
                    Assert.True(Vector3.Dot(cross, t.N) < 0, $"{Blocks.Registry.Get(id).Name}: counter-clockwise triangle");
                    Assert.True(Vector3.Dot(Vector3.Normalize(cross), t.N) < -0.9999f, $"{Blocks.Registry.Get(id).Name}: normal off the geometry");
                }
                // sloped tops face up (their normal comes from the winding, so check it independently)
                Assert.All(tris.Where(t => Math.Abs(t.N.Y) is > 0 and < 1), t => Assert.True(t.N.Y > 0));
            }
            var low = f.Opposite().Offset();
            Assert.Equal(2, Tris(MeshShape(Blocks.BuiltRamp(BuildMaterial.Stone, f)), Vector3.Normalize(new Vector3(low.X, 1, low.Z))));
        }
    }
    [Fact] public void Cap_WaterUnderWaterOrSolid_GoesToLiquid()
    {
        var w = TestWorlds.SolidBelow(32, 60); w.SetBlock(3, 40, 3, Blocks.Water, false); w.SetBlock(4, 40, 4, Blocks.Water, false); w.SetBlock(4, 41, 4, Blocks.Water, false);
        var v = V(w); v.FogEnabled = false; var m = CapMesher.Build(v, 0, 0, 40);
        Assert.Equal(2, Faces(m.Liquid)); Assert.Equal(32 * 32 - 2, CapArea(m.Opaque));
        Assert.All(m.Liquid.Vertices, p => Assert.Equal(41f, p.Y));
    }
}
