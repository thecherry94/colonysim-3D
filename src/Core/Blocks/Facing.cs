namespace ColonySim.Core;

public enum Facing { North, East, South, West }

public static class FacingExtensions
{
    /// <summary>North = -Z, East = +X, South = +Z, West = -X.</summary>
    public static Int3 Offset(this Facing f) => f switch
    {
        Facing.North => new Int3(0, 0, -1),
        Facing.East => new Int3(1, 0, 0),
        Facing.South => new Int3(0, 0, 1),
        _ => new Int3(-1, 0, 0),
    };
}
