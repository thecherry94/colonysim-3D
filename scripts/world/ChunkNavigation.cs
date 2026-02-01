using Godot;
using System.Collections.Generic;

namespace ColonySim.World;

/// <summary>
/// Generates NavigationMesh for chunks based on walkable block surfaces.
/// Walkable = solid block with air above (top face is exposed).
/// </summary>
public static class ChunkNavigation
{
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
}
