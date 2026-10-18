namespace ColonySim.Core;

public enum MapSize { Small, Medium, Large }

public static class MapSizes
{
    public static int Dimension(MapSize s) => s switch
    {
        MapSize.Small => 256,
        MapSize.Medium => 384,
        MapSize.Large => 512,
        _ => throw new ArgumentOutOfRangeException(nameof(s)),
    };
}
