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

    /// <summary>N to E to S to W to N.</summary>
    public static Facing Clockwise(this Facing f) => (Facing)(((int)f + 1) & 3);

    public static Facing Opposite(this Facing f) => (Facing)(((int)f + 2) & 3);

    /// <summary>Inverse of <see cref="Offset"/>; only unit offsets are valid.</summary>
    public static Facing FromOffset(int dx, int dz) => (dx, dz) switch
    {
        (0, -1) => Facing.North,
        (1, 0) => Facing.East,
        (0, 1) => Facing.South,
        (-1, 0) => Facing.West,
        _ => throw new ArgumentException($"Not a unit offset: ({dx}, {dz})"),
    };
}
