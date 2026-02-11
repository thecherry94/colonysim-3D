namespace ColonySim;

using System.Collections.Generic;
using Godot;

/// <summary>
/// A walkable position on the voxel grid.
/// X, Y, Z are the world block coordinates of the solid block the colonist stands ON.
/// </summary>
public readonly struct VoxelNode
{
    public readonly int X, Y, Z;

    public VoxelNode(int x, int y, int z)
    {
        X = x; Y = y; Z = z;
    }

    /// <summary>
    /// World position where the colonist's feet should be.
    /// Block center on X/Z, top of the block on Y.
    /// </summary>
    public Vector3 StandPosition => new(X + 0.5f, Y + 1.0f, Z + 0.5f);

    public override int GetHashCode() => X * 73856093 ^ Y * 19349663 ^ Z * 83492791;
    public override bool Equals(object obj) => obj is VoxelNode other && X == other.X && Y == other.Y && Z == other.Z;
    public override string ToString() => $"({X},{Y},{Z})";
}

/// <summary>
/// Result of a pathfinding request.
/// </summary>
public class PathResult
{
    public bool Success { get; }
    public List<VoxelNode> Waypoints { get; }
    public int NodesExplored { get; }

    public PathResult(bool success, List<VoxelNode> waypoints, int nodesExplored)
    {
        Success = success;
        Waypoints = waypoints;
        NodesExplored = nodesExplored;
    }
}
