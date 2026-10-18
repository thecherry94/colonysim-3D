namespace ColonySim.Core;

/// <summary>A chunk's opaque geometry and its separately rendered liquid geometry.</summary>
public sealed record MeshPair(MeshBuffers Opaque, MeshBuffers Liquid)
{
    public static MeshPair Empty() => new(new MeshBuffers(), new MeshBuffers());
}
