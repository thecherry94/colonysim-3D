namespace ColonySim.Core;

/// <summary>
/// Smooths single-block steps of the natural surface with slope blocks. Runs after trees and boulders, before discovery:
/// each open surface cell checks which of its four corners touch a column exactly one higher and picks a ramp or corner.
/// </summary>
public static class SlopePlacer
{
    public static void Place(VoxelWorld w, MapSettings s)
    {
        for (int z = 0; z < w.SizeZ; z++)
            for (int x = 0; x < w.SizeX; x++)
            {
                int h = w.GetSurfaceHeight(x, z);
                if (h + 1 >= w.SizeY || w.GetBlock(x, h + 1, z) != Blocks.Air) continue;
                ushort top = w.GetBlock(x, h, z);
                if (!Blocks.Registry.Get(top).IsNatural) continue;

                int count = 0, missing = 0;
                var raised = new bool[4];
                for (int c = 0; c < 4; c++)
                {
                    raised[c] = CornerRaised(w, x, z, h, (Facing)c);
                    if (raised[c]) count++; else missing = c;
                }

                BlockShape shape;
                Facing facing;
                if (count == 1) { shape = BlockShape.CornerOuter; facing = (Facing)Array.IndexOf(raised, true); }
                else if (count == 3) { shape = BlockShape.CornerInner; facing = ((Facing)missing).Opposite(); }
                else if (count == 2)
                {
                    // Adjacent corners c and c+1 share side c+1, which is the raised one.
                    int c = Enumerable.Range(0, 4).FirstOrDefault(i => raised[i] && raised[(i + 1) & 3], -1);
                    if (c < 0) continue; // opposite corners
                    shape = BlockShape.Ramp;
                    facing = ((Facing)c).Clockwise();
                }
                else continue;

                w.SetBlock(x, h + 1, z, Blocks.NaturalSlope(Blocks.SlopeBaseFor(top), shape, facing), markDirty: false);
            }
    }

    /// <summary>Corner <paramref name="c"/> lies between side c and side c.Clockwise(); it is raised when one of the columns touching it is a clean one-block step up.</summary>
    static bool CornerRaised(VoxelWorld w, int x, int z, int h, Facing c)
    {
        var a = c.Offset();
        var b = c.Clockwise().Offset();
        return StepUp(w, x + a.X, z + a.Z, h) || StepUp(w, x + b.X, z + b.Z, h) || StepUp(w, x + a.X + b.X, z + a.Z + b.Z, h);
    }

    /// <summary>The column's surface is exactly h+1, natural, and not a tree trunk's base.</summary>
    static bool StepUp(VoxelWorld w, int x, int z, int h)
    {
        if ((uint)x >= (uint)w.SizeX || (uint)z >= (uint)w.SizeZ) return false;
        if (w.GetSurfaceHeight(x, z) != h + 1) return false;
        if (!Blocks.Registry.Get(w.GetBlock(x, h + 1, z)).IsNatural) return false;
        ushort above = w.GetBlock(x, h + 2, z);
        return above != Blocks.OakLog && above != Blocks.PineLog;
    }
}
