using System.Diagnostics;
using System.Reflection;
using Xunit.Abstractions;

namespace ColonySim.Core.Tests;

public class WorkTests
{
    // Flat map: stone to y = 150, so the standing (feet) y is 151; home is (32, 151, 32).

    const int MiningIndex = 0, ConstructionIndex = 1; // in WorkTypes.All

    /// <summary>A colonist at exactly <paramref name="at"/> with the given skill levels and priorities (0 = off).</summary>
    static Colonist Worker(Simulation sim, Int3 at, int mining = 0, int construction = 0, int miningPriority = 3, int constructionPriority = 3)
    {
        var c = sim.Spawn(at);
        Assert.Equal(at, c.Cell);
        c.Skills[(int)SkillType.Mining].Level = mining;
        c.Skills[(int)SkillType.Construction].Level = construction;
        c.Priorities[MiningIndex] = miningPriority;
        c.Priorities[ConstructionIndex] = constructionPriority;
        return c;
    }

    static Job Dig(ColonyMap m, Int3 p) => m.Jobs.Add(new JobSpec(p, JobKind.Dig, Blocks.Air));
    static Job Wall(ColonyMap m, Int3 p) => m.Jobs.Add(new JobSpec(p, JobKind.BuildWall, Blocks.WoodPlanks));

    static void Run(Simulation sim, Func<bool> until, int maxTicks = 5000)
    {
        for (int t = 0; t < maxTicks && !until(); t++) sim.Tick();
        Assert.True(until(), $"not done after {maxTicks} ticks");
    }

    /// <summary>Ticks until every job is done, returning the cells in the order their jobs went.</summary>
    static List<Int3> CompletionOrder(Simulation sim, int maxTicks = 20000)
    {
        var jobs = sim.Map.Jobs;
        var pending = jobs.All.Select(j => j.Position).ToHashSet();
        var order = new List<Int3>();
        for (int t = 0; t < maxTicks && pending.Count > 0; t++)
        {
            sim.Tick();
            foreach (var p in pending.Where(p => !jobs.Contains(p)).ToList())
            {
                order.Add(p);
                pending.Remove(p);
            }
        }
        Assert.True(pending.Count == 0, "left: " + string.Join("; ", jobs.All.Select(j => $"{j.Kind} {j.Position} {j.Status}@{j.StatusVersion}")));
        return order;
    }

    /// <summary>Records which colonist claimed each job first.</summary>
    static Dictionary<long, int> ClaimsUntilDone(Simulation sim, int maxTicks = 5000)
    {
        var claims = new Dictionary<long, int>();
        for (int t = 0; t < maxTicks && sim.Map.Jobs.Count > 0; t++)
        {
            sim.Tick();
            foreach (var j in sim.Map.Jobs.All)
                if (j.ClaimedBy != 0) claims.TryAdd(j.Id, j.ClaimedBy);
        }
        Assert.Equal(0, sim.Map.Jobs.Count);
        return claims;
    }

    static void Fill(ColonyMap m, int x0, int x1, int y0, int y1, int z0, int z1, ushort block)
    {
        for (int y = y0; y <= y1; y++)
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++) m.World.SetBlock(x, y, z, block);
    }

    [Fact] public void Colonist_DigsDesignatedBlock_GainsMiningXp()
    {
        var map = TestWorlds.FlatMap();
        var sim = new Simulation(map);
        var c = Worker(sim, new Int3(30, 151, 32));
        Dig(map, new Int3(33, 150, 32));
        Run(sim, () => map.Jobs.Count == 0);
        Assert.Equal(Blocks.Air, map.World.GetBlock(33, 150, 32));
        // Stone: 2.5 × 40 = 100 work ticks at speed 0.5 (level 0), one experience per tick.
        Assert.Equal(200f, c.Skills[(int)SkillType.Mining].Xp);
        Assert.Equal(0f, c.Skills[(int)SkillType.Construction].Xp);
        Assert.Equal((ColonistActivity.Idle, 0L), (c.Activity, c.JobId));
    }

    [Fact] public void Priorities_SplitWork()
    {
        var map = TestWorlds.FlatMap();
        var sim = new Simulation(map);
        // Each starts next to the job it doesn't want first.
        var a = Worker(sim, new Int3(40, 151, 32), miningPriority: 1, constructionPriority: 3);
        var b = Worker(sim, new Int3(24, 151, 32), miningPriority: 3, constructionPriority: 1);
        var dig = Dig(map, new Int3(26, 150, 32));
        var wall = Wall(map, new Int3(38, 151, 32));
        var claims = ClaimsUntilDone(sim);
        Assert.Equal(a.Id, claims[dig.Id]);
        Assert.Equal(b.Id, claims[wall.Id]);
        Assert.Equal(Blocks.WoodPlanks, map.World.GetBlock(38, 151, 32));
    }

    [Fact] public void SamePriority_PrefersBestSkill()
    {
        var map = TestWorlds.FlatMap();
        var sim = new Simulation(map);
        var c = Worker(sim, new Int3(32, 151, 32), mining: 10, construction: 2);
        var dig = Dig(map, new Int3(32, 150, 36));   // squared distance 17
        Wall(map, new Int3(32, 151, 28));            // squared distance 16
        Run(sim, () => c.JobId != 0);
        Assert.Equal(dig.Id, c.JobId);
        Assert.Equal(c.Id, dig.ClaimedBy);
        Run(sim, () => map.Jobs.Count == 0);
    }

    [Fact] public void BuildsBottomUp_DigsTopDown()
    {
        var map = TestWorlds.FlatMap();
        var sim = new Simulation(map);
        Worker(sim, new Int3(26, 151, 26), mining: 5, construction: 5);
        Worker(sim, new Int3(36, 151, 36), mining: 5, construction: 5);
        for (int y = 151; y <= 153; y++) Wall(map, new Int3(20, y, 20));
        // A trench two wide, three deep: a one-wide shaft can't be reached below its first block.
        for (int y = 150; y >= 148; y--)
        {
            Dig(map, new Int3(40, y, 40));
            Dig(map, new Int3(41, y, 40));
        }
        var order = CompletionOrder(sim);
        Assert.Equal(new[] { 151, 152, 153 }, order.Where(p => p.X == 20).Select(p => p.Y));
        Assert.Equal(new[] { 150, 149, 148 }, order.Where(p => p.X == 40).Select(p => p.Y));
        Assert.Equal(new[] { 150, 149, 148 }, order.Where(p => p.X == 41).Select(p => p.Y));
        for (int y = 148; y <= 150; y++) Assert.Equal(Blocks.Air, map.World.GetBlock(41, y, 40));
        Assert.All(map.Colony.Colonists, c => Assert.False(c.Stuck, $"{c.Name} at {c.Cell}"));
    }

    [Fact] public void CarvedStaircase_LeadsDown_AndIsWalkable()
    {
        var map = TestWorlds.FlatMap();
        var sim = new Simulation(map);
        for (int z = 30; z <= 34; z += 2) Worker(sim, new Int3(30, 151, z), mining: 5);
        Designator.Apply(map, DesignationTool.DigStairs, BuildMaterial.Wood, new DragBox(new Int3(34, 150, 32), new Int3(39, 150, 32)));
        Assert.True(map.Jobs.Count > 6);
        for (int t = 0; t < 20000 && map.Jobs.Count > 0; t++) sim.Tick();
        Assert.True(map.Jobs.Count == 0, "left: " + string.Join("; ", map.Jobs.All.Select(j => $"{j.Kind} {j.Position} {j.Status}")));
        map.Nav.Update();
        var path = new PathFinder(map.Nav).FindNow(new Int3(30, 151, 32), new Int3(39, 146, 32));
        Assert.Equal(PathStatus.Found, path.Status);
        Assert.Equal(Blocks.CarvedStairs(Blocks.Stone, Facing.West), map.World.GetBlock(39, 145, 32));
        Assert.All(map.Colony.Colonists, c => Assert.False(c.Stuck, $"{c.Name} at {c.Cell}"));
    }

    [Fact] public void Unreachable_JobMarked_ColonistIdles()
    {
        var map = TestWorlds.FlatMap();
        // A sealed pocket in the rock: the job's only work spot is inside it.
        TestWorlds.Dig(map, 10, 140, 10);
        TestWorlds.Dig(map, 10, 141, 10);
        var job = Dig(map, new Int3(11, 140, 10));
        var sim = new Simulation(map);
        var c = Worker(sim, new Int3(20, 151, 20));
        for (int t = 0; t < 30; t++) sim.Tick();
        Assert.Equal((JobStatus.Unreachable, map.Nav.Version), (job.Status, job.StatusVersion));
        Assert.Equal((ColonistActivity.Idle, 0L, 0), (c.Activity, c.JobId, job.ClaimedBy));
    }

    [Fact] public void DiggingOnlyLadder_IsNeedsWayBack()
    {
        var map = TestWorlds.FlatMap();
        // A 1×1 shaft six deep with a ladder all the way up: its only way out.
        for (int y = 145; y <= 150; y++)
        {
            TestWorlds.Dig(map, 20, y, 20);
            map.World.SetBlock(20, y, 20, Blocks.Ladder(Facing.North));
        }
        var sim = new Simulation(map);
        var c = Worker(sim, new Int3(20, 145, 20));
        Assert.False(c.Stuck);
        // Two above the bottom cell, which is its only work spot; taking it out cuts the bottom off.
        var job = Dig(map, new Int3(20, 147, 20));
        for (int t = 0; t < 30; t++) sim.Tick();
        Assert.Equal((JobStatus.NeedsWayBack, map.Nav.Version), (job.Status, job.StatusVersion));
        Assert.Equal((ColonistActivity.Idle, 0L, 0), (c.Activity, c.JobId, job.ClaimedBy));
        Assert.Equal(Blocks.Ladder(Facing.North), map.World.GetBlock(20, 147, 20));
    }

    [Fact] public void WallGap_BuiltFromOutside_ColonistNotSealedIn()
    {
        var map = TestWorlds.FlatMap();
        // A 5×5 plank room, 3 high (built blocks can't be climbed), with a 2-high gap under a lintel.
        Fill(map, 20, 24, 151, 153, 20, 24, Blocks.WoodPlanks);
        Fill(map, 21, 23, 151, 153, 21, 23, Blocks.Air);
        Fill(map, 22, 22, 151, 152, 20, 20, Blocks.Air);
        var sim = new Simulation(map);
        var c = Worker(sim, new Int3(22, 151, 22), construction: 5);
        Assert.False(c.Stuck);
        Wall(map, new Int3(22, 151, 20));
        Wall(map, new Int3(22, 152, 20));
        Run(sim, () => map.Jobs.Count == 0);
        Assert.Equal(Blocks.WoodPlanks, map.World.GetBlock(22, 151, 20));
        Assert.Equal(Blocks.WoodPlanks, map.World.GetBlock(22, 152, 20));
        sim.Tick();
        Assert.False(c.Stuck, $"sealed in at {c.Cell}");
        Assert.Equal(sim.HomeComponent, map.Nav.ComponentOf(c.Cell));
        Assert.True(c.Cell.Z < 20, $"inside at {c.Cell}");
    }

    [Fact] public void WallGap_WithAnotherColonistInside_NeedsWayBack()
    {
        var map = TestWorlds.FlatMap();
        Fill(map, 20, 24, 151, 153, 20, 24, Blocks.WoodPlanks);
        Fill(map, 21, 23, 151, 153, 21, 23, Blocks.Air);
        Fill(map, 22, 22, 151, 152, 20, 20, Blocks.Air);
        var sim = new Simulation(map);
        var inside = Worker(sim, new Int3(22, 151, 22), miningPriority: 0, constructionPriority: 0);
        var builder = Worker(sim, new Int3(22, 151, 16), construction: 5);
        var job = Wall(map, new Int3(22, 151, 20));
        for (int t = 0; t < 30; t++) sim.Tick();
        Assert.Equal(JobStatus.NeedsWayBack, job.Status);
        Assert.Equal((ColonistActivity.Idle, 0L, 0), (builder.Activity, builder.JobId, job.ClaimedBy));
        Assert.Equal(Blocks.Air, map.World.GetBlock(22, 151, 20));

        // Looked at again (a new mark, at a new tick) only every so often, not on every search.
        long marked = job.StatusTick;
        while (map.Colony.Tick < marked + JobFinder.SealRecheckTicks) sim.Tick();
        Assert.Equal(marked, job.StatusTick);
        Run(sim, () => job.StatusTick != marked, 2 * ColonistWorker.SearchInterval);
        Assert.Equal(JobStatus.NeedsWayBack, job.Status);

        // Once it walks out, the gap can be closed.
        sim.GoTo(inside, new Int3(22, 151, 12));
        Run(sim, () => map.Jobs.Count == 0);
        Assert.Equal(Blocks.WoodPlanks, map.World.GetBlock(22, 151, 20));
        Assert.False(inside.Stuck);
    }

    [Fact] public void CompletionsInOneTick_WaitForTheSyncBudget()
    {
        var map = TestWorlds.FlatMap(128);
        var sim = new Simulation(map);
        // One per chunk column, so each finish dirties chunks of its own.
        var workers = new List<(Colonist C, Job J)>();
        for (int i = 0; i < 4; i++)
            for (int k = 0; k < 4; k++)
                workers.Add((Worker(sim, new Int3(10 + 32 * i, 151, 10 + 32 * k)), Dig(map, new Int3(13 + 32 * i, 150, 10 + 32 * k))));
        var late = Worker(sim, new Int3(20, 151, 20), miningPriority: 0, constructionPriority: 0); // steps after them all
        Run(sim, () => workers.All(w => w.C.Activity == ColonistActivity.Working));
        while ((map.Colony.Tick + late.Id) % ColonistWorker.SearchInterval != 0) sim.Tick();
        // All one work tick (0.5 at level 0) from done.
        foreach (var (_, j) in workers) map.Jobs.AddProgress(j, 100 - 0.25f - j.Progress);

        // No budget: only the first finish of the tick, which needs no sync, goes through.
        sim.SyncBudgetMs = 0;
        sim.Tick();
        var held = workers.Where(w => map.Jobs.Contains(w.J.Position)).ToList();
        Assert.Equal(workers.Count - 1, held.Count);
        Assert.All(held, w => Assert.True(w.J.Progress >= 100));
        Assert.True(late.SearchPending);
        var xp = held.ToDictionary(w => w.C, w => w.C.Skills[(int)SkillType.Mining].Xp);

        // With the budget: some finish, and the held ones never work on past done.
        sim.SyncBudgetMs = Simulation.MidTickSyncBudgetMs;
        sim.Tick();
        Assert.True(sim.MidTickSyncTime - sim.LastMidTickSync < TimeSpan.FromMilliseconds(Simulation.MidTickSyncBudgetMs),
            $"a sync began past the budget: {sim.MidTickSyncTime.TotalMilliseconds:F2} ms");
        Run(sim, () => workers.All(w => !map.Jobs.Contains(w.J.Position)), 20);
        // It searched as soon as there was budget left after the others, not at its next turn in the stagger.
        Run(sim, () => !late.SearchPending, 1);
        Assert.All(held, w => Assert.Equal(xp[w.C], w.C.Skills[(int)SkillType.Mining].Xp));
    }

    [Fact] public void Search_RemembersNothingFound_AndGoesOnAfterTheCap()
    {
        var map = TestWorlds.FlatMap();
        // Two colonists in a sealed pocket, and 300 dig jobs on the surface they can't reach.
        foreach (var (x, y) in new[] { (10, 140), (10, 141), (11, 140), (11, 141) }) TestWorlds.Dig(map, x, y, 10);
        for (int z = 20; z < 35; z++)
            for (int x = 20; x < 40; x++) Dig(map, new Int3(x, 150, z));
        var sim = new Simulation(map);
        var a = Worker(sim, new Int3(10, 140, 10));
        var b = Worker(sim, new Int3(11, 140, 10));
        var finder = sim.Finder;

        Assert.False(finder.TryFind(sim, a, out _, out _));
        Assert.Equal(JobFinder.MaxChecks, finder.LastChecks);
        Assert.False(finder.TryFind(sim, a, out _, out _));
        Assert.Equal(300 - JobFinder.MaxChecks, finder.LastChecks); // went on from where it stopped
        Assert.False(finder.TryFind(sim, b, out _, out _));
        Assert.Equal(0, finder.LastChecks); // nothing to find from this component, remembered
        Assert.All(map.Jobs.All, j => Assert.Equal(JobStatus.Open, j.Status)); // from outside home, nothing is marked

        // A new job, or a navigation change, and it looks again.
        Dig(map, new Int3(30, 150, 40));
        Assert.False(finder.TryFind(sim, b, out _, out _));
        Assert.Equal(JobFinder.MaxChecks, finder.LastChecks);
    }

    [Fact] public void CancelClaimedJob_ColonistGoesIdle()
    {
        var map = TestWorlds.FlatMap();
        var sim = new Simulation(map);
        var c = Worker(sim, new Int3(30, 151, 32));

        // Cancelled while working on it.
        var job = Dig(map, new Int3(33, 150, 32));
        Run(sim, () => c.Activity == ColonistActivity.Working);
        Designator.Apply(map, DesignationTool.Cancel, BuildMaterial.Wood, new DragBox(job.Position, job.Position));
        sim.Tick();
        Assert.Equal((ColonistActivity.Idle, 0L), (c.Activity, c.JobId));
        Assert.Equal(Blocks.Stone, map.World.GetBlock(33, 150, 32));

        // Cancelled on the way there.
        var far = Dig(map, new Int3(50, 150, 32));
        Run(sim, () => c.Activity == ColonistActivity.Walking && c.Motion != null);
        Assert.Equal(far.Id, c.JobId);
        map.Jobs.Remove(far.Position);
        sim.Tick();
        Assert.Equal((ColonistActivity.Idle, 0L), (c.Activity, c.JobId));
        for (int t = 0; t < 20; t++) sim.Tick();
        Assert.Equal(ColonistActivity.Idle, c.Activity);
        Assert.True(c.Cell.X < 40, $"kept walking to {c.Cell}");
    }

    [Fact] public void BuildOnColonist_Waits()
    {
        var map = TestWorlds.FlatMap();
        var sim = new Simulation(map);
        var idle = Worker(sim, new Int3(30, 151, 32), miningPriority: 0, constructionPriority: 0);
        var builder = Worker(sim, new Int3(36, 151, 32), construction: 5);
        var job = Wall(map, new Int3(30, 151, 32));
        Run(sim, () => builder.Activity == ColonistActivity.Working);
        for (int t = 0; t < 100; t++)
        {
            sim.Tick();
            Assert.Equal(0f, job.Progress);
        }
        Assert.Equal(ColonistActivity.Working, builder.Activity);
        // Instant work skips it too, keeping the job (and doesn't count it as done).
        Assert.Equal(0, InstantWorker.Step(map, 10));
        Assert.True(map.Jobs.Contains(job.Position));
        Assert.Equal(Blocks.Air, map.World.GetBlock(30, 151, 32));

        sim.GoTo(idle, new Int3(26, 151, 32));
        Run(sim, () => map.Jobs.Count == 0);
        Assert.Equal(Blocks.WoodPlanks, map.World.GetBlock(30, 151, 32));
        Assert.NotEqual(new Int3(30, 151, 32), idle.Cell);
    }

    [Fact] public void WorkSpots_AndReadiness()
    {
        var map = TestWorlds.FlatMap();
        var dig = Dig(map, new Int3(10, 150, 10));
        // Around the block on the surface; not on top of it (its own support).
        var spots = WorkRules.WorkSpots(map, dig).ToHashSet();
        Assert.Equal(8, spots.Count);
        Assert.DoesNotContain(new Int3(10, 151, 10), spots);
        Assert.Contains(new Int3(11, 151, 11), spots);

        var below = Dig(map, new Int3(10, 147, 10));
        Assert.False(WorkRules.IsReady(map, below));
        map.Jobs.Remove(dig.Position);
        Assert.True(WorkRules.IsReady(map, below));

        var low = Wall(map, new Int3(20, 151, 20));
        var high = Wall(map, new Int3(21, 155, 21));
        Assert.True(WorkRules.IsReady(map, low));
        Assert.False(WorkRules.IsReady(map, high));
        map.Jobs.Remove(low.Position);
        Assert.True(WorkRules.IsReady(map, high));

        Assert.Equal(100, WorkRules.WorkTicks(dig, Blocks.Registry.Get(Blocks.Stone)));
        Assert.Equal(20, WorkRules.WorkTicks(dig, Blocks.Registry.Get(Blocks.OakLeaves)));
        Assert.Equal(10, WorkRules.WorkTicks(dig, Blocks.Registry.Get(Blocks.Air))); // at least 10
        Assert.Same(WorkTypes.Mining, WorkRules.TypeOf(JobKind.CarveStairs));
        Assert.Same(WorkTypes.Construction, WorkRules.TypeOf(JobKind.BuildLadder));
    }

    [Fact] public void CutsNothing_OnlyWhenThePreviewAgrees()
    {
        // Rough terrain from random edits; then, for random edits, whenever the local check says nothing is cut, the
        // preview must not split any component near the edit.
        var map = TestWorlds.FlatMap();
        var rng = new Random(11);
        ushort[] blocks = [Blocks.Stone, Blocks.WoodPlanks, Blocks.Ladder(Facing.North), Blocks.Stairs(BuildMaterial.Wood, Facing.East),
            Blocks.NaturalSlope(Blocks.Stone, BlockShape.Ramp, Facing.South)];
        Int3 RandomCell() => new(rng.Next(20, 44), rng.Next(144, 156), rng.Next(20, 44));
        void Edit(Int3 c, ushort b)
        {
            if (b == Blocks.Air) { TestWorlds.Dig(map, c.X, c.Y, c.Z); return; }
            map.World.SetBlock(c.X, c.Y, c.Z, b);
            if (!Blocks.Registry.Get(b).IsSolid) map.World.SetDiscovered(c.X, c.Y, c.Z);
        }
        ushort RandomBlock() => rng.Next(3) == 0 ? blocks[rng.Next(blocks.Length)] : Blocks.Air;
        for (int i = 0; i < 1500; i++) Edit(RandomCell(), RandomBlock());

        var nav = map.Nav;
        int proven = 0, checkedEdits = 0;
        for (int i = 0; i < 400; i++)
        {
            nav.Update();
            Int3 p = RandomCell();
            ushort b = RandomBlock();
            var edited = new NavRules<OverlaidBlocks>(new OverlaidBlocks(map.View, p, b));
            if (StrandCheck.CutsNothing(nav, edited, p))
            {
                proven++;
                var after = nav.Preview(p, b);
                var seen = new Dictionary<int, int>();
                for (int y = p.Y - 8; y <= p.Y + 8; y++)
                    for (int z = p.Z - 8; z <= p.Z + 8; z++)
                        for (int x = p.X - 8; x <= p.X + 8; x++)
                        {
                            var c = new Int3(x, y, z);
                            int was = nav.ComponentOf(c), now = after.ComponentOf(c);
                            if (was < 0 || now < 0) continue;
                            if (!seen.TryAdd(was, now)) Assert.True(seen[was] == now, $"edit {b} at {p} split component {was} at {c}");
                        }
            }
            checkedEdits++;
            if (rng.Next(2) == 0) Edit(p, b); // keep the terrain changing
        }
        Assert.True(proven > 50 && proven < checkedEdits, $"proven {proven} of {checkedEdits}");
    }

    /// <summary>The timing test, alone in the Perf collection so no other test competes for the cores.</summary>
    [Collection(nameof(NavigationTests.Perf))]
    public class Perf(ITestOutputHelper output)
    {
        static bool Optimized => typeof(Simulation).Assembly.GetCustomAttribute<DebuggableAttribute>() is not { IsJITOptimizerDisabled: true };

        [Fact, Trait("Category", "Perf")] public void JobSearch_200kJobs_StaysFast()
        {
            var map = TestWorlds.FlatMap(128);
            var sim = new Simulation(map);
            var add = Stopwatch.StartNew();
            for (int y = 131; y <= 150; y++)
                for (int z = 14; z < 114; z++)
                    for (int x = 14; x < 114; x++) map.Jobs.Add(new JobSpec(new Int3(x, y, z), JobKind.Dig, Blocks.Air));
            add.Stop();
            Assert.Equal(200_000, map.Jobs.Count);
            var rng = new Random(5);
            for (int i = 0; i < 100; i++) sim.Spawn(new Int3(rng.Next(14, 114), 151, rng.Next(14, 114)));

            // Every colonist searching in turn, each claiming what it finds (as the worker would).
            var find = new List<double>();
            var found = new List<Job>();
            for (int round = 0; round < 2; round++)
                foreach (var c in map.Colony.Colonists)
                {
                    var sw = Stopwatch.StartNew();
                    bool ok = sim.Finder.TryFind(sim, c, out var job, out _);
                    if (round > 0) find.Add(sw.Elapsed.TotalMilliseconds); // the first round warms up
                    Assert.True(ok);
                    map.Jobs.Claim(job, c.Id);
                    found.Add(job);
                }
            foreach (var j in found) map.Jobs.Release(j);

            var ticks = new List<double>();
            double midMax = 0;
            for (int t = 0; t < 1000; t++)
            {
                var sw = Stopwatch.StartNew();
                sim.Tick();
                ticks.Add(sw.Elapsed.TotalMilliseconds);
                midMax = Math.Max(midMax, sim.MidTickSyncTime.TotalMilliseconds);
                Assert.True(sim.MidTickSyncTime - sim.LastMidTickSync < TimeSpan.FromMilliseconds(Simulation.MidTickSyncBudgetMs), "a sync began past the budget");
            }
            int done = 200_000 - map.Jobs.Count;
            double p99 = ticks.Order().ElementAt((int)(ticks.Count * 0.99));
            output.WriteLine($"200k jobs added in {add.Elapsed.TotalMilliseconds:F0} ms; TryFind mean {find.Average():F3} ms, max {find.Max():F2} ms; "
                + $"Tick mean {ticks.Average():F3} ms, p99 {p99:F2} ms, max {ticks.Max():F2} ms (mid-tick navigation at most {midMax:F2} ms); {done} jobs done in 1000 ticks"
                + (Optimized ? "" : " [Debug build: limits not checked, run with -c Release]"));
            Assert.True(done > 0);
            if (!Optimized) return;
            Assert.True(find.Average() < 1, $"TryFind mean {find.Average():F3} ms");
            Assert.True(ticks.Average() < 4, $"Tick mean {ticks.Average():F3} ms");
            // Not 8: one tick-start sync alone rebuilds a chunk of the pitted surface in 6–8 ms (two at a chunk border),
            // which no mid-tick budget can take out.
            Assert.True(p99 <= 16, $"Tick p99 {p99:F2} ms");
        }

        [Fact, Trait("Category", "Perf")] public void UnreachableJobs_FromOutsideHome_SearchStaysFast()
        {
            var map = TestWorlds.FlatMap(128);
            // One colonist in a sealed pocket; 11,000 ready dig jobs on the surface, none reachable from there.
            foreach (var (x, y) in new[] { (64, 120), (64, 121) }) TestWorlds.Dig(map, x, y, 64);
            for (int z = 14; z < 114; z++)
                for (int x = 9; x < 119; x++) map.Jobs.Add(new JobSpec(new Int3(x, 150, z), JobKind.Dig, Blocks.Air));
            var sim = new Simulation(map);
            var c = sim.Spawn(new Int3(64, 120, 64));
            Assert.True(c.Stuck);

            // Navigation changes before every search (a block toggled far off), so nothing it learned holds for long.
            var find = new List<double>();
            for (int i = 0; i < 300; i++)
            {
                map.World.SetBlock(2, 151, 2, i % 2 == 0 ? Blocks.Stone : Blocks.Air);
                sim.SyncNav();
                var sw = Stopwatch.StartNew();
                Assert.False(sim.Finder.TryFind(sim, c, out _, out _));
                if (i >= 20) find.Add(sw.Elapsed.TotalMilliseconds);
            }
            output.WriteLine($"11k unreachable jobs, outside home, navigation changing: TryFind mean {find.Average():F3} ms, max {find.Max():F2} ms"
                + (Optimized ? "" : " [Debug build: limits not checked, run with -c Release]"));
            if (!Optimized) return;
            Assert.True(find.Average() < 1, $"TryFind mean {find.Average():F3} ms");
        }
    }
}
