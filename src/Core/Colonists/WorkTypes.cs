namespace ColonySim.Core;

/// <summary>
/// Every work type, in the order of <see cref="Colonist.Priorities"/> and the priorities grid. This list is the only
/// place to edit when adding one.
/// </summary>
public static class WorkTypes
{
    public static readonly WorkType Mining = new("mining", "Mining", SkillType.Mining, [JobKind.Dig, JobKind.CarveStairs]);

    public static readonly WorkType Construction = new("construction", "Construction", SkillType.Construction,
        [JobKind.BuildWall, JobKind.BuildFloor, JobKind.BuildStairs, JobKind.BuildRamp, JobKind.BuildLadder]);

    public static IReadOnlyList<WorkType> All { get; } = [Mining, Construction];
}
