namespace ColonySim.Core;

/// <summary>Trees (oak and pine) and boulders on grass. Candidates sit on a jittered grid; runs sequentially after caves and ores.</summary>
public static class SurfaceDecorator
{
    const int Cell = 5;
    const int PineLine = 160;
    const float BoulderChance = 0.006f;

    public static void Decorate(VoxelWorld world, MapSettings s)
    {
        var forest = HeightField.Noise(s.Seed, 21, 0.01f, FastNoiseLite.FractalType.FBm, 3);
        var pines = HeightField.Noise(s.Seed, 22, 0.02f, FastNoiseLite.FractalType.None, 1);
        for (int gz = 0; gz * Cell < world.SizeZ; gz++)
            for (int gx = 0; gx * Cell < world.SizeX; gx++)
            {
                int x = gx * Cell + (int)(Hash.Of(s.Seed, gx, 101, gz) % Cell);
                int z = gz * Cell + (int)(Hash.Of(s.Seed, gx, 102, gz) % Cell);
                if (x >= world.SizeX || z >= world.SizeZ) continue;
                var rng = new Random(unchecked((int)Hash.Of(s.Seed, x, 103, z)));
                if (rng.NextSingle() < ForestDensity(forest.GetNoise(x, z)))
                {
                    int h = world.GetSurfaceHeight(x, z);
                    bool pine = h >= PineLine || pines.GetNoise(x, z) > 0.45f;
                    TryTree(world, s.Terrain, rng, x, z, pine);
                }
                else if (rng.NextSingle() < BoulderChance)
                    Boulder(world, rng, x, z);
            }
    }

    /// <summary>Clearings where the noise is low, dense woods where it is high.</summary>
    static float ForestDensity(float n) => Math.Clamp((n + 0.05f) / 0.5f, 0f, 1f) * 0.85f;

    static void TryTree(VoxelWorld world, TerrainParams t, Random rng, int x, int z, bool pine)
    {
        int h = world.GetSurfaceHeight(x, z);
        if (h >= t.MountainRockLine || world.GetBlock(x, h, z) != Blocks.Grass) return;
        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, nz = z + dz;
                if (nx < 0 || nz < 0 || nx >= world.SizeX || nz >= world.SizeZ) return;
                if (Math.Abs(world.GetSurfaceHeight(nx, nz) - h) > 1) return;
            }

        int trunk = pine ? 6 + rng.Next(4) : 4 + rng.Next(3);
        for (int y = h + 1; y <= h + trunk + 2; y++)
            if (world.GetBlock(x, y, z) != Blocks.Air) return;

        ushort log = pine ? Blocks.PineLog : Blocks.OakLog;
        world.SetBlock(x, h, z, Blocks.Dirt, markDirty: false);
        for (int y = h + 1; y <= h + trunk; y++) world.SetBlock(x, y, z, log, markDirty: false);
        if (pine) PineCrown(world, rng, x, h + trunk, z);
        else OakCrown(world, rng, x, h + trunk, z);
    }

    /// <summary>A ragged ball of radius 2 around the trunk top: edge cells are dropped at random.</summary>
    static void OakCrown(VoxelWorld world, Random rng, int x, int top, int z)
    {
        for (int dy = -2; dy <= 2; dy++)
            for (int dz = -2; dz <= 2; dz++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    int d = dx * dx + dy * dy + dz * dz;
                    if (d > 5 || (d >= 4 && rng.Next(3) == 0)) continue;
                    Leaf(world, x + dx, top + dy, z + dz, Blocks.OakLeaves);
                }
    }

    /// <summary>A cone from a few blocks above the ground to one block above the trunk, widest (radius 2-3) at the bottom.</summary>
    static void PineCrown(VoxelWorld world, Random rng, int x, int top, int z)
    {
        int bottom = top - 4 - rng.Next(2);
        int baseRadius = 2 + rng.Next(2);
        for (int y = bottom; y <= top + 1; y++)
        {
            float r = baseRadius * (top + 1 - y) / (float)(top + 1 - bottom);
            if ((top - y) % 2 == 1) r = Math.Max(r - 0.7f, 0.5f); // alternating layers give the stepped silhouette
            float r2 = r * r + 0.5f;
            int ri = (int)MathF.Ceiling(r);
            for (int dz = -ri; dz <= ri; dz++)
                for (int dx = -ri; dx <= ri; dx++)
                    if (dx * dx + dz * dz <= r2) Leaf(world, x + dx, y, z + dz, Blocks.PineLeaves);
        }
    }

    static void Leaf(VoxelWorld world, int x, int y, int z, ushort leaves)
    {
        if (world.GetBlock(x, y, z) == Blocks.Air) world.SetBlock(x, y, z, leaves, markDirty: false);
    }

    /// <summary>2-4 cobblestones on grass: one at the candidate, the rest beside it or stacked on top.</summary>
    static void Boulder(VoxelWorld world, Random rng, int x, int z)
    {
        int h = world.GetSurfaceHeight(x, z);
        if (world.GetBlock(x, h, z) != Blocks.Grass || world.GetBlock(x, h + 1, z) != Blocks.Air) return;
        world.SetBlock(x, h + 1, z, Blocks.Cobblestone, markDirty: false);
        int extra = 1 + rng.Next(3);
        for (int i = 0; i < extra; i++)
        {
            int nx = x + rng.Next(3) - 1, nz = z + rng.Next(3) - 1;
            if (!world.InBounds(nx, 0, nz)) continue;
            int y = world.GetSurfaceHeight(nx, nz) + 1;
            while (world.GetBlock(nx, y, nz) == Blocks.Cobblestone) y++;
            ushort below = world.GetBlock(nx, y - 1, nz);
            if (y <= h + 2 && world.GetBlock(nx, y, nz) == Blocks.Air && (below == Blocks.Grass || below == Blocks.Cobblestone))
                world.SetBlock(nx, y, nz, Blocks.Cobblestone, markDirty: false);
        }
    }
}
