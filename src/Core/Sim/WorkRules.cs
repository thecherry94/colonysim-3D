namespace ColonySim.Core;

/// <summary>
/// What working a job takes: where a colonist can stand to do it (its reach), whether it can be done yet, how long
/// it takes and which work type it is.
/// </summary>
public static class WorkRules
{
    /// <summary>The most work spots a job can have: 3 × 3 columns × 4 heights, less those the target itself rules out.</summary>
    public const int MaxWorkSpots = 34;

    static readonly WorkType[] Types = Enum.GetValues<JobKind>()
        .Select(k => WorkTypes.All.Single(t => t.Kinds.Contains(k))).ToArray();

    /// <summary>
    /// The standable cells S a colonist can work the job from: |dx| ≤ 1 and |dz| ≤ 1 from the target, the target's y in
    /// [S.y − 1, S.y + 2], and the target none of S, S + up (its body) or S − up (its support). A carve is the exception
    /// to the last: the stair it leaves still holds the colonist up, so a staircase is carved standing on each step,
    /// the only place the step below can be reached from while it is still being dug out.
    /// </summary>
    public static IEnumerable<Int3> WorkSpots(ColonyMap map, Job job)
    {
        var spots = new Int3[MaxWorkSpots];
        int n = WorkSpots(map.Nav.Rules, job, spots);
        return spots.Take(n);
    }

    /// <summary><see cref="WorkSpots(ColonyMap, Job)"/>, written into <paramref name="into"/> (at least <see cref="MaxWorkSpots"/> long).</summary>
    internal static int WorkSpots(NavRules rules, Job job, Span<Int3> into)
    {
        int n = 0, onTop = job.Kind == JobKind.CarveStairs ? 1 : -2;
        for (int dy = -2; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0 && dy != -2 && dy != onTop) continue; // the target is S, S + up or S − up
                    var s = job.Position + new Int3(dx, dy, dz);
                    if (rules.IsStandable(s)) into[n++] = s;
                }
        return n;
    }

    /// <summary>Digs go top-down and builds bottom-up: ready when no job must go first (<see cref="JobQueue"/> keeps the order).</summary>
    public static bool IsReady(ColonyMap map, Job job) => job.WaitingFor == 0;

    /// <summary>Work ticks at speed 1: 40 per point of the target block's hardness, at least 10.</summary>
    public static int WorkTicks(Job job, BlockDef target) => Math.Max(10, (int)MathF.Ceiling(target.Hardness * 40));

    public static WorkType TypeOf(JobKind k) => Types[(int)k];

    /// <summary>The block the work is on: the one dug or carved (as the colony knows it), or the one built.</summary>
    internal static BlockDef Target(ColonyMap map, Job job)
    {
        var p = job.Position;
        return Blocks.Registry.Get(IsBuild(job.Kind) ? job.Block : map.View.GetKnownBlock(p.X, p.Y, p.Z));
    }

    internal static bool IsBuild(JobKind k) => k is not (JobKind.Dig or JobKind.CarveStairs);

    /// <summary>The block the job leaves at its cell: air for a dig, else its block.</summary>
    internal static ushort ResultBlock(Job job) => job.Kind == JobKind.Dig ? Blocks.Air : job.Block;

    /// <summary>
    /// The job still has something to do: a dig has a diggable block to take (as the colony knows it, so undiscovered
    /// space still needs digging), a carve a solid block to shape, a build an open cell (air or water) to fill.
    /// </summary>
    internal static bool NeedsWork(ColonyMap map, Job job)
    {
        var p = job.Position;
        return job.Kind switch
        {
            JobKind.Dig => Blocks.Registry.Get(map.View.GetKnownBlock(p.X, p.Y, p.Z)).IsDiggable,
            JobKind.CarveStairs => Blocks.Registry.Get(map.View.GetKnownBlock(p.X, p.Y, p.Z)).IsSolid,
            _ => map.World.GetBlock(p.X, p.Y, p.Z) is var b && (b == Blocks.Air || b == Blocks.Water),
        };
    }

    /// <summary>
    /// Clears a job with nothing left to do (<see cref="NeedsWork"/> false): through <see cref="WorkCompletion"/>, which
    /// drops it, except a dig on something that can't be dug (water, bedrock), which is simply removed.
    /// </summary>
    internal static void DropMoot(ColonyMap map, Job job)
    {
        var p = job.Position;
        if (job.Kind == JobKind.Dig && map.View.GetKnownBlock(p.X, p.Y, p.Z) != Blocks.Air) map.Jobs.Remove(p);
        else WorkCompletion.Complete(map, job);
    }
}
