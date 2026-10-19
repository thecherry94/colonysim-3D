namespace ColonySim.Core;

/// <summary>The skill curves (placeholders to tune, ruling 7): experience per level and the work speed a level gives.</summary>
public static class Skills
{
    public const int MaxLevel = 20;

    /// <summary>The experience that takes a skill from <paramref name="level"/> to the next.</summary>
    public static float XpForNextLevel(int level) => 400f * (level + 1);

    /// <summary>Work speed at a level: 0.5 at 0, 2 at <see cref="MaxLevel"/>.</summary>
    public static float SpeedFactor(int level) => 0.5f + 0.075f * level;

    /// <summary>Adds experience, levelling up as often as it reaches; the excess carries over. At <see cref="MaxLevel"/> it stays at 0.</summary>
    public static void AddXp(Skill s, float xp)
    {
        if (s.Level >= MaxLevel)
        {
            s.Xp = 0;
            return;
        }
        s.Xp += xp;
        while (s.Level < MaxLevel && s.Xp >= XpForNextLevel(s.Level))
        {
            s.Xp -= XpForNextLevel(s.Level);
            s.Level++;
        }
        if (s.Level >= MaxLevel) s.Xp = 0;
    }
}
