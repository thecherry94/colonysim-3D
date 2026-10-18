namespace ColonySim.Core;

/// <summary>Ground material before caves, ores and water. Shared by generation and fog of war.</summary>
public sealed class TerrainLayers(MapSettings s)
{
    readonly int _seed = s.Seed;
    readonly TerrainParams _t = s.Terrain;

    public ushort BaseMaterial(int x, int y, int z, int surfaceY)
    {
        if (y > surfaceY) return Blocks.Air;
        if (y == 0) return Blocks.Bedrock;
        if (y <= 3) return Hash.Unit(_seed, x, y, z) < (4 - y) / 4f ? Blocks.Bedrock : DeepOrStone(x, y, z);

        int dirtDepth = 3 + (int)(Hash.Of(_seed, x, 0, z) % 3);
        int depth = surfaceY - y; // 0 at the surface block
        if (surfaceY <= _t.WaterLevel + 1)
        {
            if (depth <= dirtDepth) return Blocks.Sand;
        }
        else if (surfaceY < _t.MountainRockLine)
        {
            if (depth == 0) return Blocks.Grass;
            if (depth <= dirtDepth) return Blocks.Dirt;
        }
        return DeepOrStone(x, y, z);
    }

    ushort DeepOrStone(int x, int y, int z) =>
        y < _t.DeepStoneLevel + (int)(Hash.Of(_seed, x, 1, z) % 5) - 2 ? Blocks.DeepStone : Blocks.Stone;
}
