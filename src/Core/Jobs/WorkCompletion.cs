namespace ColonySim.Core;

/// <summary>Applies a finished job to the world. Shared by the instant worker and colonists.</summary>
public static class WorkCompletion
{
    /// <summary>
    /// Applies the job's effect and removes it. Returns false if the job no longer made sense against the real world
    /// (it is removed anyway), and also for a build into a cell a colonist's body is in: that one waits, so the job is
    /// kept and nothing is placed.
    /// </summary>
    public static bool Complete(ColonyMap map, Job job)
    {
        var p = job.Position;
        var w = map.World;
        bool valid = true;
        switch (job.Kind)
        {
            case JobKind.Dig:
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

    /// <summary>A colonist's body (its cell and the one above, at either end of a move under way) is in the cell (<see cref="Colony.Bodies"/>).</summary>
    public static bool IsOccupied(ColonyMap map, Int3 cell) => map.Colony.Bodies.IsOccupied(cell);
}
