namespace ColonySim.Core;

/// <summary>
/// Block geometry class. Append-only: saves and registry ids depend on the numeric values.
/// Corners (with <see cref="BlockDef.Facing"/> f): <c>CornerOuter</c> has a single raised corner, between the f side and the
/// f.Clockwise() side. <c>CornerInner</c> has the f and f.Clockwise() sides fully raised and the opposite corner lowered.
/// Both corners ascend toward f and toward f.Clockwise(). A ladder's Facing is the side of the cell it is mounted against.
/// </summary>
public enum BlockShape { None, Cube, Liquid, Stairs, Ramp, CornerOuter, CornerInner, Ladder }
