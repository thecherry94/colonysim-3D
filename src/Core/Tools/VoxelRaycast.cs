using System.Numerics;

namespace ColonySim.Core;

public readonly record struct RaycastHit(Int3 Block, Int3 Normal, Vector3 Point);

/// <summary>Amanatides-Woo voxel traversal against the fogged <see cref="PlayerView"/>.</summary>
public static class VoxelRaycast
{
    /// <summary>
    /// Finds the first solid-ish cell (anything but air and liquid; ramps and ladders included) along the ray. Air and liquid are passed through; cells above
    /// <paramref name="maxY"/> count as empty. Origins outside the map are fine: the ray is clipped to the map box first.
    /// </summary>
    public static bool Cast(PlayerView view, Vector3 origin, Vector3 direction, float maxDistance, int maxY, out RaycastHit hit)
    {
        hit = default;
        if (direction.LengthSquared() < 1e-12f) return false;
        var dir = Vector3.Normalize(direction);
        var size = new[] { view.World.SizeX, view.World.SizeY, view.World.SizeZ };

        // Slab clip against [0,size] on each axis.
        float tEnter = 0f, tExit = maxDistance;
        int entryAxis = -1;
        for (int a = 0; a < 3; a++)
        {
            float o = Comp(origin, a), d = Comp(dir, a);
            if (MathF.Abs(d) < 1e-9f)
            {
                if (o < 0 || o >= size[a]) return false;
                continue;
            }
            float t0 = (0 - o) / d, t1 = (size[a] - o) / d;
            if (t0 > t1) (t0, t1) = (t1, t0);
            if (t0 > tEnter) { tEnter = t0; entryAxis = a; }
            if (t1 < tExit) tExit = t1;
            if (tEnter > tExit) return false;
        }

        var start = origin + dir * tEnter;
        var cell = new int[3];
        var step = new int[3];
        var tMax = new float[3];
        var tDelta = new float[3];
        for (int a = 0; a < 3; a++)
        {
            float d = Comp(dir, a);
            cell[a] = Math.Clamp((int)MathF.Floor(Comp(start, a)), 0, size[a] - 1);
            step[a] = d > 0 ? 1 : d < 0 ? -1 : 0;
            if (step[a] == 0) { tMax[a] = float.PositiveInfinity; tDelta[a] = float.PositiveInfinity; continue; }
            tDelta[a] = 1f / MathF.Abs(d);
            tMax[a] = ((d > 0 ? cell[a] + 1 : cell[a]) - Comp(origin, a)) / d;
        }

        var normal = Int3.Zero;
        if (entryAxis >= 0) normal = Axis(entryAxis, -step[entryAxis]);
        float t = tEnter;

        while (true)
        {
            if (cell[1] <= maxY && IsPickable(view, cell[0], cell[1], cell[2]))
            {
                hit = new RaycastHit(new Int3(cell[0], cell[1], cell[2]), normal, origin + dir * t);
                return true;
            }
            int s = tMax[0] <= tMax[1] ? (tMax[0] <= tMax[2] ? 0 : 2) : (tMax[1] <= tMax[2] ? 1 : 2);
            t = tMax[s];
            if (t > tExit) return false;
            cell[s] += step[s];
            if (cell[s] < 0 || cell[s] >= size[s]) return false;
            tMax[s] += tDelta[s];
            normal = Axis(s, -step[s]);
        }
    }

    static bool IsPickable(PlayerView view, int x, int y, int z)
    {
        var shape = Blocks.Registry.Get(view.GetVisibleBlock(x, y, z)).Shape;
        return shape != BlockShape.None && shape != BlockShape.Liquid;
    }

    static float Comp(Vector3 v, int a) => a == 0 ? v.X : a == 1 ? v.Y : v.Z;
    static Int3 Axis(int a, int sign) => a == 0 ? new(sign, 0, 0) : a == 1 ? new(0, sign, 0) : new(0, 0, sign);
}
