namespace ColonySim.Core;

/// <summary>What a colonist is doing right now.</summary>
public enum ColonistActivity { Idle, Walking, Working, Falling }

/// <summary>How a colonist's last walk ended: <see cref="None"/> while one is under way (or before the first).</summary>
public enum WalkResult { None, Arrived, GaveUp }

/// <summary>
/// A move under way, from feet cell <paramref name="From"/> to <paramref name="To"/>: <paramref name="Elapsed"/> of
/// <paramref name="TotalTicks"/> ticks done. The colonist's <see cref="Colonist.Cell"/> stays <paramref name="From"/> until it ends.
/// </summary>
public readonly record struct Motion(Int3 From, Int3 To, MoveKind Kind, int TotalTicks, int Elapsed);

/// <summary>One colonist: who it is, where it stands (1 wide, 2 tall, feet in <see cref="Cell"/>), its skills and work priorities, and what it is doing.</summary>
public sealed class Colonist(int id, string name)
{
    public int Id { get; } = id;
    public string Name { get; set; } = name;

    /// <summary>Palette index, 0–7.</summary>
    public int ShirtColor { get; set; }

    /// <summary>The feet cell; while a <see cref="Motion"/> is under way, the cell it started from.</summary>
    public Int3 Cell { get; set; }

    public Motion? Motion { get; set; }

    /// <summary>Indexed by <see cref="SkillType"/>.</summary>
    public Skill[] Skills { get; } = Enum.GetValues<SkillType>().Select(_ => new Skill()).ToArray();

    /// <summary>Indexed like <see cref="WorkTypes.All"/>: 0 = off, else 1 (first) to 4 (last).</summary>
    public int[] Priorities { get; } = new int[WorkTypes.All.Count];

    public ColonistActivity Activity { get; set; }

    /// <summary>The job it is on, or 0.</summary>
    public long JobId { get; set; }

    /// <summary>It can't reach home: refreshed when navigation changes and when it lands from a fall.</summary>
    public bool Stuck { get; set; }

    /// <summary>How its last walk ended.</summary>
    public WalkResult LastWalk { get; internal set; }

    /// <summary>Where it stands to work its job (<see cref="JobId"/>); the <see cref="ColonistWorker"/>'s.</summary>
    internal Int3 WorkSpot { get; set; }

    /// <summary>The navigation version its job last passed the <see cref="StrandCheck"/> at.</summary>
    internal int CheckedAt { get; set; }

    /// <summary>Its search was put off (navigation couldn't be brought up to date within the tick's budget): it searches next tick.</summary>
    internal bool SearchPending { get; set; }

    /// <summary>Where it is walking and how, while it walks; the <see cref="ColonistMover"/>'s.</summary>
    internal Walk? Walk { get; set; }
}
