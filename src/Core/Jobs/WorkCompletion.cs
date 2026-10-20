namespace ColonySim.Core;

/// <summary>Applies a finished job to the world. Shared by the instant worker and colonists.</summary>
public static class WorkCompletion
{
    /// <summary>
    /// Applies the job's effect and removes it. Returns false if the job no longer made sense against the real world
    /// (it is removed anyway), and also when a colonist is in the way (<see cref="IsInTheWay"/>): that one waits, so
    /// the job is kept and nothing changes.
    /// </summary>
    public static bool Complete(ColonyMap map, Job job)
    {
        var p = job.Position;
        var w = map.World;
        bool valid = true;
        switch (job.Kind)
        {
            case JobKind.Dig:
                if (IsInTheWay(map, job)) return false;
                w.SetBlock(p.X, p.Y, p.Z, Blocks.Air);
                // Newly discovered cells with a dig/carve job turned out to be open already.
                Discovery.RevealFrom(w, p.X, p.Y, p.Z, cell =>
                {
                    if (map.Jobs.TryGet(cell, out var other) && other.Kind is JobKind.Dig or JobKind.CarveStairs)
                        map.Jobs.Remove(cell);
                });
                break;
            case JobKind.CarveStairs:
                if (Blocks.Registry.Get(w.GetBlock(p.X, p.Y, p.Z)).IsSolid) w.SetBlock(p.X, p.Y, p.Z, job.Block);
                else valid = false;
                break;
            default:
                ushort now = w.GetBlock(p.X, p.Y, p.Z);
                if (now != Blocks.Air && now != Blocks.Water) valid = false;
                else if (IsOccupied(map, p)) return false;
                else w.SetBlock(p.X, p.Y, p.Z, job.Block);
                break;
        }
        map.Jobs.Remove(p);
        return valid;
    }

    /// <summary>
    /// A colonist is in the job's way, so it must wait: a build waits while a body is in its cell; a dig while someone
    /// stands on the block, or is stepping onto it (taking it would drop them, maybe into a cave the colony doesn't know
    /// of). A carve never waits: the stair it leaves still holds them up.
    /// </summary>
    public static bool IsInTheWay(ColonyMap map, Job job)
    {
        var p = job.Position;
        return job.Kind switch
        {
            JobKind.Dig => map.World.GetBlock(p.X, p.Y, p.Z) != Blocks.Air && IsOccupied(map, p + Int3.Up),
            JobKind.CarveStairs => false,
            _ => IsOccupied(map, p),
        };
    }

    /// <summary>A colonist's body (its cell and the one above, at either end of a move under way) is in the cell (<see cref="Colony.Bodies"/>).</summary>
    public static bool IsOccupied(ColonyMap map, Int3 cell) => map.Colony.Bodies.IsOccupied(cell);
}
