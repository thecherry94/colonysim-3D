namespace ColonySim.Core;

/// <summary>
/// Static definition of a block type. For stairs, ramps and corners, Facing is the direction you walk to go up;
/// for ladders it is the side of the cell they are mounted against. BaseBlock is the material block a variant is made of
/// (its own id for plain blocks).
/// </summary>
public sealed record BlockDef(
    ushort Id, string Name, BlockShape Shape, bool IsSolid, bool IsDiggable,
    int TexTop, int TexSide, int TexBottom, Facing Facing,
    bool IsNatural, float Hardness, ushort BaseBlock)
{
    public bool Occludes => Shape == BlockShape.Cube;
    public bool IsLiquid => Shape == BlockShape.Liquid;
    public bool IsSlope => Shape is BlockShape.Ramp or BlockShape.CornerOuter or BlockShape.CornerInner;
    public bool IsLadder => Shape == BlockShape.Ladder;

    /// <summary>True when walking in <paramref name="dir"/> across this block goes up.</summary>
    public bool AscendsToward(Facing dir) => Shape switch
    {
        BlockShape.Stairs or BlockShape.Ramp => Facing == dir,
        BlockShape.CornerOuter or BlockShape.CornerInner => Facing == dir || Facing.Clockwise() == dir,
        _ => false,
    };
}
