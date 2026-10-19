namespace ColonySim.Core;

/// <summary>One ore's depth band and vein shape. Vein counts are per 32^3 chunk.</summary>
public sealed record OreBand(ushort Block, int MinY, int MaxY, int VeinsPerChunk, int MinSize, int MaxSize);

/// <summary>
/// Random-walk ore veins inside depth bands, then an extra pass of coal and iron veins along shallow caves
/// (iron may reach above its band there, up to the surface minus the cave roof). They replace only Stone or DeepStone. Runs sequentially.
/// </summary>
public static class OrePlacer
{
    public static IReadOnlyList<OreBand> Bands { get; } = new[]
    {
        new OreBand(Blocks.CoalOre, 70, 180, 6, 6, 14),
        new OreBand(Blocks.CopperOre, 50, 140, 4, 4, 10),
        new OreBand(Blocks.IronOre, 20, 110, 4, 4, 9),
        new OreBand(Blocks.GoldOre, 5, 50, 2, 3, 7),
    };

    const int CaveShallowMax = 40;       // depth below the surface down to which caves get the extra veins
    const float CaveVeinChance = 0.035f;  // per stone block touching shallow cave air
    static readonly OreBand CaveCoal = Bands[0], CaveIron = Bands[2];

    public static void Place(VoxelWorld world, MapSettings s)
    {
        PlaceBands(world, s);
        PlaceAlongCaves(world, s);
    }

    static void PlaceBands(VoxelWorld world, MapSettings s)
    {
        const int cs = VoxelWorld.ChunkSize;
        for (int ore = 0; ore < Bands.Count; ore++)
        {
            var band = Bands[ore];
            for (int cy = band.MinY / cs; cy <= band.MaxY / cs; cy++)
                for (int cz = 0; cz < world.ChunksZ; cz++)
                    for (int cx = 0; cx < world.ChunksX; cx++)
                    {
                        var rng = new Random(unchecked((int)Hash.Of(s.Seed, ore, cx * 64 + cy, cz)));
                        for (int v = 0; v < band.VeinsPerChunk; v++)
                        {
                            int x = cx * cs + rng.Next(cs), y = cy * cs + rng.Next(cs), z = cz * cs + rng.Next(cs);
                            int size = rng.Next(band.MinSize, band.MaxSize + 1);
                            // Starts drawn over the whole chunk keep density uniform where a band covers part of a chunk.
                            if (y < band.MinY || y > band.MaxY) continue;
                            Vein(world, band, band.MinY, band.MaxY, 0, rng, x, y, z, size);
                        }
                    }
        }
    }

    /// <summary>Seeds coal and iron veins on stone touching cave air in the shallow band, so early caves are worth walking into.</summary>
    static void PlaceAlongCaves(VoxelWorld world, MapSettings s)
    {
        int roof = s.Terrain.CaveRoof;
        for (int z = 1; z < world.SizeZ - 1; z++)
            for (int x = 1; x < world.SizeX - 1; x++)
            {
                int h = world.GetSurfaceHeight(x, z);
                for (int y = Math.Max(h - CaveShallowMax, 1); y <= h - roof; y++)
                {
                    if (world.GetBlock(x, y, z) != Blocks.Air) continue;
                    for (int f = 0; f < 6; f++)
                    {
                        int nx = x + Dx[f], ny = y + Dy[f], nz = z + Dz[f];
                        ushort b = world.GetBlock(nx, ny, nz);
                        if (b != Blocks.Stone && b != Blocks.DeepStone) continue;
                        uint hash = Hash.Of(s.Seed, nx * 7 + ny, nz, 4000);
                        if ((hash >> 8) * (1f / 16777216f) >= CaveVeinChance) continue;
                        var rng = new Random(unchecked((int)Hash.Of(s.Seed, nx, ny, nz)));
                        bool iron = (hash & 3) == 0;
                        var band = iron ? CaveIron : CaveCoal;
                        int maxY = iron ? Math.Max(band.MaxY, h - roof) : band.MaxY;
                        if (ny < band.MinY || ny > maxY) continue; // coal high on a mountain stays out of its band
                        // Iron may leave its band upward, but never into the roof layer of whichever column the walk reaches.
                        Vein(world, band, band.MinY, maxY, roof, rng, nx, ny, nz, rng.Next(band.MinSize, band.MaxSize + 1));
                    }
                }
            }
    }

    static readonly int[] Dx = { 1, -1, 0, 0, 0, 0 }, Dy = { 0, 0, 1, -1, 0, 0 }, Dz = { 0, 0, 0, 0, 1, -1 };

    /// <summary>Walks until <paramref name="size"/> blocks became ore, giving up after a few steps per block (walks into air or dirt).</summary>
    static void Vein(VoxelWorld world, OreBand band, int minY, int maxY, int roof, Random rng, int x, int y, int z, int size)
    {
        for (int placed = 0, steps = 0; placed < size && steps < size * 4; steps++)
        {
            ushort b = world.GetBlock(x, y, z);
            bool belowRoof = roof == 0 || (x >= 0 && z >= 0 && x < world.SizeX && z < world.SizeZ && y <= world.GetSurfaceHeight(x, z) - roof);
            if (belowRoof && (b == Blocks.Stone || b == Blocks.DeepStone)) { world.SetBlock(x, y, z, band.Block, markDirty: false); placed++; }
            switch (rng.Next(6))
            {
                case 0: x++; break;
                case 1: x--; break;
                case 2: z++; break;
                case 3: z--; break;
                case 4: if (y < maxY) y++; break;
                default: if (y > minY) y--; break;
            }
        }
    }
}
