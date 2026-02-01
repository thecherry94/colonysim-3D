using Godot;
using System.Collections.Generic;

namespace ColonySim.World;

/// <summary>
/// Static class that generates ArrayMesh geometry from chunk block data.
/// Implements face culling: only renders faces adjacent to air/transparent blocks.
/// </summary>
public static class ChunkMeshGenerator
{
    /// <summary>
    /// The 6 cardinal directions for face checks.
    /// </summary>
    private static readonly Vector3I[] FaceDirections =
    {
        new Vector3I(0, 1, 0),   // Top (+Y)
        new Vector3I(0, -1, 0),  // Bottom (-Y)
        new Vector3I(1, 0, 0),   // Right (+X)
        new Vector3I(-1, 0, 0),  // Left (-X)
        new Vector3I(0, 0, 1),   // Front (+Z)
        new Vector3I(0, 0, -1),  // Back (-Z)
    };

    /// <summary>
    /// Normal vectors for each face (same indices as FaceDirections).
    /// </summary>
    private static readonly Vector3[] FaceNormals =
    {
        Vector3.Up,      // Top
        Vector3.Down,    // Bottom
        Vector3.Right,   // Right
        Vector3.Left,    // Left
        Vector3.Back,    // Front (+Z in Godot)
        Vector3.Forward, // Back (-Z in Godot)
    };

    /// <summary>
    /// Vertex positions for each face, relative to block origin.
    /// 4 vertices per face, in clockwise order when viewed from outside.
    /// </summary>
    private static readonly Vector3[][] FaceVertices =
    {
        // Top face (+Y) - looking down at it from above
        new Vector3[]
        {
            new Vector3(0, 1, 0),
            new Vector3(0, 1, 1),
            new Vector3(1, 1, 1),
            new Vector3(1, 1, 0),
        },
        // Bottom face (-Y) - vertices: h,g,c,d from working cube example
        new Vector3[]
        {
            new Vector3(0, 0, 1),  // h: bottom-left-back
            new Vector3(1, 0, 1),  // g: bottom-right-back
            new Vector3(1, 0, 0),  // c: bottom-right-front
            new Vector3(0, 0, 0),  // d: bottom-left-front
        },
        // Right face (+X) - looking at it from the right
        new Vector3[]
        {
            new Vector3(1, 0, 1),
            new Vector3(1, 0, 0),
            new Vector3(1, 1, 0),
            new Vector3(1, 1, 1),
        },
        // Left face (-X) - looking at it from the left
        new Vector3[]
        {
            new Vector3(0, 0, 0),
            new Vector3(0, 0, 1),
            new Vector3(0, 1, 1),
            new Vector3(0, 1, 0),
        },
        // Front face (+Z) - looking at it from front
        new Vector3[]
        {
            new Vector3(0, 0, 1),
            new Vector3(1, 0, 1),
            new Vector3(1, 1, 1),
            new Vector3(0, 1, 1),
        },
        // Back face (-Z) - looking at it from back
        new Vector3[]
        {
            new Vector3(1, 0, 0),
            new Vector3(0, 0, 0),
            new Vector3(0, 1, 0),
            new Vector3(1, 1, 0),
        },
    };

    /// <summary>
    /// UV coordinates for each face vertex.
    /// Standard 0-1 mapping for future texture support.
    /// </summary>
    private static readonly Vector2[] FaceUVs =
    {
        new Vector2(0, 1),
        new Vector2(1, 1),
        new Vector2(1, 0),
        new Vector2(0, 0),
    };

    /// <summary>
    /// Triangle indices for a quad face.
    /// Two triangles with REVERSED winding (counter-clockwise to clockwise).
    /// Original was { 0, 1, 2, 0, 2, 3 } but our vertices are CCW, so we reverse.
    /// </summary>
    private static readonly int[] QuadIndices = { 0, 2, 1, 0, 3, 2 };

    /// <summary>
    /// Generates an ArrayMesh from chunk block data.
    /// Creates separate surfaces for each block type (for different materials).
    /// </summary>
    /// <param name="chunk">The chunk to generate mesh for</param>
    /// <param name="materials">Materials array indexed by BlockType</param>
    /// <returns>Generated ArrayMesh with all visible faces</returns>
    public static ArrayMesh GenerateMesh(Chunk chunk, StandardMaterial3D[] materials)
    {
        var mesh = new ArrayMesh();

        // Generate one surface per block type (skip Air at index 0)
        for (int blockTypeIndex = 1; blockTypeIndex < materials.Length; blockTypeIndex++)
        {
            BlockType blockType = (BlockType)blockTypeIndex;

            // Collect geometry for this block type
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var indices = new List<int>();

            // Iterate all blocks in chunk
            for (int x = 0; x < Chunk.SIZE; x++)
            {
                for (int y = 0; y < Chunk.SIZE; y++)
                {
                    for (int z = 0; z < Chunk.SIZE; z++)
                    {
                        // Skip if not this block type
                        if (chunk.GetBlock(x, y, z) != blockType)
                            continue;

                        // Add faces for this block
                        Vector3 blockPos = new Vector3(x, y, z);
                        AddBlockFaces(chunk, blockPos, x, y, z,
                            vertices, normals, uvs, indices);
                    }
                }
            }

            // Skip if no geometry for this block type
            if (vertices.Count == 0)
                continue;

            // Create surface array
            var surfaceArray = new Godot.Collections.Array();
            surfaceArray.Resize((int)Mesh.ArrayType.Max);

            surfaceArray[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
            surfaceArray[(int)Mesh.ArrayType.Normal] = normals.ToArray();
            surfaceArray[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
            surfaceArray[(int)Mesh.ArrayType.Index] = indices.ToArray();

            // Debug: log vertex count per block type
            GD.Print($"Block type {blockType}: {vertices.Count} vertices, {indices.Count} indices");

            // Add surface to mesh
            int surfaceIndex = mesh.GetSurfaceCount();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, surfaceArray);

            // Apply material
            if (materials[blockTypeIndex] != null)
            {
                mesh.SurfaceSetMaterial(surfaceIndex, materials[blockTypeIndex]);
            }
        }

        return mesh;
    }

    /// <summary>
    /// Adds visible faces for a single block to the geometry lists.
    /// Only adds faces that are adjacent to transparent blocks (face culling).
    /// </summary>
    private static void AddBlockFaces(
        Chunk chunk,
        Vector3 blockPos,
        int x, int y, int z,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int> indices)
    {
        // Check each of the 6 faces
        for (int faceIndex = 0; faceIndex < 6; faceIndex++)
        {
            // Get neighbor position
            Vector3I dir = FaceDirections[faceIndex];
            int nx = x + dir.X;
            int ny = y + dir.Y;
            int nz = z + dir.Z;

            // Get neighbor block (returns Air if out of bounds)
            BlockType neighbor = chunk.GetBlock(nx, ny, nz);

            // Only add face if neighbor is transparent (air)
            if (!BlockData.IsTransparent(neighbor))
                continue;

            // Add this face
            AddFace(blockPos, faceIndex, vertices, normals, uvs, indices);
        }
    }

    /// <summary>
    /// Adds a single face (quad) to the geometry lists.
    /// </summary>
    private static void AddFace(
        Vector3 blockPos,
        int faceIndex,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int> indices)
    {
        // Record starting vertex index for this face
        int startIndex = vertices.Count;

        // Add 4 vertices for this face
        Vector3[] faceVerts = FaceVertices[faceIndex];
        Vector3 normal = FaceNormals[faceIndex];

        for (int i = 0; i < 4; i++)
        {
            vertices.Add(blockPos + faceVerts[i]);
            normals.Add(normal);
            uvs.Add(FaceUVs[i]);
        }

        // Add 6 indices for 2 triangles
        int[] quadIndices = QuadIndices;
        for (int i = 0; i < 6; i++)
        {
            indices.Add(startIndex + quadIndices[i]);
        }
    }
}
