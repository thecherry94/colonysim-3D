namespace ColonySim.Core;

public sealed record HeightFieldResult(int[] Heights, bool RiverAlongX);

/// <summary>Terrain shape: gentle hills, distinct mountain masses and a meandering river. Indexed x + z * SizeX.</summary>
public static class HeightField
{
    const int MaxHeight = 236;
    const float BankWidth = 12f;

    public static HeightFieldResult Generate(MapSettings s)
    {
        var t = s.Terrain;
        int sx = s.SizeX, sz = s.SizeZ;

        var hills = Noise(s.Seed, 1, t.HillFrequency, FastNoiseLite.FractalType.FBm, 4);
        var maskNoise = Noise(s.Seed, 2, 0.003f, FastNoiseLite.FractalType.FBm, 2);
        var ridge = Noise(s.Seed, 3, 0.012f, FastNoiseLite.FractalType.Ridged, 4);

        // Mountain mask: remap so that about MountainCoverage of the area is above 0, then smoothstep.
        var raw = new float[sx * sz];
        for (int z = 0; z < sz; z++)
            for (int x = 0; x < sx; x++)
                raw[x + z * sx] = maskNoise.GetNoise(x, z);
        float hi = raw.Max();
        float threshold = Quantile(raw, 1f - t.MountainCoverage);
        float span = Math.Max((hi - threshold) * 0.6f, 1e-4f);

        bool alongX = (Hash.Of(s.Seed, 7, 7, 7) & 1) == 0;
        int along = alongX ? sx : sz, across = alongX ? sz : sx;
        float[] center = new float[along];
        float[] bed = new float[along];
        if (t.HasRiver)
        {
            var meander = Noise(s.Seed, 4, 0.006f, FastNoiseLite.FractalType.FBm, 2);
            var depth = Noise(s.Seed, 5, 0.02f, FastNoiseLite.FractalType.None, 1);
            for (int a = 0; a < along; a++)
            {
                float off = Math.Clamp(meander.GetNoise(a, 0f) * 1.6f, -1f, 1f) * 0.25f * across;
                center[a] = across * 0.5f + off;
                bed[a] = t.WaterLevel - 4f + Math.Clamp(depth.GetNoise(a, 0f) * 1.5f, -1f, 1f);
            }
        }

        float mountainRange = Math.Max(t.MountainMaxHeight - t.BaseHeight, 1);
        float halfWidth = t.RiverWidth * 0.5f;
        int minH = t.WaterLevel - 8;
        var heights = new int[sx * sz];
        for (int z = 0; z < sz; z++)
            for (int x = 0; x < sx; x++)
            {
                float h = t.BaseHeight + t.HillAmplitude * Math.Clamp(hills.GetNoise(x, z), -1f, 1f);

                float m = t.MountainCoverage <= 0f ? 0f
                    : t.MountainCoverage >= 1f ? 1f
                    : Smooth((raw[x + z * sx] - threshold) / span);
                if (m > 0f)
                {
                    float r = Math.Clamp((ridge.GetNoise(x, z) + 1f) * 0.5f, 0f, 1f);
                    h += m * (0.3f + 0.7f * r) * mountainRange;
                }

                if (t.HasRiver)
                {
                    int a = alongX ? x : z, b = alongX ? z : x;
                    // Distance across the flow, not along the grid axis: a slanted river would otherwise run narrow.
                    float slope = (center[Math.Min(a + 1, along - 1)] - center[Math.Max(a - 1, 0)]) / (Math.Min(a + 1, along - 1) - Math.Max(a - 1, 0));
                    float d = Math.Abs(b - center[a]) * MathF.Cos(MathF.Atan(slope));
                    if (d < halfWidth + BankWidth)
                    {
                        float k = d <= halfWidth ? 0f : Smooth((d - halfWidth) / BankWidth);
                        h = bed[a] + (h - bed[a]) * k; // river overrides mountains
                    }
                }

                heights[x + z * sx] = Math.Clamp((int)MathF.Round(h), minH, MaxHeight);
            }
        return new HeightFieldResult(heights, alongX);
    }

    static float Smooth(float v) { v = Math.Clamp(v, 0f, 1f); return v * v * (3f - 2f * v); }

    static float Quantile(float[] values, float q)
    {
        var sorted = (float[])values.Clone();
        Array.Sort(sorted);
        return sorted[Math.Clamp((int)(q * (sorted.Length - 1)), 0, sorted.Length - 1)];
    }

    internal static FastNoiseLite Noise(int seed, int salt, float frequency, FastNoiseLite.FractalType fractal, int octaves)
    {
        var n = new FastNoiseLite(unchecked(seed * 31 + salt * 7919));
        n.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        n.SetFrequency(frequency);
        n.SetFractalType(fractal);
        if (fractal != FastNoiseLite.FractalType.None) n.SetFractalOctaves(octaves);
        return n;
    }
}
