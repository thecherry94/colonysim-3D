namespace ColonySim.Core;

/// <summary>Deterministic, well-mixed integer coordinate hash.</summary>
public static class Hash
{
    public static uint Of(int seed, int x, int y, int z)
    {
        unchecked
        {
            uint h = (uint)seed * 0x9E3779B1u + 0x165667B1u;
            h = Mix(h, (uint)x, 0x85EBCA77u);
            h = Mix(h, (uint)y, 0xC2B2AE3Du);
            h = Mix(h, (uint)z, 0x27D4EB2Fu);
            h ^= h >> 15; h *= 0x85EBCA77u;
            h ^= h >> 13; h *= 0xC2B2AE3Du;
            h ^= h >> 16;
            return h;
        }
    }

    /// <summary>Hash mapped to [0,1).</summary>
    public static float Unit(int seed, int x, int y, int z) => (Of(seed, x, y, z) >> 8) * (1f / 16777216f);

    static uint Mix(uint h, uint v, uint prime)
    {
        unchecked
        {
            h += v * prime;
            h = (h << 13) | (h >> 19);
            return h * 0x9E3779B1u;
        }
    }
}
