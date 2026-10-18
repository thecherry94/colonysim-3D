namespace ColonySim.Core;

public readonly record struct Int3(int X, int Y, int Z)
{
    public static readonly Int3 Zero = new(0, 0, 0);
    public static readonly Int3 Up = new(0, 1, 0);
    public static readonly Int3 Down = new(0, -1, 0);

    public static Int3 operator +(Int3 a, Int3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Int3 operator -(Int3 a, Int3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Int3 operator *(Int3 a, int s) => new(a.X * s, a.Y * s, a.Z * s);
}
