namespace ColonySim.Core;

/// <summary>
/// The moves a 1-wide, 2-tall colonist can make between feet cells, read from the known (always fogged) world.
/// Every rule is symmetric in existence: A→B exists exactly when B→A does (costs may differ, because of water).
/// Never caches world state: every call reads the view, so it can run on any thread that only reads.
/// </summary>
public sealed class NavRules(PlayerView view)
{
    /// <summary>The smallest span <see cref="GetMoves"/> accepts.</summary>
    public const int MaxMoves = 48;

    const byte SolidBit = 1, NaturalBit = 2, LadderBit = 4, LiquidBit = 8;
    const byte NaturalSolid = SolidBit | NaturalBit;
    const byte OutOfBounds = SolidBit; // impassable, and never natural, a ladder or liquid

    // Per block id, from the (immutable) registry: shape flags, and one bit per Facing the block ascends toward.
    static readonly byte[] Flags, AscendMask;

    static NavRules()
    {
        var defs = Blocks.Registry.All;
        Flags = new byte[defs.Count];
        AscendMask = new byte[defs.Count];
        for (int i = 0; i < defs.Count; i++)
        {
            var d = defs[i];
            Flags[i] = (byte)((d.IsSolid ? SolidBit : 0) | (d.IsNatural ? NaturalBit : 0)
                | (d.IsLadder ? LadderBit : 0) | (d.IsLiquid ? LiquidBit : 0));
            for (int f = 0; f < 4; f++)
                if (d.AscendsToward((Facing)f)) AscendMask[i] |= (byte)(1 << f);
        }
    }

    readonly VoxelWorld _world = view.World;

    /// <summary>In bounds, and the known block is not solid: air, water and ladders pass.</summary>
    public bool IsPassable(Int3 c) => (At(c) & SolidBit) == 0;

    /// <summary>
    /// y in [1, 254], the cell and the one above are passable, and the cell stands on a solid block (any shape) or a
    /// ladder, or is itself a ladder.
    /// </summary>
    public bool IsStandable(Int3 c) => IsStandable(c, out _);

    /// <summary>Writes every move out of <paramref name="from"/> into <paramref name="into"/> (at least <see cref="MaxMoves"/> long); none unless it is standable.</summary>
    public int GetMoves(Int3 from, Span<Move> into)
    {
        if (into.Length < MaxMoves) throw new ArgumentException($"Needs room for {MaxMoves} moves", nameof(into));
        if (from.Y < 1 || from.Y > 254) return 0;
        byte here = At(from), under = At(from + Int3.Down);
        if (!Stands(here, At(from + Int3.Up), under)) return 0;
        int n = 0, open = 0; // bit f: feet and head fit through the cell ahead in facing f
        for (int f = 0; f < 4; f++)
        {
            var dir = (Facing)f;
            Int3 ahead = from + dir.Offset();
            // The rules partition on the column ahead (exactly, so these gates only skip reads): every up-move needs
            // the block ahead solid (steps and hops need the cell above it passable, climbs need that cell solid);
            // walking and every down-move need the block ahead passable, and down-moves the cell below it as well.
            byte feet = At(ahead), head = At(ahead + Int3.Up);
            if ((feet & SolidBit) != 0)
            {
                if ((head & SolidBit) == 0)
                {
                    StepUp(from, dir, into, ref n);
                    HopUp(from, dir, into, ref n);
                }
                else
                {
                    ClimbUp(from, dir, 2, into, ref n);
                    ClimbUp(from, dir, 3, into, ref n);
                }
                continue;
            }
            byte below = At(ahead + Int3.Down);
            if ((head & SolidBit) == 0) open |= 1 << f;
            Walk(ahead, feet, head, below, into, ref n);
            if ((below & SolidBit) == 0)
            {
                StepDown(from, dir, into, ref n);
                HopDown(from, dir, into, ref n);
                ClimbDown(from, dir, 2, into, ref n);
                ClimbDown(from, dir, 3, into, ref n);
            }
        }
        Diagonal(from, Facing.East, Facing.South, open, into, ref n);
        Diagonal(from, Facing.East, Facing.North, open, into, ref n);
        Diagonal(from, Facing.West, Facing.South, open, into, ref n);
        Diagonal(from, Facing.West, Facing.North, open, into, ref n);
        LadderUp(from, here, into, ref n);
        LadderDown(from, under, into, ref n);
        return n;
    }

    /// <summary>True when <see cref="GetMoves"/> from <paramref name="from"/> has a move to <paramref name="to"/>.</summary>
    public bool TryGetMove(Int3 from, Int3 to, out Move move)
    {
        Int3 d = to - from;
        if (Math.Abs(d.X) <= 1 && Math.Abs(d.Z) <= 1 && Math.Abs(d.Y) <= 3)
        {
            Span<Move> moves = stackalloc Move[MaxMoves];
            int n = GetMoves(from, moves);
            for (int i = 0; i < n; i++)
                if (moves[i].To == to) { move = moves[i]; return true; }
        }
        move = default;
        return false;
    }

    /// <summary>The highest standable y in the column, or -1.</summary>
    public int TopStandable(int x, int z)
    {
        for (int y = 254; y >= 1; y--)
            if (IsStandable(new Int3(x, y, z))) return y;
        return -1;
    }

    /// <summary>The standable cell nearest <paramref name="around"/> by squared distance within a cube of <paramref name="radius"/>; ties go to higher y, then lower x, then lower z.</summary>
    public bool FindStandableNear(Int3 around, int radius, out Int3 cell)
    {
        cell = default;
        int best = int.MaxValue;
        // Scanning y down, then x and z up, with a strict "<" keeps the first of equal distances: the tie-break order.
        for (int y = Math.Min(around.Y + radius, 254); y >= Math.Max(around.Y - radius, 1); y--)
            for (int x = around.X - radius; x <= around.X + radius; x++)
                for (int z = around.Z - radius; z <= around.Z + radius; z++)
                {
                    int dx = x - around.X, dy = y - around.Y, dz = z - around.Z, d2 = dx * dx + dy * dy + dz * dz;
                    if (d2 >= best || !IsStandable(new Int3(x, y, z))) continue;
                    best = d2;
                    cell = new Int3(x, y, z);
                }
        return best != int.MaxValue;
    }

    ushort Known(Int3 c) => view.GetKnownBlock(c.X, c.Y, c.Z);

    byte At(Int3 c) => _world.InBounds(c.X, c.Y, c.Z) ? Flags[Known(c)] : OutOfBounds;

    bool IsNaturalSolid(Int3 c) => (At(c) & NaturalSolid) == NaturalSolid;

    static bool Ascends(ushort id, Facing dir) => (AscendMask[id] & (1 << (int)dir)) != 0;

    /// <summary>Standability from the flags of the cell, the one above and the one below (y range aside; out of bounds reads as solid).</summary>
    static bool Stands(byte feet, byte head, byte below) =>
        ((feet | head) & SolidBit) == 0 && ((feet & LadderBit) != 0 || (below & (SolidBit | LadderBit)) != 0);

    bool IsStandable(Int3 c, out byte feet)
    {
        feet = OutOfBounds;
        if (c.Y < 1 || c.Y > 254 || !_world.InBounds(c.X, c.Y, c.Z)) return false;
        feet = Flags[Known(c)];
        return (feet & SolidBit) == 0 && Stands(feet, At(c + Int3.Up), At(c + Int3.Down));
    }

    /// <summary>Appends a move; <paramref name="feet"/> are the flags of its destination, where liquid triples cost and ticks.</summary>
    static void Add(Span<Move> into, ref int n, Int3 to, byte feet, MoveKind kind, float cost, int ticks)
    {
        if ((feet & LiquidBit) != 0) { cost *= 3; ticks *= 3; }
        into[n++] = new Move(to, kind, cost, ticks);
    }

    /// <summary>Level with the standable origin, so <paramref name="to"/> is in y range and in bounds whenever its feet are passable.</summary>
    static void Walk(Int3 to, byte feet, byte head, byte below, Span<Move> into, ref int n)
    {
        if (Stands(feet, head, below)) Add(into, ref n, to, feet, MoveKind.Walk, 1.0f, 6);
    }

    /// <summary>
    /// Both side cells, feet and head, must be passable (no cutting corners): that is bits <paramref name="sx"/> and
    /// <paramref name="sz"/> of <paramref name="open"/>.
    /// </summary>
    void Diagonal(Int3 c, Facing sx, Facing sz, int open, Span<Move> into, ref int n)
    {
        if ((open & (1 << (int)sx)) == 0 || (open & (1 << (int)sz)) == 0) return;
        Int3 d = c + sx.Offset() + sz.Offset();
        if (IsStandable(d, out byte feet)) Add(into, ref n, d, feet, MoveKind.Diagonal, 1.4142f, 8);
    }

    void StepUp(Int3 c, Facing dir, Span<Move> into, ref int n)
    {
        Int3 ahead = c + dir.Offset();
        if (!Ascends(Known(ahead), dir)) return;
        Int3 d = ahead + Int3.Up;
        if (IsStandable(d, out byte feet)) Add(into, ref n, d, feet, MoveKind.Step, 1.5f, 8);
    }

    void StepDown(Int3 c, Facing dir, Span<Move> into, ref int n)
    {
        if (!Ascends(Known(c + Int3.Down), dir.Opposite())) return;
        Int3 d = c + dir.Offset() + Int3.Down;
        if (IsStandable(d, out byte feet)) Add(into, ref n, d, feet, MoveKind.Step, 1.5f, 8);
    }

    void LadderUp(Int3 c, byte here, Span<Move> into, ref int n)
    {
        if ((here & LadderBit) == 0) return;
        Int3 d = c + Int3.Up;
        if (IsStandable(d, out byte feet)) Add(into, ref n, d, feet, MoveKind.Ladder, 2.0f, 10);
    }

    void LadderDown(Int3 c, byte under, Span<Move> into, ref int n)
    {
        if ((under & LadderBit) == 0) return;
        Int3 d = c + Int3.Down;
        if (IsStandable(d, out byte feet)) Add(into, ref n, d, feet, MoveKind.Ladder, 2.0f, 10);
    }

    /// <summary>Onto a natural block one up that is not a step that way, with head room above the hopper.</summary>
    void HopUp(Int3 c, Facing dir, Span<Move> into, ref int n)
    {
        Int3 ahead = c + dir.Offset();
        ushort id = Known(ahead);
        if ((Flags[id] & NaturalSolid) != NaturalSolid || Ascends(id, dir)) return;
        if (!IsPassable(c + Int3.Up * 2)) return;
        Int3 d = ahead + Int3.Up;
        if (IsStandable(d, out byte feet)) Add(into, ref n, d, feet, MoveKind.Hop, 6.0f, 20);
    }

    /// <summary>The reverse of <see cref="HopUp"/>: off a natural block, with head room above the landing.</summary>
    void HopDown(Int3 c, Facing dir, Span<Move> into, ref int n)
    {
        ushort id = Known(c + Int3.Down);
        if ((Flags[id] & NaturalSolid) != NaturalSolid || Ascends(id, dir.Opposite())) return;
        Int3 o = dir.Offset();
        if (!IsPassable(c + o + Int3.Up)) return;
        Int3 d = c + o + Int3.Down;
        if (IsStandable(d, out byte feet)) Add(into, ref n, d, feet, MoveKind.Hop, 6.0f, 20);
    }

    /// <summary>Up a natural face <paramref name="k"/> blocks high, with the column above the climber open to k+1 up.</summary>
    void ClimbUp(Int3 c, Facing dir, int k, Span<Move> into, ref int n)
    {
        Int3 o = dir.Offset();
        for (int j = 0; j < k; j++)
            if (!IsNaturalSolid(c + o + Int3.Up * j)) return;
        for (int j = 2; j <= k + 1; j++)
            if (!IsPassable(c + Int3.Up * j)) return;
        Int3 d = c + o + Int3.Up * k;
        if (IsStandable(d, out byte feet)) Add(into, ref n, d, feet, MoveKind.Climb, 10f + 4 * k, 30 * k);
    }

    /// <summary>The exact reverse of <see cref="ClimbUp"/>, seen from the top.</summary>
    void ClimbDown(Int3 c, Facing dir, int k, Span<Move> into, ref int n)
    {
        Int3 o = dir.Offset();
        Int3 d = c + o - Int3.Up * k, face = c - Int3.Up * k;
        for (int j = 0; j < k; j++)
            if (!IsNaturalSolid(face + Int3.Up * j)) return;
        for (int j = 2; j <= k + 1; j++)
            if (!IsPassable(d + Int3.Up * j)) return;
        if (IsStandable(d, out byte feet)) Add(into, ref n, d, feet, MoveKind.Climb, 10f + 4 * k, 30 * k);
    }
}
