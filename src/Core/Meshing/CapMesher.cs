using System.Numerics;

namespace ColonySim.Core;

/// <summary>
/// Builds the "cap" of a slice view for one chunk column: up-facing quads at <c>cutY + 1</c> wherever the cut
/// passes through material, so cut-open rock reads as a solid surface instead of exposing hollow geometry.
/// Caps carry no ambient occlusion, so neighboring cells with the same kind and top texture are greedily merged
/// into rectangles whose UVs tile the texture once per block (the sampler must repeat). A cut through rock
/// therefore costs a handful of quads instead of one per block. Buffers are sized exactly; empty columns
/// allocate nothing.
/// </summary>
public static class CapMesher
{
    const int S = VoxelWorld.ChunkSize;
    const byte None = 0, Opaque = 1, Liquid = 2;

    readonly record struct Rect(int X, int Z, int W, int H, byte Kind, int Layer);

    public static MeshPair Build(PlayerView view, int chunkX, int chunkZ, int cutY)
    {
        int x0 = chunkX * S, z0 = chunkZ * S;
        Span<byte> kind = stackalloc byte[S * S];
        Span<int> layer = stackalloc int[S * S];
        int cells = 0;
        for (int lz = 0, i = 0; lz < S; lz++)
            for (int lx = 0; lx < S; lx++, i++)
            {
                kind[i] = None;
                var b = Blocks.Registry.Get(view.GetVisibleBlock(x0 + lx, cutY, z0 + lz));
                if (!b.IsSolid && !b.IsLiquid) continue;
                var a = Blocks.Registry.Get(view.GetVisibleBlock(x0 + lx, cutY + 1, z0 + lz));
                if (b.IsSolid && a.IsSolid) kind[i] = Opaque;
                else if (b.IsLiquid && (a.IsLiquid || a.IsSolid)) kind[i] = Liquid;
                else continue;
                layer[i] = b.TexTop;
                cells++;
            }
        if (cells == 0) return MeshPair.Empty();

        Span<Rect> rects = stackalloc Rect[cells];
        int count = Merge(kind, layer, rects, out int opaqueCount);
        var result = new MeshPair(new MeshBuffers(opaqueCount), new MeshBuffers(count - opaqueCount));

        float y = cutY + 1;
        foreach (var r in rects[..count])
        {
            float x = x0 + r.X, z = z0 + r.Z;
            (r.Kind == Opaque ? result.Opaque : result.Liquid).AddQuad(Vector3.UnitY, r.Layer, false,
                new Vector3(x, y, z), new Vector2(0, 0), 1f, new Vector3(x + r.W, y, z), new Vector2(r.W, 0), 1f,
                new Vector3(x + r.W, y, z + r.H), new Vector2(r.W, r.H), 1f, new Vector3(x, y, z + r.H), new Vector2(0, r.H), 1f);
        }
        return result;
    }

    /// <summary>Greedy rectangles over the kind/layer grid (consumed cells are cleared). Returns the rectangle count.</summary>
    static int Merge(Span<byte> kind, Span<int> layer, Span<Rect> rects, out int opaqueCount)
    {
        int count = 0;
        opaqueCount = 0;
        for (int z = 0; z < S; z++)
            for (int x = 0; x < S; x++)
            {
                int i = z * S + x;
                byte k = kind[i];
                if (k == None) continue;
                int l = layer[i];
                int w = 1;
                while (x + w < S && kind[i + w] == k && layer[i + w] == l) w++;
                int h = 1;
                while (z + h < S && RowMatches(kind, layer, (z + h) * S + x, w, k, l)) h++;
                for (int dz = 0; dz < h; dz++) kind.Slice((z + dz) * S + x, w).Fill(None);
                rects[count++] = new Rect(x, z, w, h, k, l);
                if (k == Opaque) opaqueCount++;
            }
        return count;
    }

    static bool RowMatches(Span<byte> kind, Span<int> layer, int start, int w, byte k, int l)
    {
        for (int i = start; i < start + w; i++)
            if (kind[i] != k || layer[i] != l) return false;
        return true;
    }
}
