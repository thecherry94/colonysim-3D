namespace ColonySim.Core;

public sealed record MapSettings(int Seed, int SizeX, int SizeZ, TerrainParams Terrain)
{
    public static MapSettings Create(int seed, MapSize size)
    {
        int d = MapSizes.Dimension(size);
        return new MapSettings(seed, d, d, new TerrainParams());
    }
}
