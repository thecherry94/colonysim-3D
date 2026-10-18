using System.Numerics;

namespace ColonySim.Core.Tests;

public class ToolTargetingTests
{
    static PlayerView Flat() { var w = TestWorlds.SolidBelow(64, 150); Discovery.InitialFlood(w); return new PlayerView(w, new TerrainLayers(MapSettings.Create(1, MapSize.Small))); }
    [Fact] public void Raycast_DownHitsSurfaceTop()
    {
        Assert.True(VoxelRaycast.Cast(Flat(), new(10.5f, 300, 10.5f), -Vector3.UnitY, 1000, 255, out var h));
        Assert.Equal(new Int3(10, 150, 10), h.Block); Assert.Equal(new Int3(0, 1, 0), h.Normal); Assert.Equal(151f, h.Point.Y, 3);
    }
    [Fact] public void Raycast_RespectsMaxY() { VoxelRaycast.Cast(Flat(), new(10.5f, 300, 10.5f), -Vector3.UnitY, 1000, 120, out var h); Assert.Equal(120, h.Block.Y); }
    [Fact] public void Raycast_FromOutsideMap_HitsSideFace() { Assert.True(VoxelRaycast.Cast(Flat(), new(-20, 100.5f, 5.5f), Vector3.UnitX, 1000, 255, out var h)); Assert.Equal(new Int3(-1, 0, 0), h.Normal); Assert.Equal(0, h.Block.X); }
    [Fact] public void Raycast_SkyMisses() => Assert.False(VoxelRaycast.Cast(Flat(), new(10, 300, 10), Vector3.UnitY, 1000, 255, out _));
    [Fact] public void Target_SlicedUsesViewLevel() => Assert.Equal(new Int3(4, 120, 6), ToolTargeting.Target(DesignationTool.Wall, new(new(4, 110, 6), new(0, 1, 0), default), 120, 255));
    [Fact] public void Target_TopLevel_BuildUsesFaceNeighbor() => Assert.Equal(new Int3(4, 151, 6), ToolTargeting.Target(DesignationTool.Floor, new(new(4, 150, 6), new(0, 1, 0), default), 255, 255));
    [Fact] public void Target_TopLevel_DigUsesHitBlock() => Assert.Equal(new Int3(4, 150, 6), ToolTargeting.Target(DesignationTool.Dig, new(new(4, 150, 6), new(0, 1, 0), default), 255, 255));
    [Fact] public void DragPlane_BuildAtLevel_DigAboveIt() { Assert.Equal(120f, ToolTargeting.DragPlaneHeight(DesignationTool.Wall, 120)); Assert.Equal(121f, ToolTargeting.DragPlaneHeight(DesignationTool.Dig, 120)); }
    [Fact] public void IntersectPlane_Floors() { Assert.True(ToolTargeting.IntersectPlane(new(0.5f, 10, 0.5f), Vector3.Normalize(new(1, -1, 0)), 5, out var x, out var z)); Assert.Equal(5, x); Assert.Equal(0, z); }
    [Fact] public void JumpLevel_TopFaceGoesToStandingLevel() { Assert.Equal(151, ToolTargeting.JumpLevel(new(new(1, 150, 1), new(0, 1, 0), default), 255)); Assert.Equal(150, ToolTargeting.JumpLevel(new(new(1, 150, 1), new(1, 0, 0), default), 255)); }
    [Fact] public void ExtendBox_StepsEndLevelFromWhereItIs()
    {
        var b = new DragBox(new(1, 140, 1), new(5, 140, 5));
        Assert.Equal(new Int3(5, 139, 5), ToolTargeting.ExtendBox(b, -1).End);
        Assert.Equal(143, ToolTargeting.ExtendBox(ToolTargeting.ExtendBox(b, +2), +1).End.Y);
        Assert.Equal(b.Start, ToolTargeting.ExtendBox(b, -1).Start);
    }
    [Fact] public void ExtendBox_ClampsToWorldHeight()
    {
        Assert.Equal(1, ToolTargeting.ExtendBox(new DragBox(new(1, 3, 1), new(5, 1, 5)), -1).End.Y);
        Assert.Equal(255, ToolTargeting.ExtendBox(new DragBox(new(1, 250, 1), new(5, 255, 5)), +1).End.Y);
    }
    [Fact] public void StepLevel_FirstStepDownFromTop_LandsOnStandingLevelAtFocus() => Assert.Equal(151, ToolTargeting.StepLevel(255, -1, 255, 150));
    [Fact] public void StepLevel_FirstStepDownFromTop_ClampsBelowTop()
    {
        Assert.Equal(254, ToolTargeting.StepLevel(255, -1, 255, 255));
        Assert.Equal(254, ToolTargeting.StepLevel(255, -1, 255, 254));
        Assert.Equal(1, ToolTargeting.StepLevel(255, -1, 255, -5));
    }
    [Fact] public void StepLevel_WhenSliced_StepsOneLevel()
    {
        Assert.Equal(149, ToolTargeting.StepLevel(150, -1, 255, 150));
        Assert.Equal(151, ToolTargeting.StepLevel(150, +1, 255, 150));
        Assert.Equal(255, ToolTargeting.StepLevel(254, +1, 255, 150));
    }
    [Fact] public void StepLevel_ClampsToRange()
    {
        Assert.Equal(255, ToolTargeting.StepLevel(255, +1, 255, 150));
        Assert.Equal(1, ToolTargeting.StepLevel(1, -1, 255, 150));
    }
}
