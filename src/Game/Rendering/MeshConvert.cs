using System.Runtime.InteropServices;
using ColonySim.Core;
using Godot;
using GArray = Godot.Collections.Array;

namespace ColonySim.Game;

/// <summary>Converts engine-agnostic <see cref="MeshBuffers"/> into Godot meshes.</summary>
public static class MeshConvert
{
    public static ArrayMesh? ToArrayMesh(MeshBuffers b)
    {
        using var arrays = ToArrays(b);
        return arrays == null ? null : FromArrays(arrays);
    }

    /// <summary>
    /// Builds the surface arrays (Vertex, Normal, TexUV, TexUV2, Index), or null when empty.
    /// Touches no scene or server state, so it may run on a worker thread.
    /// </summary>
    public static GArray? ToArrays(MeshBuffers b)
    {
        if (b.IsEmpty) return null;
        var arrays = new GArray();
        arrays.Resize((int)Mesh.ArrayType.Max);
        // System.Numerics and Godot vectors share the same float layout, so the lists are reinterpreted, not converted.
        arrays[(int)Mesh.ArrayType.Vertex] = Cast<System.Numerics.Vector3, Vector3>(b.Vertices);
        arrays[(int)Mesh.ArrayType.Normal] = Cast<System.Numerics.Vector3, Vector3>(b.Normals);
        arrays[(int)Mesh.ArrayType.TexUV] = Cast<System.Numerics.Vector2, Vector2>(b.Uvs);
        arrays[(int)Mesh.ArrayType.TexUV2] = Cast<System.Numerics.Vector2, Vector2>(b.Uv2s);
        arrays[(int)Mesh.ArrayType.Index] = b.Indices.ToArray();
        return arrays;
    }

    public static ArrayMesh FromArrays(GArray arrays)
    {
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    static TTo[] Cast<TFrom, TTo>(List<TFrom> list) where TFrom : struct where TTo : struct =>
        MemoryMarshal.Cast<TFrom, TTo>(CollectionsMarshal.AsSpan(list)).ToArray();
}
