namespace ColonySim.Core;

/// <summary>A kind of work colonists are prioritised for: the jobs it covers and the skill that speeds them (none for unskilled work).</summary>
public sealed record WorkType(string Id, string Label, SkillType? Skill, IReadOnlyList<JobKind> Kinds);
