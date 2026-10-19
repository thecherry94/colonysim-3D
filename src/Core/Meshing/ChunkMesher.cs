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
    static readonly Face[] SideFaces;                       // indexed by Facing
    static readonly (Vector3 Min, Vector3 Max)[][] LadderBoxes; // indexed by Facing

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
        SideFaces = [Faces[5], Faces[0], Faces[4], Faces[1]];

        // Ladder against the north side: two rails and four rungs, rotated for the other facings.
        (Vector3, Vector3)[] north =
        [
            (new(.15f, 0, .02f), new(.25f, 1, .12f)), (new(.75f, 0, .02f), new(.85f, 1, .12f)),
            .. new[] { .125f, .375f, .625f, .875f }.Select(y => (new Vector3(.25f, y - .03f, .04f), new Vector3(.75f, y + .03f, .10f))),
        ];
        LadderBoxes = new (Vector3, Vector3)[4][];
        for (int f = 0; f < 4; f++)
            LadderBoxes[f] = north.Select(b =>
            {
                Vector3 p = b.Item1, q = b.Item2;
                for (int r = 0; r < f; r++) { p = new(1 - p.Z, p.Y, p.X); q = new(1 - q.Z, q.Y, q.X); } // a quarter turn clockwise
                return (Vector3.Min(p, q), Vector3.Max(p, q));
            }).ToArray();
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
                            case BlockShape.Ramp or BlockShape.CornerOuter or BlockShape.CornerInner:
                                EmitSlope(result.Opaque, pad, idx, id, origin); break;
                            case BlockShape.Ladder: EmitLadder(result.Opaque, id, origin); break;
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

    /// <summary>
    /// Ramps and slope corners: a unit cell whose top corners (in top-face order, x/z = 00, 10, 11, 01) are each
    /// raised to 1 or lowered to 0; the side facing f spans corners f and f + 1. Ramps rise toward their facing; an outer corner raises only the corner between
    /// its facing and the next side clockwise, an inner corner lowers only the opposite one. Sloped tops are never
    /// culled; sides, the bottom and an inner corner's flat top cull against occluders.
    /// </summary>
    static void EmitSlope(MeshBuffers m, ushort[] pad, int idx, ushort id, Vector3 origin)
    {
        int f = (int)Facings[id];
        Span<bool> high = stackalloc bool[4];
        int special = -1; // the one corner that differs from the rest (corners only)
        switch (Shapes[id])
        {
            case BlockShape.Ramp: high[f] = high[(f + 1) & 3] = true; break;
            case BlockShape.CornerOuter: special = (f + 1) & 3; high[special] = true; break;
            default: special = (f + 3) & 3; high.Fill(true); high[special] = false; break;
        }

        var top = Faces[2];
        Span<Vector3> p = stackalloc Vector3[4];
        Span<float> ao = stackalloc float[4];
        for (int k = 0; k < 4; k++)
        {
            p[k] = origin + top.Corners[k] with { Y = high[k] ? 1 : 0 };
            ao[k] = CornerAo(pad, idx, top.AoDeltas, k);
        }
        var uv = top.CornerUvs;
        int tex = TexTop[id];
        if (special < 0)
            m.AddQuad(SlopeNormal(p[0], p[1], p[2]), tex, ao[0] + ao[2] < ao[1] + ao[3],
                p[0], uv[0], ao[0], p[1], uv[1], ao[1], p[2], uv[2], ao[2], p[3], uv[3], ao[3]);
        else
        {
            // Split along the diagonal through the special corner's neighbors: the special corner's half slopes,
            // the other half is flat (at 1 for an inner corner; at 0 for an outer one, where it is the floor and skipped).
            int a = (special + 3) & 3, s = special, c = (special + 1) & 3, o = (special + 2) & 3;
            m.AddTriangle(SlopeNormal(p[a], p[s], p[c]), tex, p[a], uv[a], ao[a], p[s], uv[s], ao[s], p[c], uv[c], ao[c]);
            if (high[o] && !Occludes[pad[idx + top.NeighborDelta]])
                m.AddTriangle(top.Normal, tex, p[c], uv[c], ao[c], p[o], uv[o], ao[o], p[a], uv[a], ao[a]);
        }

        EmitBoxFace(m, pad, idx, id, origin, Faces[3], Vector3.Zero, Vector3.One);
        foreach (var side in SideFaces) EmitSlopeSide(m, pad, idx, id, origin, side, high);
    }

    /// <summary>Normal of a triangle wound clockwise seen from outside.</summary>
    static Vector3 SlopeNormal(Vector3 a, Vector3 b, Vector3 c) => Vector3.Normalize(Vector3.Cross(c - a, b - a));

    /// <summary>A side of a slope: the full face minus its top corners that are lowered, so a quad, a triangle or nothing.</summary>
    static void EmitSlopeSide(MeshBuffers m, ushort[] pad, int idx, ushort id, Vector3 origin, Face f, ReadOnlySpan<bool> high)
    {
        Span<int> keep = stackalloc int[4];
        int n = 0;
        for (int k = 0; k < 4; k++)
        {
            var c = f.Corners[k];
            if (c.Y == 0 || high[c.X == 0 ? (c.Z == 0 ? 0 : 3) : (c.Z == 0 ? 1 : 2)]) keep[n++] = k;
        }
        if (n < 3 || Occludes[pad[idx + f.NeighborDelta]]) return;

        // Every kept point is a corner of the full face, so the full face's bilinear AO is just its corner AO.
        Span<float> ao = stackalloc float[4];
        for (int k = 0; k < 4; k++) ao[k] = CornerAo(pad, idx, f.AoDeltas, k);
        var cs = f.Corners; var uv = f.CornerUvs;
        int tex = Texture(id, f);
        if (n == 4)
            m.AddQuad(f.Normal, tex, ao[0] + ao[2] < ao[1] + ao[3],
                origin + cs[0], uv[0], ao[0], origin + cs[1], uv[1], ao[1], origin + cs[2], uv[2], ao[2], origin + cs[3], uv[3], ao[3]);
        else
        {
            int i = keep[0], j = keep[1], k = keep[2]; // a subsequence of the clockwise corners stays clockwise
            m.AddTriangle(f.Normal, tex,
                origin + cs[i], uv[i], ao[i], origin + cs[j], uv[j], ao[j], origin + cs[k], uv[k], ao[k]);
        }
    }

    /// <summary>Ladder: rails and rungs against the facing side of the cell. Every face is drawn, unoccluded, with the side texture.</summary>
    static void EmitLadder(MeshBuffers m, ushort id, Vector3 origin)
    {
        int tex = TexSide[id];
        foreach (var (min, max) in LadderBoxes[(int)Facings[id]])
            foreach (var f in Faces)
            {
                var c = f.Corners;
                Vector3 p0 = min + c[0] * (max - min), p1 = min + c[1] * (max - min), p2 = min + c[2] * (max - min), p3 = min + c[3] * (max - min);
                m.AddQuad(f.Normal, tex, false,
                    origin + p0, Uv(f.Axis, f.Positive, p0), 1f, origin + p1, Uv(f.Axis, f.Positive, p1), 1f,
                    origin + p2, Uv(f.Axis, f.Positive, p2), 1f, origin + p3, Uv(f.Axis, f.Positive, p3), 1f);
            }
    }
}
