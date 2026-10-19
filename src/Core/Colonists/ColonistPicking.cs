using System.Numerics;

namespace ColonySim.Core;

/// <summary>Click-picking of colonists: a ray against each one's body box.</summary>
public static class ColonistPicking
{
    public const float Width = 0.6f, Height = 1.8f;

    /// <summary>
    /// The id of the colonist whose box (<see cref="Width"/> × <see cref="Height"/> × <see cref="Width"/>, centered on the
    /// feet's x/z and rising from the feet's y) the ray enters first within <paramref name="maxDistance"/>, or 0.
    /// <paramref name="direction"/> must be a unit vector.
    /// </summary>
    public static int Pick(Vector3 origin, Vector3 direction, IReadOnlyList<(int Id, Vector3 Feet)> colonists, float maxDistance)
    {
        int best = 0;
        float bestT = maxDistance;
        foreach (var (id, feet) in colonists)
        {
            var min = new Vector3(feet.X - Width / 2, feet.Y, feet.Z - Width / 2);
            var max = new Vector3(feet.X + Width / 2, feet.Y + Height, feet.Z + Width / 2);
            if (Entry(origin, direction, min, max) is float t && t <= bestT) { bestT = t; best = id; }
        }
        return best;
    }

    /// <summary>The distance along the ray at which it enters the box (0 when it starts inside), or null for a miss.</summary>
    static float? Entry(Vector3 o, Vector3 d, Vector3 min, Vector3 max)
    {
        float tNear = 0, tFar = float.MaxValue;
        for (int a = 0; a < 3; a++)
        {
            float oa = Component(o, a), da = Component(d, a), lo = Component(min, a), hi = Component(max, a);
            if (MathF.Abs(da) < 1e-9f)
            {
                if (oa < lo || oa > hi) return null;
                continue;
            }
            float t1 = (lo - oa) / da, t2 = (hi - oa) / da;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tNear = MathF.Max(tNear, t1);
            tFar = MathF.Min(tFar, t2);
            if (tNear > tFar) return null;
        }
        return tNear;
    }

    static float Component(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;
}
