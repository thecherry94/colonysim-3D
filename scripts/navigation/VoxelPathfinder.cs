namespace ColonySim;

using System.Collections.Generic;
using Godot;

/// <summary>
/// A* pathfinding on the voxel grid.
/// 4-connected neighbors, flat/step-up/step-down, 2-high clearance checks.
/// </summary>
public class VoxelPathfinder
{
    private readonly World _world;
    private const int MaxNodes = 10000;
    private const float CostFlat = 1.0f;
    private const float CostStepDown = 1.2f;
    private const float CostStepUp = 2.0f;
    private const float CostDiagonal = 1.414f;        // sqrt(2)
    private const float CostDiagonalDown = 1.614f;     // sqrt(2) + 0.2

    // 4 cardinal directions
    private static readonly Vector3I[] CardinalDirs = {
        new(1, 0, 0), new(-1, 0, 0), new(0, 0, 1), new(0, 0, -1)
    };

    // 4 diagonal directions
    private static readonly Vector3I[] DiagonalDirs = {
        new(1, 0, 1), new(1, 0, -1), new(-1, 0, 1), new(-1, 0, -1)
    };

    /// <summary>
    /// Toggle diagonal movement. When false, uses 4-connected cardinal only.
    /// </summary>
    public bool AllowDiagonals { get; set; } = true;

    public VoxelPathfinder(World world)
    {
        _world = world;
    }

    /// <summary>
    /// Convert a world position (e.g., colonist position) to the VoxelNode
    /// of the solid block the entity is standing on.
    /// Scans downward from the position to find the first solid block.
    /// </summary>
    public VoxelNode? WorldPosToVoxelNode(Vector3 worldPos)
    {
        int x = Mathf.FloorToInt(worldPos.X);
        int z = Mathf.FloorToInt(worldPos.Z);
        int startY = Mathf.FloorToInt(worldPos.Y);

        // Scan downward to find solid ground
        for (int y = startY; y >= 0; y--)
        {
            if (BlockData.IsSolid(_world.GetBlock(new Vector3I(x, y, z))))
            {
                // Verify 2-high clearance above
                if (HasClearance(x, y, z))
                    return new VoxelNode(x, y, z);
            }
        }
        return null;
    }

    public PathResult FindPath(VoxelNode start, VoxelNode goal)
    {
        var openSet = new PriorityQueue<VoxelNode, float>();
        var cameFrom = new Dictionary<VoxelNode, VoxelNode>();
        var gScore = new Dictionary<VoxelNode, float>();

        gScore[start] = 0;
        openSet.Enqueue(start, Heuristic(start, goal));

        int nodesExplored = 0;

        while (openSet.Count > 0 && nodesExplored < MaxNodes)
        {
            var current = openSet.Dequeue();
            nodesExplored++;

            if (current.Equals(goal))
            {
                var path = ReconstructPath(cameFrom, current);
                GD.Print($"Path found: {start} -> {goal}, {path.Count} waypoints, {nodesExplored} nodes explored");
                return new PathResult(true, path, nodesExplored);
            }

            foreach (var (neighbor, cost) in GetNeighbors(current))
            {
                float tentativeG = gScore[current] + cost;
                if (!gScore.ContainsKey(neighbor) || tentativeG < gScore[neighbor])
                {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeG;
                    float f = tentativeG + Heuristic(neighbor, goal);
                    openSet.Enqueue(neighbor, f);
                }
            }
        }

        GD.Print($"No path from {start} to {goal} — explored {nodesExplored} nodes");
        return new PathResult(false, new List<VoxelNode>(), nodesExplored);
    }

    private IEnumerable<(VoxelNode node, float cost)> GetNeighbors(VoxelNode current)
    {
        // Cardinal neighbors (N/S/E/W)
        foreach (var dir in CardinalDirs)
        {
            int nx = current.X + dir.X;
            int nz = current.Z + dir.Z;

            // Flat walk: same Y, solid ground at (nx, current.Y, nz), 2-high clearance above
            if (IsWalkable(nx, current.Y, nz))
            {
                yield return (new VoxelNode(nx, current.Y, nz), CostFlat);
                continue;
            }

            // Step up: solid ground at (nx, current.Y+1, nz), 2-high clearance above,
            // AND clearance at current position Y+2 (need head room to step up)
            if (IsWalkable(nx, current.Y + 1, nz) && !BlockData.IsSolid(_world.GetBlock(new Vector3I(current.X, current.Y + 2, current.Z))))
            {
                yield return (new VoxelNode(nx, current.Y + 1, nz), CostStepUp);
                continue;
            }

            // Step down: solid ground at (nx, current.Y-1, nz), 2-high clearance above
            if (current.Y > 0 && IsWalkable(nx, current.Y - 1, nz))
            {
                yield return (new VoxelNode(nx, current.Y - 1, nz), CostStepDown);
            }
        }

        // Diagonal neighbors (NE/NW/SE/SW)
        if (!AllowDiagonals) yield break;

        foreach (var dir in DiagonalDirs)
        {
            int dx = dir.X;
            int dz = dir.Z;
            int nx = current.X + dx;
            int nz = current.Z + dz;

            // CORNER SAFETY: both adjacent cardinal blocks must have air at body height.
            // Without this, the capsule (radius 0.3) clips through block corners.
            if (!HasCornerClearance(current.X, current.Y, current.Z, dx, dz))
                continue;

            // Flat diagonal
            if (IsWalkable(nx, current.Y, nz))
            {
                yield return (new VoxelNode(nx, current.Y, nz), CostDiagonal);
                continue;
            }

            // Diagonal step-down only (no diagonal step-up — jump physics don't support it)
            if (current.Y > 0 && IsWalkable(nx, current.Y - 1, nz))
            {
                yield return (new VoxelNode(nx, current.Y - 1, nz), CostDiagonalDown);
            }
        }
    }

    /// <summary>
    /// Check that both cardinal neighbors adjacent to a diagonal move have
    /// 2-high air clearance. Prevents capsule corner-clipping.
    /// From (x, y, z) moving diagonally by (dx, dz):
    ///   - Block at (x+dx, y+1, z) and (x+dx, y+2, z) must be Air
    ///   - Block at (x, y+1, z+dz) and (x, y+2, z+dz) must be Air
    /// </summary>
    private bool HasCornerClearance(int x, int y, int z, int dx, int dz)
    {
        var side1Low = _world.GetBlock(new Vector3I(x + dx, y + 1, z));
        var side1High = _world.GetBlock(new Vector3I(x + dx, y + 2, z));
        var side2Low = _world.GetBlock(new Vector3I(x, y + 1, z + dz));
        var side2High = _world.GetBlock(new Vector3I(x, y + 2, z + dz));

        return side1Low == BlockType.Air && side1High == BlockType.Air
            && side2Low == BlockType.Air && side2High == BlockType.Air;
    }

    /// <summary>
    /// A block is walkable if it's solid and has 2 air blocks above it.
    /// </summary>
    private bool IsWalkable(int x, int y, int z)
    {
        return BlockData.IsSolid(_world.GetBlock(new Vector3I(x, y, z))) && HasClearance(x, y, z);
    }

    /// <summary>
    /// Check 2-high clearance above a solid block at (x, y, z).
    /// Colonist is ~2 blocks tall, needs air at y+1 and y+2.
    /// Also blocks on water — colonists can't walk through water.
    /// </summary>
    private bool HasClearance(int x, int y, int z)
    {
        var above1 = _world.GetBlock(new Vector3I(x, y + 1, z));
        var above2 = _world.GetBlock(new Vector3I(x, y + 2, z));
        return above1 == BlockType.Air && above2 == BlockType.Air;
    }

    private float Heuristic(VoxelNode a, VoxelNode b)
    {
        float dx = Mathf.Abs(a.X - b.X);
        float dz = Mathf.Abs(a.Z - b.Z);
        float dy = Mathf.Abs(a.Y - b.Y);

        if (AllowDiagonals)
        {
            // Octile distance: diagonals cost sqrt(2), cardinals cost 1
            return Mathf.Max(dx, dz) + 0.414f * Mathf.Min(dx, dz) + dy * 1.5f;
        }
        else
        {
            // Manhattan distance for 4-connected
            return dx + dz + dy * 1.5f;
        }
    }

    private List<VoxelNode> ReconstructPath(Dictionary<VoxelNode, VoxelNode> cameFrom, VoxelNode current)
    {
        var path = new List<VoxelNode> { current };
        while (cameFrom.ContainsKey(current))
        {
            current = cameFrom[current];
            path.Add(current);
        }
        path.Reverse();
        return path;
    }
}
