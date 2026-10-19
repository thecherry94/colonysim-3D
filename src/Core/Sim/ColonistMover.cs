namespace ColonySim.Core;

/// <summary>A walk under way: where to, the path toward it and how far along that path the colonist is.</summary>
internal sealed class Walk(Int3 target, PathRequest request)
{
    public Int3 Target { get; } = target;

    /// <summary>The path being followed, pending or answered.</summary>
    public PathRequest Request { get; set; } = request;

    /// <summary>The index in the path of the next move to make.</summary>
    public int Next { get; set; }

    /// <summary>A move of this walk is under way (as opposed to one begun before it).</summary>
    public bool Moving { get; set; }

    /// <summary>New paths asked for after a move turned invalid.</summary>
    public int Repaths { get; set; }
}

/// <summary>
/// Moves colonists through the world, one tick at a time:
/// <list type="bullet">
/// <item>A colonist not mid-motion whose cell isn't standable falls straight down to the first standable cell, 2 ticks per block (stuck if there is none).</item>
/// <item>A walk follows its path move by move, re-validating each move against the world before making it; an invalid one means a new path to the same target, at most <see cref="MaxRepaths"/> times, then it gives up and idles.</item>
/// <item>A fall during a walk asks for a new path from where it lands; that doesn't count as one of the re-paths.</item>
/// <item>How the last walk ended is left in <see cref="Colonist.LastWalk"/>.</item>
/// <item>A motion whose destination turns solid on the way is called off: the colonist stays where it was.</item>
/// </list>
/// </summary>
internal sealed class ColonistMover(Simulation sim)
{
    public const int MaxRepaths = 3;
    const int FallTicksPerBlock = 2;

    NavRules Rules => sim.Nav.Rules;

    public void GoTo(Colonist c, Int3 target)
    {
        Stop(c);
        // Mid-motion, the path starts where the motion ends.
        c.Walk = new Walk(target, sim.Paths.Request(c.Motion?.To ?? c.Cell, target));
        c.LastWalk = WalkResult.None;
        if (c.Activity != ColonistActivity.Falling) c.Activity = ColonistActivity.Walking;
    }

    /// <summary>Drops the colonist's walk, if any, as given up; a motion under way still finishes.</summary>
    public void Stop(Colonist c)
    {
        if (c.Walk is not { } w) return;
        sim.Paths.Cancel(w.Request);
        c.Walk = null;
        c.LastWalk = WalkResult.GaveUp;
        if (c.Activity == ColonistActivity.Walking) c.Activity = ColonistActivity.Idle;
    }

    /// <summary>Moves the colonist one tick, then updates where its body is (<see cref="Colony.Bodies"/>).</summary>
    public void Step(Colonist c)
    {
        Move(c);
        sim.Colony.Bodies.Track(c);
    }

    void Move(Colonist c)
    {
        if (c.Motion is { } m)
        {
            if (!Fits(m.To))
            {
                c.Motion = null; // the way is blocked: stay at From
                if (c.Walk is { } blocked) blocked.Moving = false;
                if (c.Activity == ColonistActivity.Falling) c.Activity = c.Walk != null ? ColonistActivity.Walking : ColonistActivity.Idle;
            }
            else if (m.Elapsed + 1 < m.TotalTicks)
            {
                c.Motion = m with { Elapsed = m.Elapsed + 1 };
                return;
            }
            else Arrive(c, m);
        }

        if (!Rules.IsStandable(c.Cell))
        {
            Fall(c);
            return;
        }
        if (c.Walk is { } w) Walk(c, w);
    }

    void Arrive(Colonist c, Motion m)
    {
        c.Motion = null;
        c.Cell = m.To;
        if (m.Kind == MoveKind.Fall)
        {
            c.Activity = c.Walk != null ? ColonistActivity.Walking : ColonistActivity.Idle;
            c.Stuck = sim.IsStuck(c.Cell); // it may have landed somewhere else entirely
            if (c.Walk is { } fell) NewPath(c, fell); // from where it landed, without using up a re-path
        }
        else if (c.Walk is { Moving: true } w)
        {
            w.Next++;
            w.Moving = false;
        }
    }

    /// <summary>Falls to the first standable cell straight down through open cells; with none, stays put, stuck.</summary>
    void Fall(Colonist c)
    {
        if (sim.Landing(c.Cell, out var below))
        {
            int ticks = FallTicksPerBlock * (c.Cell.Y - below.Y);
            c.Motion = new Motion(c.Cell, below, MoveKind.Fall, ticks, 0);
            c.Activity = ColonistActivity.Falling;
            return;
        }
        c.Stuck = true;
        Stop(c);
    }

    void Walk(Colonist c, Walk w)
    {
        var path = w.Request;
        if (path.Status == PathStatus.Pending) return;
        if (path.Status != PathStatus.Found)
        {
            Stop(c);
            return;
        }
        if (w.Next >= path.Moves.Count)
        {
            if (c.Cell != w.Target) Repath(c, w);
            else
            {
                c.Walk = null;
                c.LastWalk = WalkResult.Arrived;
                c.Activity = ColonistActivity.Idle;
            }
            return;
        }
        if (!Rules.TryGetMove(c.Cell, path.Moves[w.Next].To, out var move))
        {
            Repath(c, w);
            return;
        }
        c.Motion = new Motion(c.Cell, move.To, move.Kind, move.Ticks, 0);
        w.Moving = true;
    }

    void Repath(Colonist c, Walk w)
    {
        if (w.Repaths >= MaxRepaths)
        {
            Stop(c);
            return;
        }
        w.Repaths++;
        NewPath(c, w);
    }

    void NewPath(Colonist c, Walk w)
    {
        sim.Paths.Cancel(w.Request);
        w.Request = sim.Paths.Request(c.Cell, w.Target);
        w.Next = 0;
        w.Moving = false;
    }

    /// <summary>A 2-tall colonist fits with its feet at <paramref name="cell"/>.</summary>
    bool Fits(Int3 cell) => Rules.IsPassable(cell) && Rules.IsPassable(cell + Int3.Up);
}
