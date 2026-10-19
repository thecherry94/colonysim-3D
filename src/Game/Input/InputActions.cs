using Godot;

namespace ColonySim.Game;

/// <summary>Registry of the named input actions every Godot-side system reads. Existing actions are left untouched.</summary>
public static class InputActions
{
    public const string CamForward = "cam_forward";
    public const string CamBack = "cam_back";
    public const string CamLeft = "cam_left";
    public const string CamRight = "cam_right";
    public const string CamRotLeft = "cam_rot_left";
    public const string CamRotRight = "cam_rot_right";
    public const string LevelUp = "level_up";
    public const string LevelDown = "level_down";
    public const string LevelTop = "level_top";
    public const string ToolCamera = "tool_camera";
    public const string ToolDig = "tool_dig";
    public const string ToolWall = "tool_wall";
    public const string ToolFloor = "tool_floor";
    public const string ToolStairs = "tool_stairs";
    public const string ToolDigStairs = "tool_dig_stairs";
    public const string ToolRamp = "tool_ramp";
    public const string ToolLadder = "tool_ladder";
    public const string ToolCancel = "tool_cancel";
    public const string SimPause = "sim_pause";
    public const string SimSpeed1 = "sim_speed_1";
    public const string SimSpeed2 = "sim_speed_2";
    public const string SimSpeed3 = "sim_speed_3";
    public const string WorkPriorities = "work_priorities";
    public const string FollowColonist = "follow_colonist";
    public const string MaterialCycle = "material_cycle";
    public const string Save = "save";
    public const string DebugOverlay = "debug_overlay";

    public static void Register()
    {
        Add(CamForward, Key.W, Key.Up);
        Add(CamBack, Key.S, Key.Down);
        Add(CamLeft, Key.A, Key.Left);
        Add(CamRight, Key.D, Key.Right);
        Add(CamRotLeft, Key.Q);
        Add(CamRotRight, Key.E);
        Add(LevelUp, Key.Pageup);
        Add(LevelDown, Key.Pagedown);
        Add(LevelTop, Key.Home);
        Add(ToolCamera, Key.Escape);
        Add(ToolDig, Key.Z);
        Add(ToolDigStairs, Key.X);
        Add(ToolWall, Key.C);
        Add(ToolFloor, Key.V);
        Add(ToolStairs, Key.B);
        Add(ToolRamp, Key.N);
        Add(ToolLadder, Key.M);
        Add(ToolCancel, Key.Delete);
        Add(SimPause, Key.Space);
        Add(SimSpeed1, Key.Key1);
        Add(SimSpeed2, Key.Key2);
        Add(SimSpeed3, Key.Key3);
        Add(WorkPriorities, Key.P);
        Add(FollowColonist, Key.F);
        Add(MaterialCycle, Key.Tab);
        Add(Save, Key.S, ctrl: true);
        Add(DebugOverlay, Key.F3);
    }

    static void Add(string action, params Key[] keys) => Add(action, false, keys);

    static void Add(string action, Key key, bool ctrl) => Add(action, ctrl, key);

    static void Add(string action, bool ctrl, params Key[] keys)
    {
        if (InputMap.HasAction(action)) return;
        InputMap.AddAction(action);
        foreach (var key in keys)
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key, CtrlPressed = ctrl });
    }
}
