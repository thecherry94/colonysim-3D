namespace ColonySim.Core;

/// <summary>
/// Each colonist's work, one tick at a time (after the <see cref="ColonistMover"/>):
/// <list type="bullet">
/// <item><b>Choosing:</b> an idle colonist without a job looks for something to do on ticks where (tick + id) % 10 == 0.
/// For now that is work (<see cref="JobFinder"/>); it claims the job and walks to the work spot.</item>
/// <item><b>Navigation first:</b> searching and finishing need navigation that includes every edit so far, also those
/// made earlier in the same tick (<see cref="Simulation.TrySyncNav"/>). Once the tick's budget for that is spent, a
/// search is put off to the next tick, and a finished job is held, at full progress, until then.</item>
/// <item><b>Working:</b> at the spot, each tick adds work at its skill's speed and one experience; at
/// <see cref="WorkRules.WorkTicks"/> the job is applied (<see cref="WorkCompletion"/>) and the colonist goes idle. A
/// build waits, with no progress, while a colonist's body is in the cell.</item>
/// <item><b>Before finishing,</b> the strand check runs again if navigation changed since it passed, and always for a
/// build (others may have walked in behind the wall meanwhile); failing, the job is marked
/// <see cref="JobStatus.NeedsWayBack"/> as the <see cref="JobFinder"/> marks it, and dropped.</item>
/// <item><b>Abandoning:</b> if the job goes away (cancelled, done by instant work, replaced), the walk gives up or the
/// colonist ends up off its spot (a fall), the claim is released (progress is kept) and it goes idle. A walk that gave
/// up marks the job <see cref="JobStatus.Unreachable"/>, if navigation hasn't changed since the job was chosen.</item>
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
        else if (c.Activity == ColonistActivity.Idle && c.Motion == null
            && (c.SearchPending || (sim.Colony.Tick + c.Id) % SearchInterval == 0)) Choose(c);
    }

    /// <summary>What an idle colonist does next. Step 3's needs (eating, sleeping) go here, ahead of work.</summary>
    void Choose(Colonist c)
    {
        c.SearchPending = !sim.TrySyncNav();
        if (c.SearchPending) return;
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
        if (c.LastWalk == WalkResult.GaveUp)
        {
            // Only when navigation is as it was when the job was chosen: an edit since (say, another colonist digging
            // out the spot's floor) is a likelier reason, and the job may well have other spots now.
            if (sim.Nav.Version == c.CheckedAt) Jobs.SetStatus(job, JobStatus.Unreachable, sim.Nav.Version);
            Abandon(c, job);
            return;
        }
        if (c.Cell != c.WorkSpot)
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

        int ticks = WorkRules.WorkTicks(job, WorkRules.Target(map, job));
        if (job.Progress < ticks)
        {
            var skill = WorkRules.TypeOf(job.Kind).Skill is { } s ? c.Skills[(int)s] : null;
            Jobs.AddProgress(job, skill != null ? Skills.SpeedFactor(skill.Level) : 1f);
            if (skill != null) Skills.AddXp(skill, 1);
            if (job.Progress < ticks) return;
        }

        if (!sim.TrySyncNav()) return; // held, done, until navigation catches up next tick
        if (sim.Nav.Version != c.CheckedAt || WorkRules.IsBuild(job.Kind))
        {
            var edit = StrandCheck.Edit.Of(sim, job);
            bool wayBack = edit.LeavesWayBack(c, c.WorkSpot);
            if (!wayBack || edit.SealsOthers(c))
            {
                if (wayBack) Jobs.SetStatus(job, JobStatus.NeedsWayBack, 0, sim.Colony.Tick);
                else Jobs.SetStatus(job, JobStatus.NeedsWayBack, sim.Nav.Version);
                Abandon(c, job);
                return;
            }
            c.CheckedAt = sim.Nav.Version;
        }
        // Nobody moved since the occupancy check above, so the build goes in.
        WorkCompletion.Complete(map, job);
        Done(c);
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
