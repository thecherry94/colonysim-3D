namespace ColonySim.Core;

/// <summary>
/// Carves sealed caves: winding spaghetti tunnels (two noises both near zero) and occasional caverns (a third noise above a threshold).
/// Noise is sampled on a world-aligned lattice and interpolated, so results depend only on position and chunk columns may be carved in parallel.
/// Reads neighbor surface heights, so every column must be filled before any is carved.
/// </summary>
public sealed class CaveCarver
{
    const int MinY = 6;
    const int MountainRoof = 2;
    const int CavernDepth = 12;
    // Depth below the local surface shapes the tunnels: roomy and tall in the shallow band (where colonists work),
    // tight, squat and broken up past DeepFrom. Between the two everything is interpolated.
    const int ShallowTo = 40, DeepFrom = 90;
    const float ShallowWidth = 0.018f, DeepWidth = 0.0025f;       // threshold on a² + b²
    const float TallSquash = 1.2f, FlatSquash = 1.7f;              // noise y scale: lower stretches tunnels vertically
    const float DeepBreakup = 0.05f;                               // fully deep tunnels need the cavern noise above this, leaving gaps
    const float CavernThreshold = 0.6f;
    const int Step = 4;                       // lattice spacing; the finest noise octave spans ~25 blocks, so interpolation is visually exact
    const int Lx = VoxelWorld.ChunkSize / Step + 1;

    readonly TerrainParams _t;
    readonly FastNoiseLite _tunnelA, _tunnelB, _cavern;

    /// <summary>0 in the shallow band, 1 from <see cref="DeepFrom"/> down, smooth between.</summary>
    static float DeepBlend(int depth)
    {
        float t = Math.Clamp((depth - ShallowTo) / (float)(DeepFrom - ShallowTo), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    public CaveCarver(MapSettings s)
    {
        _t = s.Terrain;
        _tunnelA = HeightField.Noise(s.Seed, 11, 0.008f, FastNoiseLite.FractalType.FBm, 2);
        _tunnelB = HeightField.Noise(s.Seed, 12, 0.008f, FastNoiseLite.FractalType.FBm, 2);
        _cavern = HeightField.Noise(s.Seed, 13, 0.01f, FastNoiseLite.FractalType.FBm, 3);
    }

    public void CarveChunkColumn(VoxelWorld world, int chunkX, int chunkZ)
    {
        const int cs = VoxelWorld.ChunkSize;
        int x0 = chunkX * cs, z0 = chunkZ * cs;

        var top = new int[cs * cs];           // highest carvable y per column (below MinY = skip)
        var cavernTop = new int[cs * cs];
        int maxTop = -1;
        for (int lz = 0; lz < cs; lz++)
            for (int lx = 0; lx < cs; lx++)
            {
                int i = lx + lz * cs;
                int h = world.GetSurfaceHeight(x0 + lx, z0 + lz);
                top[i] = CarveTop(world, x0 + lx, z0 + lz, h);
                cavernTop[i] = Math.Min(top[i], h - CavernDepth - 1);
                maxTop = Math.Max(maxTop, top[i]);
            }
        if (maxTop < MinY) return;

        int ly = maxTop / Step + 2;
        var a = new float[Lx * Lx * ly];       // tunnel noises, stretched tall...
        var b = new float[a.Length];
        var a2 = new float[a.Length];          // ...and squashed flat; columns blend the two by depth
        var b2 = new float[a.Length];
        var c = new float[a.Length];
        for (int j = 0; j < ly; j++)
            for (int k = 0; k < Lx; k++)
                for (int i = 0; i < Lx; i++)
                {
                    float x = x0 + i * Step, y = j * Step, z = z0 + k * Step;
                    int n = (j * Lx + k) * Lx + i;
                    a[n] = _tunnelA.GetNoise(x, y * TallSquash, z);
                    b[n] = _tunnelB.GetNoise(x, y * TallSquash, z);
                    a2[n] = _tunnelA.GetNoise(x, y * FlatSquash, z);
                    b2[n] = _tunnelB.GetNoise(x, y * FlatSquash, z);
                    c[n] = _cavern.GetNoise(x, y * 2f, z);
                }

        const float inv = 1f / Step;
        for (int lz = 0; lz < cs; lz++)
            for (int lx = 0; lx < cs; lx++)
            {
                int col = lx + lz * cs, t = top[col], ct = cavernTop[col];
                if (t < MinY) continue;
                int surface = world.GetSurfaceHeight(x0 + lx, z0 + lz);
                int i0 = lx / Step, k0 = lz / Step;
                float fx = (lx % Step) * inv, fz = (lz % Step) * inv;
                for (int y = MinY; y <= t; y++)
                {
                    int j0 = y / Step;
                    float fy = (y % Step) * inv;
                    int n = (j0 * Lx + k0) * Lx + i0;
                    float u = DeepBlend(surface - y);
                    float va = Lerp3(a, n, fx, fy, fz), vb = Lerp3(b, n, fx, fy, fz);
                    if (u > 0f)
                    {
                        va += (Lerp3(a2, n, fx, fy, fz) - va) * u;
                        vb += (Lerp3(b2, n, fx, fy, fz) - vb) * u;
                    }
                    float vc = Lerp3(c, n, fx, fy, fz);
                    float width = ShallowWidth + (DeepWidth - ShallowWidth) * u;
                    bool carve = (va * va + vb * vb < width && (u < 0.5f || vc > DeepBreakup * (2f * u - 1f)))
                        || (y <= ct && vc > CavernThreshold);
                    if (carve) world.SetBlock(x0 + lx, y, z0 + lz, Blocks.Air, markDirty: false);
                }
            }
    }

    /// <summary>
    /// Highest y caves may reach in a column. Mountain columns keep a thin roof so caves open onto mountain faces;
    /// elsewhere the roof is measured from the lowest neighboring surface so caves never open on slopes or touch water.
    /// </summary>
    int CarveTop(VoxelWorld world, int x, int z, int h)
    {
        if (h <= _t.WaterLevel) return -1;
        int lowest = h;
        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, nz = z + dz;
                if (nx < 0 || nz < 0 || nx >= world.SizeX || nz >= world.SizeZ) continue;
                lowest = Math.Min(lowest, world.GetSurfaceHeight(nx, nz));
            }
        if (h >= _t.MountainRockLine && lowest > _t.WaterLevel) return h - MountainRoof;
        return lowest - _t.CaveRoof;
    }

    static float Lerp3(float[] v, int n, float fx, float fy, float fz)
    {
        const int dz = Lx, dy = Lx * Lx;
        float c00 = v[n] + (v[n + 1] - v[n]) * fx;
        float c10 = v[n + dz] + (v[n + dz + 1] - v[n + dz]) * fx;
        float c01 = v[n + dy] + (v[n + dy + 1] - v[n + dy]) * fx;
        float c11 = v[n + dy + dz] + (v[n + dy + dz + 1] - v[n + dy + dz]) * fx;
        float c0 = c00 + (c10 - c00) * fz;
        float c1 = c01 + (c11 - c01) * fz;
        return c0 + (c1 - c0) * fy;
    }
}
