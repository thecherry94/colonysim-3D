namespace ColonySim.Core;

/// <summary>
/// Each colonist's work, one tick at a time (after the <see cref="ColonistMover"/>):
/// <list type="bullet">
/// <item><b>Choosing:</b> an idle colonist without a job looks for something to do on ticks where (tick + id) % 10 == 0.
/// For now that is work (<see cref="JobFinder"/>); it claims the job and walks to the work spot. Finding none while it
/// is in the way of a job that could be done now (on a block to dig, or in a build's cell), it steps aside, so that job
/// isn't held up by it for good.</item>
/// <item><b>Navigation first:</b> searching and finishing need navigation that includes every edit so far, also those
/// made earlier in the same tick (<see cref="Simulation.TrySyncNav"/>). Once the tick's budget for that is spent, a
/// search is put off to the next tick, and a finished job is held, at full progress, until then.</item>
/// <item><b>Working:</b> at the spot, each tick adds work at its skill's speed and one experience; at
/// <see cref="WorkRules.WorkTicks"/> the job is applied (<see cref="WorkCompletion"/>) and the colonist goes idle. It
/// waits, with no progress, while someone is in the way (<see cref="WorkCompletion.IsInTheWay"/>): in a build's cell,
/// or on a block being dug.</item>
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
        StepAside(c);
    }

    /// <summary>
    /// Out of the way of a ready job (<see cref="InTheWayOfReady"/>), onto a cell one move away (so in its component:
    /// home if it is home) that isn't in the way of one too. One path request at most per search.
    /// </summary>
    void StepAside(Colonist c)
    {
        if (!InTheWayOfReady(c.Cell)) return;
        Span<Move> moves = stackalloc Move[NavRules.MaxMoves];
        int n = sim.Nav.Rules.GetMoves(c.Cell, moves);
        for (int i = 0; i < n; i++)
            if (!InTheWayOfReady(moves[i].To))
            {
                sim.GoTo(c, moves[i].To);
                return;
            }
    }

    /// <summary>A colonist standing at the cell holds up a ready job: it is on a block to dig, or its body is in a build's cell.</summary>
    bool InTheWayOfReady(Int3 cell)
    {
        return Ready(cell + Int3.Down, build: false) || Ready(cell, build: true) || Ready(cell + Int3.Up, build: true);

        bool Ready(Int3 p, bool build) =>
            Jobs.TryGet(p, out var j) && (build ? WorkRules.IsBuild(j.Kind) : j.Kind == JobKind.Dig) && WorkRules.IsReady(sim.Map, j);
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
        if (WorkCompletion.IsInTheWay(map, job)) return; // wait for them to move

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
        // Nobody moved since the check above that nobody is in the way, so the job goes through.
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
