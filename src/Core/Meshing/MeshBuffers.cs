using System.Numerics;

namespace ColonySim.Core;

/// <summary>Engine-agnostic triangle mesh. <c>Uv2.X</c> is the texture layer, <c>Uv2.Y</c> the ambient occlusion (1 = unoccluded).</summary>
public sealed class MeshBuffers
{
    public List<Vector3> Vertices { get; }
    public List<Vector3> Normals { get; }
    public List<Vector2> Uvs { get; }
    public List<Vector2> Uv2s { get; }
    public List<int> Indices { get; }

    public MeshBuffers() : this(0) { }

    /// <summary>Pre-sizes the buffers for <paramref name="quadCapacity"/> quads.</summary>
    public MeshBuffers(int quadCapacity)
    {
        Vertices = new(quadCapacity * 4); Normals = new(quadCapacity * 4);
        Uvs = new(quadCapacity * 4); Uv2s = new(quadCapacity * 4);
        Indices = new(quadCapacity * 6);
    }

    public bool IsEmpty => Indices.Count == 0;

    /// <summary>
    /// Adds a quad whose corners p0..p3 wind clockwise seen from outside. Splits along the p1-p3 diagonal when
    /// <paramref name="flip"/> is set (used to keep AO gradients symmetric).
    /// </summary>
    internal void AddQuad(Vector3 normal, float layer, bool flip,
        Vector3 p0, Vector2 uv0, float ao0, Vector3 p1, Vector2 uv1, float ao1,
        Vector3 p2, Vector2 uv2, float ao2, Vector3 p3, Vector2 uv3, float ao3)
    {
        int i = Vertices.Count;
        Vertices.Add(p0); Vertices.Add(p1); Vertices.Add(p2); Vertices.Add(p3);
        Normals.Add(normal); Normals.Add(normal); Normals.Add(normal); Normals.Add(normal);
        Uvs.Add(uv0); Uvs.Add(uv1); Uvs.Add(uv2); Uvs.Add(uv3);
        Uv2s.Add(new Vector2(layer, ao0)); Uv2s.Add(new Vector2(layer, ao1)); Uv2s.Add(new Vector2(layer, ao2)); Uv2s.Add(new Vector2(layer, ao3));
        if (flip)
        {
            Indices.Add(i); Indices.Add(i + 1); Indices.Add(i + 3);
            Indices.Add(i + 1); Indices.Add(i + 2); Indices.Add(i + 3);
        }
        else
        {
            Indices.Add(i); Indices.Add(i + 1); Indices.Add(i + 2);
            Indices.Add(i); Indices.Add(i + 2); Indices.Add(i + 3);
        }
    }

    /// <summary>Adds a triangle whose corners p0..p2 wind clockwise seen from outside.</summary>
    internal void AddTriangle(Vector3 normal, float layer,
        Vector3 p0, Vector2 uv0, float ao0, Vector3 p1, Vector2 uv1, float ao1, Vector3 p2, Vector2 uv2, float ao2)
    {
        int i = Vertices.Count;
        Vertices.Add(p0); Vertices.Add(p1); Vertices.Add(p2);
        Normals.Add(normal); Normals.Add(normal); Normals.Add(normal);
        Uvs.Add(uv0); Uvs.Add(uv1); Uvs.Add(uv2);
        Uv2s.Add(new Vector2(layer, ao0)); Uv2s.Add(new Vector2(layer, ao1)); Uv2s.Add(new Vector2(layer, ao2));
        Indices.Add(i); Indices.Add(i + 1); Indices.Add(i + 2);
    }
}
