using System.Numerics;

namespace ColonySim.Core;

/// <summary>
/// Builds the "cap" of a slice view for one chunk column: an up-facing quad at <c>cutY + 1</c> wherever the cut
/// passes through material, so cut-open rock reads as a solid surface instead of exposing hollow geometry.
/// </summary>
public static class CapMesher
{
    public static MeshPair Build(PlayerView view, int chunkX, int chunkZ, int cutY)
    {
        int s = VoxelWorld.ChunkSize;
        var result = new MeshPair(new MeshBuffers(s * s), new MeshBuffers());
        int x0 = chunkX * s, z0 = chunkZ * s;
        float y = cutY + 1;
        var normal = Vector3.UnitY;
        for (int z = z0; z < z0 + s; z++)
            for (int x = x0; x < x0 + s; x++)
            {
                var b = Blocks.Registry.Get(view.GetVisibleBlock(x, cutY, z));
                if (!b.IsSolid && !b.IsLiquid) continue;
                var a = Blocks.Registry.Get(view.GetVisibleBlock(x, cutY + 1, z));
                MeshBuffers target;
                if (b.IsSolid && a.IsSolid) target = result.Opaque;
                else if (b.IsLiquid && (a.IsLiquid || a.IsSolid)) target = result.Liquid;
                else continue;
                target.AddQuad(normal, b.TexTop, false,
                    new Vector3(x, y, z), new Vector2(0, 0), 1f, new Vector3(x + 1, y, z), new Vector2(1, 0), 1f,
                    new Vector3(x + 1, y, z + 1), new Vector2(1, 1), 1f, new Vector3(x, y, z + 1), new Vector2(0, 1), 1f);
            }
        return result;
    }
}
