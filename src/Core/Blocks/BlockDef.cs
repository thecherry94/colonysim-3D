namespace ColonySim.Core;

/// <summary>Static definition of a block type. For stairs, Facing is the direction you walk to go up.</summary>
public sealed record BlockDef(
    ushort Id, string Name, BlockShape Shape, bool IsSolid, bool IsDiggable,
    int TexTop, int TexSide, int TexBottom, Facing Facing)
{
    public bool Occludes => Shape == BlockShape.Cube;
    public bool IsLiquid => Shape == BlockShape.Liquid;
}
