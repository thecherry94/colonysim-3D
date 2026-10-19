namespace ColonySim.Core;

/// <summary>
/// Each colonist's work, one tick at a time (after the <see cref="ColonistMover"/>):
/// <list type="bullet">
/// <item><b>Choosing:</b> an idle colonist without a job looks for something to do on ticks where (tick + id) % 10 == 0.
/// For now that is work (<see cref="JobFinder"/>); it claims the job and walks to the work spot.</item>
/// <item><b>Working:</b> at the spot, each tick adds work at its skill's speed and one experience; at
/// <see cref="WorkRules.WorkTicks"/> the job is applied (<see cref="WorkCompletion"/>) and the colonist goes idle. A
/// build waits, with no progress, while a colonist's body is in the cell.</item>
/// <item><b>Before finishing,</b> the strand check runs again if navigation changed since it passed, and always for a
/// build (others may have walked in behind the wall meanwhile); failing, the job is marked
/// <see cref="JobStatus.NeedsWayBack"/> as the <see cref="JobFinder"/> marks it, and dropped.</item>
/// <item><b>Abandoning:</b> if the job goes away (cancelled, done by instant work, replaced), the walk gives up or the
/// colonist ends up off its spot (a fall), the claim is released (progress is kept) and it goes idle.</item>
/// </list>
/// Jobs nobody has claimed cost nothing per tick.
/// </summary>
internal sealed class ColonistWorker(Simulation sim)
{
    /// <summary>Idle colonists look for something to do once every this many ticks, staggered by id.</summary>
    public const int SearchInterval = 10;

    JobQueue Jobs => sim.Map.Jobs;

    public void Step(Colonist c)
    {
        if (c.JobId != 0) Continue(c);
        else if (c.Activity == ColonistActivity.Idle && c.Motion == null && (sim.Colony.Tick + c.Id) % SearchInterval == 0) Choose(c);
    }

    /// <summary>What an idle colonist does next. Step 3's needs (eating, sleeping) go here, ahead of work.</summary>
    void Choose(Colonist c)
    {
        if (TryStartWork(c)) return;
    }

    bool TryStartWork(Colonist c)
    {
        if (!sim.Finder.TryFind(sim, c, out var job, out var spot)) return false;
        Jobs.Claim(job, c.Id);
        c.JobId = job.Id;
        c.WorkSpot = spot;
        c.CheckedAt = sim.Nav.Version;
        sim.GoTo(c, spot); // at the spot already, it arrives on the next step
        return true;
    }

    void Continue(Colonist c)
    {
        if (!Jobs.TryGetById(c.JobId, out var job) || job.ClaimedBy != c.Id)
        {
            Abandon(c, null);
            return;
        }
        if (c.Motion != null || c.Activity == ColonistActivity.Falling || c.Walk != null) return; // on the way
        if (c.LastWalk == WalkResult.GaveUp || c.Cell != c.WorkSpot)
        {
            Abandon(c, job);
            return;
        }
        c.Activity = ColonistActivity.Working;
        Work(c, job);
    }

    void Work(Colonist c, Job job)
    {
        var map = sim.Map;
        if (!WorkRules.NeedsWork(map, job))
        {
            WorkRules.DropMoot(map, job);
            Done(c);
            return;
        }
        if (WorkRules.IsBuild(job.Kind) && WorkCompletion.IsOccupied(map, job.Position)) return; // wait for them to move

        var skill = WorkRules.TypeOf(job.Kind).Skill is { } s ? c.Skills[(int)s] : null;
        Jobs.AddProgress(job, skill != null ? Skills.SpeedFactor(skill.Level) : 1f);
        if (skill != null) Skills.AddXp(skill, 1);
        if (job.Progress < WorkRules.WorkTicks(job, WorkRules.Target(map, job))) return;

        sim.SyncNav();
        if (sim.Nav.Version != c.CheckedAt || WorkRules.IsBuild(job.Kind))
        {
            var edit = StrandCheck.Edit.Of(sim, job);
            bool wayBack = edit.LeavesWayBack(c, c.WorkSpot);
            if (!wayBack || edit.SealsOthers(c))
            {
                Jobs.SetStatus(job, JobStatus.NeedsWayBack, wayBack ? 0 : sim.Nav.Version);
                Abandon(c, job);
                return;
            }
            c.CheckedAt = sim.Nav.Version;
        }
        WorkCompletion.Complete(map, job);
        if (!Jobs.TryGetById(job.Id, out _)) Done(c); // else kept: someone stepped into the cell, so wait on
    }

    /// <summary>Drops the job (releasing the claim if it still holds it) and the walk to it, and goes idle.</summary>
    void Abandon(Colonist c, Job? job)
    {
        if (job != null && job.ClaimedBy == c.Id) Jobs.Release(job);
        sim.Stop(c);
        Done(c);
    }

    static void Done(Colonist c)
    {
        c.JobId = 0;
        if (c.Activity is ColonistActivity.Working or ColonistActivity.Walking) c.Activity = ColonistActivity.Idle;
    }
}
