using Godot;
using System.Collections.Generic;

namespace ColonySim.World;

/// <summary>
/// Generates NavigationMesh for chunks based on walkable block surfaces.
/// Walkable = solid block with air above (top face is exposed).
/// Also detects height transitions for NavigationLinks.
/// </summary>
public static class ChunkNavigation
{
    /// <summary>
    /// Represents a height transition link between two walkable surfaces.
    /// </summary>
    public struct HeightLink
    {
        public Vector3 LowerPosition;
        public Vector3 UpperPosition;
    }
    /// <summary>
    /// Generates a NavigationMesh for a chunk.
    /// Creates quad polygons on top of every solid block that has air above it.
    /// </summary>
    /// <param name="chunk">The chunk to generate navigation for</param>
    /// <returns>NavigationMesh with walkable surface polygons</returns>
    public static NavigationMesh GenerateNavigationMesh(Chunk chunk)
    {
        var navMesh = new NavigationMesh();
        var vertices = new List<Vector3>();
        var polygons = new List<int[]>();

        for (int x = 0; x < Chunk.SIZE; x++)
        {
            for (int z = 0; z < Chunk.SIZE; z++)
            {
                for (int y = 0; y < Chunk.SIZE; y++)
                {
                    BlockType block = chunk.GetBlock(x, y, z);
                    BlockType blockAbove = chunk.GetBlock(x, y + 1, z);

                    // Walkable = solid block with air above
                    if (BlockData.IsSolid(block) && !BlockData.IsSolid(blockAbove))
                    {
                        int baseIndex = vertices.Count;
                        float topY = y + 1;

                        // 4 vertices of top face (clockwise when viewed from above)
                        vertices.Add(new Vector3(x, topY, z));
                        vertices.Add(new Vector3(x + 1, topY, z));
                        vertices.Add(new Vector3(x + 1, topY, z + 1));
                        vertices.Add(new Vector3(x, topY, z + 1));

                        // Add polygon (indices into vertices array)
                        polygons.Add(new int[] { baseIndex, baseIndex + 1, baseIndex + 2, baseIndex + 3 });
                    }
                }
            }
        }

        // Set vertices first, then add polygons
        navMesh.Vertices = vertices.ToArray();

        foreach (var polygon in polygons)
        {
            navMesh.AddPolygon(polygon);
        }

        return navMesh;
    }

    /// <summary>
    /// Finds positions where NavigationLinks are needed (1-block height transitions).
    /// Returns links in local chunk coordinates.
    /// </summary>
    public static List<HeightLink> FindHeightLinks(Chunk chunk)
    {
        var links = new List<HeightLink>();

        for (int x = 0; x < Chunk.SIZE; x++)
        {
            for (int z = 0; z < Chunk.SIZE; z++)
            {
                for (int y = 0; y < Chunk.SIZE - 1; y++)
                {
                    BlockType block = chunk.GetBlock(x, y, z);
                    BlockType blockAbove = chunk.GetBlock(x, y + 1, z);

                    // Check if this is a walkable surface
                    if (!BlockData.IsSolid(block) || BlockData.IsSolid(blockAbove))
                        continue;

                    // Check all 4 horizontal neighbors for height transitions
                    CheckNeighborLink(chunk, x, y, z, x + 1, z, links);  // +X
                    CheckNeighborLink(chunk, x, y, z, x - 1, z, links);  // -X
                    CheckNeighborLink(chunk, x, y, z, x, z + 1, links);  // +Z
                    CheckNeighborLink(chunk, x, y, z, x, z - 1, links);  // -Z
                }
            }
        }

        return links;
    }

    private static void CheckNeighborLink(Chunk chunk, int x, int y, int z, int nx, int nz, List<HeightLink> links)
    {
        // Skip out of bounds (cross-chunk links would need World-level handling)
        if (nx < 0 || nx >= Chunk.SIZE || nz < 0 || nz >= Chunk.SIZE)
            return;

        // Check if neighbor at y+1 is walkable (1 block higher)
        int ny = y + 1;
        if (ny >= Chunk.SIZE)
            return;

        BlockType neighborBlock = chunk.GetBlock(nx, ny, nz);
        BlockType neighborAbove = chunk.GetBlock(nx, ny + 1, nz);

        // Neighbor must be solid with air above (walkable at y+1)
        if (BlockData.IsSolid(neighborBlock) && !BlockData.IsSolid(neighborAbove))
        {
            // Found height transition: walkable at (x, y+1, z) connects to walkable at (nx, ny+1, nz)
            links.Add(new HeightLink
            {
                LowerPosition = new Vector3(x + 0.5f, y + 1, z + 0.5f),
                UpperPosition = new Vector3(nx + 0.5f, ny + 1, nz + 0.5f)
            });
        }
    }
}
