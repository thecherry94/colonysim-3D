namespace ColonySim.Core;

/// <summary>One colonist's standing in one skill: its level and the experience gathered toward the next.</summary>
public sealed class Skill
{
    public int Level { get; set; }
    public float Xp { get; set; }
}
