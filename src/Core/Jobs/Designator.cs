namespace ColonySim.Core;

/// <summary>Turns a drag box and a tool into jobs. All checks use the fogged player view.</summary>
public static class Designator
{
    public const int MaxCellsPerDrag = 200_000;

    public static IReadOnlyList<JobSpec> Preview(ColonyMap map, DesignationTool tool, BuildMaterial material, DragBox box)
    {
        var w = map.World;
        int x0 = Math.Max(0, Math.Min(box.Start.X, box.End.X)), x1 = Math.Min(w.SizeX - 1, Math.Max(box.Start.X, box.End.X));
        int y0 = Math.Max(1, Math.Min(box.Start.Y, box.End.Y)), y1 = Math.Min(255, Math.Max(box.Start.Y, box.End.Y));
        int z0 = Math.Max(0, Math.Min(box.Start.Z, box.End.Z)), z1 = Math.Min(w.SizeZ - 1, Math.Max(box.Start.Z, box.End.Z));
        var result = new List<JobSpec>();

        if (tool == DesignationTool.DigStairs) { CollectDigStairs(map, box, result); return result; }
        if (x0 > x1 || y0 > y1 || z0 > z1) return result;
        if (tool == DesignationTool.Stairs) { CollectStairs(map, box, JobKind.BuildStairs, f => Blocks.Stairs(material, f), result); return result; }
        if (tool == DesignationTool.Ramp) { CollectStairs(map, box, JobKind.BuildRamp, f => Blocks.BuiltRamp(material, f), result); return result; }
        if (tool == DesignationTool.Ladder) { CollectLadders(map, (x0, x1, y0, y1, z0, z1), result); return result; }

        // Outline of the unclipped box, so clipping never invents a wall along the map edge.
        int ox0 = Math.Min(box.Start.X, box.End.X), ox1 = Math.Max(box.Start.X, box.End.X);
        int oz0 = Math.Min(box.Start.Z, box.End.Z), oz1 = Math.Max(box.Start.Z, box.End.Z);
        ushort build = Blocks.BuildBlock(material);

        if (tool == DesignationTool.Wall)
        {
            CollectWalls(map, build, (x0, x1, y0, y1, z0, z1), (ox0, ox1, oz0, oz1), result);
            return result;
        }

        if (tool == DesignationTool.Cancel)
        {
            long volume = (long)(x1 - x0 + 1) * (y1 - y0 + 1) * (z1 - z0 + 1);
            if (map.Jobs.Count < volume)
            {
                foreach (var j in map.Jobs.All)
                {
                    if (result.Count >= MaxCellsPerDrag) break;
                    var q = j.Position;
                    if (q.X >= x0 && q.X <= x1 && q.Y >= y0 && q.Y <= y1 && q.Z >= z0 && q.Z <= z1) result.Add(j.Spec);
                }
                return result;
            }
        }

        for (int y = y0; y <= y1; y++)
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    if (result.Count >= MaxCellsPerDrag) return result;
                    var p = new Int3(x, y, z);
                    switch (tool)
                    {
                        case DesignationTool.Dig:
                            if (Blocks.Registry.Get(map.View.GetVisibleBlock(x, y, z)).IsDiggable)
                                result.Add(new JobSpec(p, JobKind.Dig, Blocks.Air));
                            break;
                        case DesignationTool.Floor:
                            if (IsEmpty(map, p)) result.Add(new JobSpec(p, JobKind.BuildFloor, build));
                            break;
                        case DesignationTool.Cancel:
                            if (map.Jobs.TryGet(p, out var existing)) result.Add(existing.Spec);
                            break;
                    }
                }
        return result;
    }

    public static int Apply(ColonyMap map, DesignationTool tool, BuildMaterial material, DragBox box)
    {
        var jobs = Preview(map, tool, material, box);
        foreach (var j in jobs)
        {
            if (tool == DesignationTool.Cancel) map.Jobs.Remove(j.Position);
            else map.Jobs.Add(j);
        }
        return jobs.Count;
    }

    static void CollectWalls(ColonyMap map, ushort build,
        (int x0, int x1, int y0, int y1, int z0, int z1) c, (int x0, int x1, int z0, int z1) o, List<JobSpec> result)
    {
        for (int y = c.y0; y <= c.y1; y++)
        {
            for (int zi = 0; zi < 2; zi++)
            {
                int z = zi == 0 ? o.z0 : o.z1;
                if ((zi == 1 && o.z1 == o.z0) || z < c.z0 || z > c.z1) continue;
                for (int x = c.x0; x <= c.x1; x++) if (!TryWall(map, build, new Int3(x, y, z), result)) return;
            }
            for (int xi = 0; xi < 2; xi++)
            {
                int x = xi == 0 ? o.x0 : o.x1;
                if ((xi == 1 && o.x1 == o.x0) || x < c.x0 || x > c.x1) continue;
                for (int z = c.z0; z <= c.z1; z++)
                {
                    if (z == o.z0 || z == o.z1) continue;
                    if (!TryWall(map, build, new Int3(x, y, z), result)) return;
                }
            }
        }
    }

    /// <summary>Adds a wall job if the cell is empty; returns false once the cap is reached.</summary>
    static bool TryWall(ColonyMap map, ushort build, Int3 p, List<JobSpec> result)
    {
        if (result.Count >= MaxCellsPerDrag) return false;
        if (IsEmpty(map, p)) result.Add(new JobSpec(p, JobKind.BuildWall, build));
        return true;
    }

    static bool IsEmpty(ColonyMap map, Int3 p) => map.View.GetVisibleBlock(p.X, p.Y, p.Z) == Blocks.Air;

    /// <summary>Direction, run length and run width of a drag; single cells face South.</summary>
    static (Facing Facing, int Length, int Width, Int3 Across) DragShape(DragBox box)
    {
        int dx = box.End.X - box.Start.X, dz = box.End.Z - box.Start.Z;
        bool alongX = Math.Abs(dx) >= Math.Abs(dz) && dx != 0;
        Facing facing = alongX ? (dx > 0 ? Facing.East : Facing.West) : dz < 0 ? Facing.North : Facing.South;
        int length = (alongX ? Math.Abs(dx) : Math.Abs(dz)) + 1;
        int width = (alongX ? Math.Abs(dz) : Math.Abs(dx)) + 1;
        // Across-direction runs from Start towards End on the other axis (or +, if it has no extent).
        var across = alongX ? new Int3(0, 0, Math.Sign(dz)) : new Int3(Math.Sign(dx), 0, 0);
        return (facing, length, width, across);
    }

    /// <summary>Stairs and ramps: runs of cells that climb one level per step along the drag.</summary>
    static void CollectStairs(ColonyMap map, DragBox box, JobKind kind, Func<Facing, ushort> blockFor, List<JobSpec> result)
    {
        var (facing, length, width, across) = DragShape(box);
        var dir = facing.Offset();
        ushort block = blockFor(facing);

        for (int i = 0; i < length; i++)
            for (int k = 0; k < width; k++)
            {
                if (result.Count >= MaxCellsPerDrag) return;
                var p = box.Start + dir * i + across * k + new Int3(0, i, 0);
                if (map.World.InBounds(p.X, p.Y, p.Z) && p.Y >= 1 && IsEmpty(map, p))
                    result.Add(new JobSpec(p, kind, block));
            }
    }

    /// <summary>A ladder on every known-empty cell, mounted against the first known-solid side (N, E, S, W; else South).</summary>
    static void CollectLadders(ColonyMap map, (int x0, int x1, int y0, int y1, int z0, int z1) c, List<JobSpec> result)
    {
        for (int y = c.y0; y <= c.y1; y++)
            for (int z = c.z0; z <= c.z1; z++)
                for (int x = c.x0; x <= c.x1; x++)
                {
                    if (result.Count >= MaxCellsPerDrag) return;
                    var p = new Int3(x, y, z);
                    if (!IsEmpty(map, p)) continue;
                    var facing = Facing.South;
                    for (int f = 0; f < 4; f++)
                    {
                        var n = p + ((Facing)f).Offset();
                        if (Blocks.Registry.Get(map.View.GetVisibleBlock(n.X, n.Y, n.Z)).IsSolid) { facing = (Facing)f; break; }
                    }
                    result.Add(new JobSpec(p, JobKind.BuildLadder, Blocks.Ladder(facing)));
                }
    }

    /// <summary>
    /// Stairs carved down into solid ground from the clicked block, descending along the drag. Each step also
    /// digs out the two solid cells above it for headroom. The box's Y extent is ignored.
    /// </summary>
    static void CollectDigStairs(ColonyMap map, DragBox box, List<JobSpec> result)
    {
        var (facing, length, width, across) = DragShape(box);
        var dir = facing.Offset();
        var w = map.World;
        for (int i = 0; i < length; i++)
            for (int k = 0; k < width; k++)
            {
                var p = box.Start + dir * i + across * k - new Int3(0, i, 0);
                if (p.Y < 1 || !w.InBounds(p.X, p.Y, p.Z)) continue;
                if (result.Count >= MaxCellsPerDrag) return;
                ushort known = map.View.GetVisibleBlock(p.X, p.Y, p.Z);
                if (IsSolidDiggable(known))
                    result.Add(new JobSpec(p, JobKind.CarveStairs, Blocks.CarvedStairs(known, facing.Opposite())));
                for (int h = 1; h <= 2; h++)
                {
                    var q = p + new Int3(0, h, 0);
                    if (q.Y > 255 || !IsSolidDiggable(map.View.GetVisibleBlock(q.X, q.Y, q.Z))) continue;
                    result.Add(new JobSpec(q, JobKind.Dig, Blocks.Air));
                }
            }
    }

    static bool IsSolidDiggable(ushort block)
    {
        var def = Blocks.Registry.Get(block);
        return def.IsSolid && def.IsDiggable;
    }
}
