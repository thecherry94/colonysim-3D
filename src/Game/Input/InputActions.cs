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
    public const string ToolCancel = "tool_cancel";
    public const string MaterialCycle = "material_cycle";
    public const string Save = "save";

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
        Add(ToolDig, Key.Key1);
        Add(ToolWall, Key.Key2);
        Add(ToolFloor, Key.Key3);
        Add(ToolStairs, Key.Key4);
        Add(ToolCancel, Key.Key5);
        Add(MaterialCycle, Key.Tab);
        Add(Save, Key.S, ctrl: true);
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
