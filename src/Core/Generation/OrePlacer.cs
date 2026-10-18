namespace ColonySim.Core;

/// <summary>One ore's depth band and vein shape. Vein counts are per 32^3 chunk.</summary>
public sealed record OreBand(ushort Block, int MinY, int MaxY, int VeinsPerChunk, int MinSize, int MaxSize);

/// <summary>Random-walk ore veins inside depth bands. They replace only Stone or DeepStone. Runs sequentially.</summary>
public static class OrePlacer
{
    public static IReadOnlyList<OreBand> Bands { get; } = new[]
    {
        new OreBand(Blocks.CoalOre, 70, 180, 6, 6, 14),
        new OreBand(Blocks.CopperOre, 50, 140, 4, 4, 10),
        new OreBand(Blocks.IronOre, 20, 110, 4, 4, 9),
        new OreBand(Blocks.GoldOre, 5, 50, 2, 3, 7),
    };

    public static void Place(VoxelWorld world, MapSettings s)
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
                            Vein(world, band, rng, x, y, z, size);
                        }
                    }
        }
    }

    /// <summary>Walks until <paramref name="size"/> blocks became ore, giving up after a few steps per block (walks into air or dirt).</summary>
    static void Vein(VoxelWorld world, OreBand band, Random rng, int x, int y, int z, int size)
    {
        for (int placed = 0, steps = 0; placed < size && steps < size * 4; steps++)
        {
            ushort b = world.GetBlock(x, y, z);
            if (b == Blocks.Stone || b == Blocks.DeepStone) { world.SetBlock(x, y, z, band.Block, markDirty: false); placed++; }
            switch (rng.Next(6))
            {
                case 0: x++; break;
                case 1: x--; break;
                case 2: z++; break;
                case 3: z--; break;
                case 4: if (y < band.MaxY) y++; break;
                default: if (y > band.MinY) y--; break;
            }
        }
    }
}
