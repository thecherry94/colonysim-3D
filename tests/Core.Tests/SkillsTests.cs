namespace ColonySim.Core.Tests;

public class SkillsTests
{
    [Fact] public void Skills_LevelUpCarriesOver_CapsAt20()
    {
        var s = new Skill();
        Skills.AddXp(s, 500); // level 0 needs 400
        Assert.Equal((1, 100f), (s.Level, s.Xp));
        Skills.AddXp(s, 700 + 1200 + 50); // the rest of level 1 (800), all of level 2 (1200), 50 into level 3
        Assert.Equal((3, 50f), (s.Level, s.Xp));
        Skills.AddXp(s, 1e7f);
        Assert.Equal((Skills.MaxLevel, 0f), (s.Level, s.Xp));
        Skills.AddXp(s, 1000);
        Assert.Equal((20, 0f), (s.Level, s.Xp));
    }

    [Fact] public void SpeedFactor_Endpoints()
    {
        Assert.Equal(0.5f, Skills.SpeedFactor(0));
        Assert.Equal(2.0f, Skills.SpeedFactor(20), 3);
    }

    [Fact] public void XpForNextLevel_Curve() => Assert.Equal((400f, 8400f), (Skills.XpForNextLevel(0), Skills.XpForNextLevel(20)));

    [Fact] public void WorkTypes_CoverEveryJobKindOnce()
    {
        var kinds = WorkTypes.All.SelectMany(w => w.Kinds).ToList();
        Assert.Equal(Enum.GetValues<JobKind>().OrderBy(k => k), kinds.OrderBy(k => k));
        Assert.Equal([JobKind.Dig, JobKind.CarveStairs], WorkTypes.Mining.Kinds);
        Assert.Equal(SkillType.Construction, WorkTypes.Construction.Skill);
    }

    [Fact] public void Factory_StartingColonists_UniqueNames_OneOrTwoStrengths()
    {
        var colony = new Colony(new Int3(10, 151, 10));
        var rng = new Random(7);
        var made = Enumerable.Range(0, 40).Select(_ => ColonistFactory.Create(colony, rng)).ToList();

        Assert.True(ColonistNames.All.Count >= 40);
        Assert.Equal(made, colony.Colonists);
        Assert.Equal(Enumerable.Range(1, 40), made.Select(c => c.Id));
        Assert.Equal(41, colony.NextColonistId);
        Assert.Equal(40, made.Select(c => c.Name).Distinct().Count());
        var strengthCounts = new HashSet<int>();
        foreach (var c in made)
        {
            Assert.Equal(Enum.GetValues<SkillType>().Length, c.Skills.Length);
            int strengths = c.Skills.Count(s => s.Level >= 6);
            Assert.InRange(strengths, 1, 2);
            strengthCounts.Add(strengths);
            Assert.All(c.Skills, s => Assert.True(s.Level is >= 0 and <= 4 or >= 6 and <= 12, $"level {s.Level}"));
            Assert.All(c.Skills, s => Assert.Equal(0f, s.Xp));
            Assert.Equal(Enumerable.Repeat(3, WorkTypes.All.Count), c.Priorities);
            Assert.Equal(c.Id % 8, c.ShirtColor);
            Assert.Equal(ColonistActivity.Idle, c.Activity);
        }
        Assert.Equal([1, 2], strengthCounts.Order());
    }

    [Fact] public void SimClock_Days() =>
        Assert.Equal((1, 1, 2), (SimClock.Day(0), SimClock.Day(SimClock.TicksPerDay - 1), SimClock.Day(SimClock.TicksPerDay)));
}
