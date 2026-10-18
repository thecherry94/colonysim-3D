using System.Numerics;

namespace ColonySim.Core;

/// <summary>Pure rules deciding which cell/level a designation tool acts on while picking and dragging.</summary>
public static class ToolTargeting
{
    const int WorldTop = 255;

    public static Int3 Target(DesignationTool tool, RaycastHit hit, int viewLevel, int topLevel)
    {
        if (viewLevel < topLevel) return new Int3(hit.Block.X, viewLevel, hit.Block.Z);
        return tool is DesignationTool.Dig or DesignationTool.Cancel ? hit.Block : hit.Block + hit.Normal;
    }

    public static float DragPlaneHeight(DesignationTool tool, int targetY) =>
        tool is DesignationTool.Dig or DesignationTool.Cancel ? targetY + 1 : targetY;

    public static bool IntersectPlane(Vector3 origin, Vector3 direction, float planeY, out int x, out int z)
    {
        x = z = 0;
        if (MathF.Abs(direction.Y) < 1e-9f) return false;
        float t = (planeY - origin.Y) / direction.Y;
        if (t < 0) return false;
        x = (int)MathF.Floor(origin.X + direction.X * t);
        z = (int)MathF.Floor(origin.Z + direction.Z * t);
        return true;
    }

    public static int JumpLevel(RaycastHit hit, int topLevel) =>
        Math.Clamp(hit.Normal.Y > 0 ? hit.Block.Y + 1 : hit.Block.Y, 1, topLevel);

    /// <summary>
    /// The level one step (<paramref name="delta"/> = ±1) from <paramref name="viewLevel"/>. The first step down from
    /// the unsliced top lands on the level you'd stand on at the camera focus (<paramref name="focusSurface"/> + 1),
    /// not one block under the sky; every other step moves one level. Clamped to [1, <paramref name="topLevel"/>].
    /// </summary>
    public static int StepLevel(int viewLevel, int delta, int topLevel, int focusSurface) =>
        viewLevel >= topLevel && delta < 0
            ? Math.Clamp(focusSurface + 1, 1, topLevel - 1)
            : Math.Clamp(viewLevel + delta, 1, topLevel);

    /// <summary>Moves the box's far level by <paramref name="delta"/> (a level step mid-drag), clamped to [1, 255].</summary>
    public static DragBox ExtendBox(DragBox box, int delta) =>
        box with { End = box.End with { Y = Math.Clamp(box.End.Y + delta, 1, WorldTop) } };
}
