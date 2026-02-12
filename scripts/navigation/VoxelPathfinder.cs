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

    // 4 horizontal directions (no diagonals)
    private static readonly Vector3I[] HorizontalDirs = {
        new(1, 0, 0), new(-1, 0, 0), new(0, 0, 1), new(0, 0, -1)
    };

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
        foreach (var dir in HorizontalDirs)
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
        // Manhattan distance with Y weighted 1.5x
        return Mathf.Abs(a.X - b.X) + Mathf.Abs(a.Z - b.Z) + Mathf.Abs(a.Y - b.Y) * 1.5f;
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
