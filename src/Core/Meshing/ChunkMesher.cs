using System.Buffers;
using System.Numerics;

namespace ColonySim.Core;

/// <summary>
/// Builds the visible-face mesh of one 32³ chunk from the player's (fogged) view, in world coordinates.
/// Thread-safe: all statics are immutable lookup tables; scratch memory is per call.
/// </summary>
public static class ChunkMesher
{
    const int S = VoxelWorld.ChunkSize;
    const int P = S + 2; // chunk plus a one-block border on each side
    const int Stride = P * P;

    // Below the world floor counts as solid ground: nobody can see under bedrock, so no bottom faces are emitted there.
    static readonly ushort FloorId = Blocks.Bedrock;

    static readonly bool[] Occludes;
    static readonly BlockShape[] Shapes;
    static readonly int[] TexTop, TexSide, TexBottom;
    static readonly Facing[] Facings;
    static readonly Face[] Faces;

    /// <summary>One of the six face directions: corners wind clockwise seen from outside.</summary>
    sealed class Face
    {
        public required Vector3 Normal;
        public required int Axis;
        public required bool Positive;
        public required int NeighborDelta;     // padded-array offset to the block in front of the face
        public required Vector3[] Corners;     // unit-cube corners p0..p3
        public required Vector2[] CornerUvs;
        public required int[] AoDeltas;        // per corner: side1, side2, diagonal (padded-array offsets)
    }

    static ChunkMesher()
    {
        var defs = Blocks.Registry.All;
        int n = defs.Count;
        Occludes = new bool[n]; Shapes = new BlockShape[n]; Facings = new Facing[n];
        TexTop = new int[n]; TexSide = new int[n]; TexBottom = new int[n];
        for (int i = 0; i < n; i++)
        {
            var d = defs[i];
            Occludes[i] = d.Occludes; Shapes[i] = d.Shape; Facings[i] = d.Facing;
            TexTop[i] = d.TexTop; TexSide[i] = d.TexSide; TexBottom[i] = d.TexBottom;
        }

        Faces =
        [
            MakeFace(0, true, [new(1, 0, 0), new(1, 0, 1), new(1, 1, 1), new(1, 1, 0)]),
            MakeFace(0, false, [new(0, 0, 0), new(0, 1, 0), new(0, 1, 1), new(0, 0, 1)]),
            MakeFace(1, true, [new(0, 1, 0), new(1, 1, 0), new(1, 1, 1), new(0, 1, 1)]),
            MakeFace(1, false, [new(0, 0, 0), new(0, 0, 1), new(1, 0, 1), new(1, 0, 0)]),
            MakeFace(2, true, [new(0, 0, 1), new(0, 1, 1), new(1, 1, 1), new(1, 0, 1)]),
            MakeFace(2, false, [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0)]),
        ];
    }

    static Face MakeFace(int axis, bool positive, Vector3[] corners)
    {
        var normal = Vector3.Zero;
        normal[axis] = positive ? 1 : -1;
        var front = ToInt3(normal);
        int t1 = (axis + 1) % 3, t2 = (axis + 2) % 3;
        var ao = new int[12];
        var uvs = new Vector2[4];
        for (int k = 0; k < 4; k++)
        {
            var c = corners[k];
            var s1 = Vector3.Zero; s1[t1] = c[t1] == 1 ? 1 : -1;
            var s2 = Vector3.Zero; s2[t2] = c[t2] == 1 ? 1 : -1;
            ao[k * 3] = Delta(front + ToInt3(s1));
            ao[k * 3 + 1] = Delta(front + ToInt3(s2));
            ao[k * 3 + 2] = Delta(front + ToInt3(s1 + s2));
            uvs[k] = Uv(axis, positive, c);
        }
        return new Face
        {
            Normal = normal, Axis = axis, Positive = positive, NeighborDelta = Delta(front),
            Corners = corners, CornerUvs = uvs, AoDeltas = ao,
        };
    }

    static Int3 ToInt3(Vector3 v) => new((int)v.X, (int)v.Y, (int)v.Z);

    static int Delta(Int3 o) => (o.Y * P + o.Z) * P + o.X;

    /// <summary>UVs from the position inside the block: 0–1 across a full face, V = 0 at the top of side faces.</summary>
    static Vector2 Uv(int axis, bool positive, Vector3 p) => axis switch
    {
        0 => new(positive ? 1 - p.Z : p.Z, 1 - p.Y),
        1 => new(p.X, positive ? p.Z : 1 - p.Z),
        _ => new(positive ? p.X : 1 - p.X, 1 - p.Y),
    };

    public static MeshPair Build(PlayerView view, Int3 chunk)
    {
        if (view.World.IsChunkUniform(chunk, out _)) return MeshPair.Empty();

        var result = MeshPair.Empty();
        var pad = ArrayPool<ushort>.Shared.Rent(P * P * P);
        try
        {
            int bx = chunk.X * S, by = chunk.Y * S, bz = chunk.Z * S;
            Fill(view, pad, bx, by, bz);
            for (int y = 0; y < S; y++)
                for (int z = 0; z < S; z++)
                {
                    int idx = ((y + 1) * P + (z + 1)) * P + 1;
                    for (int x = 0; x < S; x++, idx++)
                    {
                        ushort id = pad[idx];
                        if (id == Blocks.Air) continue;
                        var origin = new Vector3(bx + x, by + y, bz + z);
                        switch (Shapes[id])
                        {
                            case BlockShape.Cube: EmitCube(result.Opaque, pad, idx, id, origin); break;
                            case BlockShape.Liquid: EmitLiquid(result.Liquid, pad, idx, id, origin); break;
                            case BlockShape.Stairs: EmitStairs(result.Opaque, pad, idx, id, origin); break;
                        }
                    }
                }
        }
        finally
        {
            ArrayPool<ushort>.Shared.Return(pad);
        }
        return result;
    }

    static void Fill(PlayerView view, ushort[] pad, int bx, int by, int bz)
    {
        int i = 0;
        for (int y = by - 1; y <= by + S; y++)
            for (int z = bz - 1; z <= bz + S; z++)
                for (int x = bx - 1; x <= bx + S; x++, i++)
                    pad[i] = y < 0 ? FloorId : view.GetVisibleBlock(x, y, z);
    }

    static int Texture(ushort id, Face f) => f.Axis != 1 ? TexSide[id] : f.Positive ? TexTop[id] : TexBottom[id];

    static float CornerAo(ushort[] pad, int idx, int[] d, int k)
    {
        bool s1 = Occludes[pad[idx + d[k * 3]]], s2 = Occludes[pad[idx + d[k * 3 + 1]]];
        if (s1 && s2) return 0f;
        int n = (s1 ? 1 : 0) + (s2 ? 1 : 0) + (Occludes[pad[idx + d[k * 3 + 2]]] ? 1 : 0);
        return (3 - n) / 3f;
    }

    static void EmitCube(MeshBuffers m, ushort[] pad, int idx, ushort id, Vector3 origin)
    {
        foreach (var f in Faces)
        {
            if (Occludes[pad[idx + f.NeighborDelta]]) continue;
            float a0 = CornerAo(pad, idx, f.AoDeltas, 0), a1 = CornerAo(pad, idx, f.AoDeltas, 1);
            float a2 = CornerAo(pad, idx, f.AoDeltas, 2), a3 = CornerAo(pad, idx, f.AoDeltas, 3);
            var c = f.Corners; var uv = f.CornerUvs;
            m.AddQuad(f.Normal, Texture(id, f), a0 + a2 < a1 + a3,
                origin + c[0], uv[0], a0, origin + c[1], uv[1], a1, origin + c[2], uv[2], a2, origin + c[3], uv[3], a3);
        }
    }

    static void EmitLiquid(MeshBuffers m, ushort[] pad, int idx, ushort id, Vector3 origin)
    {
        foreach (var f in Faces)
        {
            if (pad[idx + f.NeighborDelta] != Blocks.Air) continue;
            var c = f.Corners; var uv = f.CornerUvs;
            m.AddQuad(f.Normal, Texture(id, f), false,
                origin + c[0], uv[0], 1f, origin + c[1], uv[1], 1f, origin + c[2], uv[2], 1f, origin + c[3], uv[3], 1f);
        }
    }

    // Stairs = full-width slab (y 0–0.5) + half block (y 0.5–1) on the ascent side. The slab's top is only
    // emitted over the uncovered half, and the half block's bottom is never emitted.
    static void EmitStairs(MeshBuffers m, ushort[] pad, int idx, ushort id, Vector3 origin)
    {
        var (upperMin, upperMax, openMin, openMax) = Facings[id] switch
        {
            Facing.North => (new Vector3(0, .5f, 0), new Vector3(1, 1, .5f), new Vector3(0, 0, .5f), new Vector3(1, .5f, 1)),
            Facing.South => (new Vector3(0, .5f, .5f), new Vector3(1, 1, 1), new Vector3(0, 0, 0), new Vector3(1, .5f, .5f)),
            Facing.East => (new Vector3(.5f, .5f, 0), new Vector3(1, 1, 1), new Vector3(0, 0, 0), new Vector3(.5f, .5f, 1)),
            _ => (new Vector3(0, .5f, 0), new Vector3(.5f, 1, 1), new Vector3(.5f, 0, 0), new Vector3(1, .5f, 1)),
        };
        foreach (var f in Faces)
        {
            if (f.Axis != 1 || !f.Positive) EmitBoxFace(m, pad, idx, id, origin, f, Vector3.Zero, new Vector3(1, .5f, 1));
            if (f.Axis != 1 || f.Positive) EmitBoxFace(m, pad, idx, id, origin, f, upperMin, upperMax);
            if (f.Axis == 1 && f.Positive) EmitBoxFace(m, pad, idx, id, origin, f, openMin, openMax);
        }
    }

    /// <summary>
    /// One face of a sub-box of the block. Faces on the block boundary cull against occluders and take AO
    /// bilinearly from the full face's corners; interior faces (risers, the half slab top) are always drawn, unoccluded.
    /// </summary>
    static void EmitBoxFace(MeshBuffers m, ushort[] pad, int idx, ushort id, Vector3 origin, Face f, Vector3 min, Vector3 max)
    {
        bool boundary = f.Positive ? max[f.Axis] == 1 : min[f.Axis] == 0;
        Span<float> full = stackalloc float[4] { 1, 1, 1, 1 };
        if (boundary)
        {
            if (Occludes[pad[idx + f.NeighborDelta]]) return;
            for (int k = 0; k < 4; k++) full[k] = CornerAo(pad, idx, f.AoDeltas, k);
        }

        Span<Vector3> p = stackalloc Vector3[4];
        Span<float> ao = stackalloc float[4];
        int t1 = (f.Axis + 1) % 3, t2 = (f.Axis + 2) % 3;
        for (int k = 0; k < 4; k++)
        {
            p[k] = min + f.Corners[k] * (max - min);
            float a = 0;
            for (int j = 0; j < 4; j++)
            {
                var c = f.Corners[j];
                a += full[j] * (c[t1] == 1 ? p[k][t1] : 1 - p[k][t1]) * (c[t2] == 1 ? p[k][t2] : 1 - p[k][t2]);
            }
            ao[k] = a;
        }
        m.AddQuad(f.Normal, Texture(id, f), ao[0] + ao[2] < ao[1] + ao[3],
            origin + p[0], Uv(f.Axis, f.Positive, p[0]), ao[0], origin + p[1], Uv(f.Axis, f.Positive, p[1]), ao[1],
            origin + p[2], Uv(f.Axis, f.Positive, p[2]), ao[2], origin + p[3], Uv(f.Axis, f.Positive, p[3]), ao[3]);
    }
}
