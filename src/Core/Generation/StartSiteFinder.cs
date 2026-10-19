namespace ColonySim.Core;

/// <summary>Picks the flattest dry, low-lying spot near the map center for the colony and levels the terrain around it.</summary>
public static class StartSiteFinder
{
    public const int CoreRadius = 6, BlendRadius = 12, ClearRadius = 8, SearchStep = 8;
    const int WaterMargin = 12;
    const float EdgeJitter = 8f;

    /// <summary>The column with the smallest height range within <see cref="ClearRadius"/>, among dry candidates away from mountains.</summary>
    public static (int X, int Z) Find(int[] heights, MapSettings s)
    {
        int sx = s.SizeX, sz = s.SizeZ;
        int water = s.Terrain.WaterLevel + 1, rock = s.Terrain.MountainRockLine;
        float cx = sx / 2f, cz = sz / 2f;
        int x0 = (int)MathF.Ceiling(sx * 0.15f), x1 = (int)(sx * 0.85f);
        int z0 = (int)MathF.Ceiling(sz * 0.15f), z1 = (int)(sz * 0.85f);

        (int X, int Z) best = (sx / 2, sz / 2);
        int bestRange = int.MaxValue;
        float bestDist = float.MaxValue;
        for (int z = z0; z <= z1; z += SearchStep)
            for (int x = x0; x <= x1; x += SearchStep)
            {
                int range = Range(heights, sx, sz, x, z, water, rock);
                if (range < 0) continue;
                float d = (x - cx) * (x - cx) + (z - cz) * (z - cz);
                if (range < bestRange || (range == bestRange && d < bestDist))
                {
                    best = (x, z);
                    bestRange = range;
                    bestDist = d;
                }
            }
        return best;
    }

    /// <summary>Height range within <see cref="ClearRadius"/>, or -1 when water is within 12 or rock within 8.</summary>
    static int Range(int[] heights, int sx, int sz, int x, int z, int water, int rock)
    {
        int min = int.MaxValue, max = int.MinValue;
        for (int dz = -WaterMargin; dz <= WaterMargin; dz++)
            for (int dx = -WaterMargin; dx <= WaterMargin; dx++)
            {
                int d2 = dx * dx + dz * dz;
                if (d2 > WaterMargin * WaterMargin) continue;
                int nx = x + dx, nz = z + dz;
                if ((uint)nx >= (uint)sx || (uint)nz >= (uint)sz) continue;
                int h = heights[nx + nz * sx];
                if (h <= water) return -1;
                if (d2 > ClearRadius * ClearRadius) continue;
                if (h >= rock) return -1;
                if (h < min) min = h;
                if (h > max) max = h;
            }
        return max - min;
    }

    /// <summary>
    /// Levels the heights around (x, z) in place: the core is exactly the median height (at least two above water), and the
    /// edge blends back into the terrain along a noisy boundary. Returns the flattened surface y.
    /// </summary>
    public static int Flatten(int[] heights, MapSettings s, int x, int z)
    {
        int sx = s.SizeX, sz = s.SizeZ;
        var core = new List<int>();
        ForEachColumn(sx, sz, x, z, CoreRadius, (i, _) => core.Add(heights[i]));
        core.Sort();
        int target = Math.Max(core[core.Count / 2], s.Terrain.WaterLevel + 2);

        var noise = HeightField.Noise(s.Seed, 31, 0.15f, FastNoiseLite.FractalType.FBm, 2);
        int reach = BlendRadius + (int)EdgeJitter / 2 + 1;
        ForEachColumn(sx, sz, x, z, reach, (i, r) =>
        {
            if (r <= CoreRadius) { heights[i] = target; return; }
            int cx = i % sx, cz = i / sx;
            float rj = r + EdgeJitter * ((noise.GetNoise(cx, cz) + 1f) * 0.5f - 0.5f);
            float w = 1f - Smoothstep(CoreRadius, BlendRadius, rj);
            heights[i] = (int)MathF.Round(heights[i] + (target - heights[i]) * w);
        });
        return target;
    }

    static void ForEachColumn(int sx, int sz, int x, int z, int radius, Action<int, float> visit)
    {
        for (int dz = -radius; dz <= radius; dz++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                int nx = x + dx, nz = z + dz;
                if ((uint)nx >= (uint)sx || (uint)nz >= (uint)sz) continue;
                float r = MathF.Sqrt(dx * dx + dz * dz);
                if (r <= radius) visit(nx + nz * sx, r);
            }
    }

    static float Smoothstep(float e0, float e1, float v)
    {
        float t = Math.Clamp((v - e0) / (e1 - e0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
