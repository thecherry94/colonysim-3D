using ColonySim.Core;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace ColonySim.Game;

/// <summary>
/// The designation tools. Left-drag draws a box at the current view level (see <see cref="ToolTargeting"/>);
/// changing the level mid-drag stretches it into a 3D box; release applies it, Esc cancels. With the Camera tool,
/// a double-click jumps the view to the clicked block's level. Right and middle buttons stay with the camera.
/// </summary>
public partial class ToolController : Node3D
{
    const float RayLength = 2000;
    const float HoverSize = 1.06f;
    // Rays this close to horizontal give absurd drag-plane intersections; such moves keep the previous end.
    const float MinPlaneRayY = 1e-4f;

    MapScene _scene = null!;
    ColonyMap _map = null!;
    DesignationTool? _tool;
    BuildMaterial _material;
    string _status = "";
    MeshInstance3D _hover = null!;

    Vector2 _mouse;
    bool _mouseOverWorld;

    bool _dragging;
    DragBox _box;
    int _levelAtStart;
    float _planeY;
    int _previewJobsVersion;

    /// <summary>The active designation tool; null is the Camera tool. Changing it cancels any drag.</summary>
    public DesignationTool? ActiveTool
    {
        get => _tool;
        set
        {
            CancelDrag();
            _tool = value;
            if (_hover != null) _hover.Visible = false;
            Status = ToolHint(value);
        }
    }

    public BuildMaterial Material
    {
        get => _material;
        set
        {
            _material = value;
            if (_dragging) RefreshPreview();
        }
    }

    /// <summary>The hint for the active tool, or the summary of the drag in progress.</summary>
    public string Status
    {
        get => _status;
        private set
        {
            if (_status == value) return;
            _status = value;
            StatusChanged?.Invoke();
        }
    }

    public bool IsDragging => _dragging;

    public event Action? StatusChanged;

    public static string ToolName(DesignationTool? tool) => tool?.ToString() ?? "Camera";

    public void Setup(MapScene scene)
    {
        _scene = scene;
        _map = scene.Map;
        scene.Slice.LevelChanged += OnLevelChanged;
        Status = ToolHint(null);
    }

    public override void _Ready()
    {
        _hover = new MeshInstance3D
        {
            Name = "Hover",
            Mesh = WireCube(HoverSize),
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = Colors.White,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_hover);
    }

    public override void _ExitTree()
    {
        if (_scene?.Slice != null) _scene.Slice.LevelChanged -= OnLevelChanged;
    }

    /// <summary>Sees mouse moves and the drag's release before the HUD, so a drag also follows the cursor over panels.</summary>
    public override void _Input(InputEvent @event)
    {
        if (_map == null) return;
        switch (@event)
        {
            case InputEventMouseMotion motion:
                _mouse = motion.Position;
                _mouseOverWorld = false; // set again in _UnhandledInput if no HUD control takes the event
                if (_dragging) UpdateDragEnd();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } release when _dragging:
                _mouse = release.Position;
                Finish();
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_map == null) return;
        if (@event is InputEventMouseMotion)
        {
            _mouseOverWorld = true;
            return;
        }
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press)
        {
            _mouse = press.Position;
            _mouseOverWorld = true;
            if (_tool == null) { if (press.DoubleClick) JumpToClickedLevel(); }
            else StartDrag();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event.IsActionPressed(InputActions.ToolCamera))
        {
            if (_dragging) { CancelDrag(); Status = ToolHint(_tool); }
            else ActiveTool = null;
        }
        else if (@event.IsActionPressed(InputActions.MaterialCycle))
            Material = _material == BuildMaterial.Wood ? BuildMaterial.Stone : BuildMaterial.Wood;
        else if (!_dragging && HotkeyTool(@event) is { } tool) ActiveTool = tool;
        else return;
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        if (_map == null) return;
        if (_dragging)
        {
            // Instant work changes the world and the queue under a drag; keep the preview truthful.
            if (_map.Jobs.Version != _previewJobsVersion) RefreshPreview();
            _hover.Visible = false;
            return;
        }
        bool show = false;
        if (_tool is { } tool && _mouseOverWorld && Raycast(_mouse, out var hit))
        {
            var cell = ToolTargeting.Target(tool, hit, _scene.Slice.ViewLevel, _scene.Slice.TopLevel);
            _hover.Position = new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);
            show = true;
        }
        _hover.Visible = show;
    }

    static DesignationTool? HotkeyTool(InputEvent e) =>
        e.IsActionPressed(InputActions.ToolDig) ? DesignationTool.Dig
        : e.IsActionPressed(InputActions.ToolWall) ? DesignationTool.Wall
        : e.IsActionPressed(InputActions.ToolFloor) ? DesignationTool.Floor
        : e.IsActionPressed(InputActions.ToolStairs) ? DesignationTool.Stairs
        : e.IsActionPressed(InputActions.ToolCancel) ? DesignationTool.Cancel
        : null;

    bool Raycast(Vector2 screen, out RaycastHit hit)
    {
        var camera = _scene.Rig.Camera;
        return VoxelRaycast.Cast(_map.View, ToNumerics(camera.ProjectRayOrigin(screen)),
            ToNumerics(camera.ProjectRayNormal(screen)), RayLength, _scene.Slice.ViewLevel, out hit);
    }

    void JumpToClickedLevel()
    {
        if (Raycast(_mouse, out var hit)) _scene.Slice.SetLevel(ToolTargeting.JumpLevel(hit, _scene.Slice.TopLevel));
    }

    void StartDrag()
    {
        if (_tool is not { } tool || !Raycast(_mouse, out var hit)) return;
        var slice = _scene.Slice;
        var start = ToolTargeting.Target(tool, hit, slice.ViewLevel, slice.TopLevel);
        _box = new DragBox(start, start);
        _levelAtStart = slice.ViewLevel;
        _planeY = ToolTargeting.DragPlaneHeight(tool, start.Y);
        _dragging = true;
        RefreshPreview();
    }

    /// <summary>The box's far corner follows the cursor on the horizontal plane the drag started on.</summary>
    void UpdateDragEnd()
    {
        var camera = _scene.Rig.Camera;
        var dir = camera.ProjectRayNormal(_mouse);
        if (Math.Abs(dir.Y) < MinPlaneRayY) return;
        if (!ToolTargeting.IntersectPlane(ToNumerics(camera.ProjectRayOrigin(_mouse)), ToNumerics(dir), _planeY, out int x, out int z))
            return;
        // One cell past the edge at most: the designator clips, and a wall outline must not land on the edge column.
        x = Math.Clamp(x, -1, _map.World.SizeX);
        z = Math.Clamp(z, -1, _map.World.SizeZ);
        if (x == _box.End.X && z == _box.End.Z) return;
        _box = _box with { End = new Int3(x, _box.End.Y, z) };
        RefreshPreview();
    }

    void OnLevelChanged(int level)
    {
        if (!_dragging) return;
        _box = ToolTargeting.ExtendBox(_box, _levelAtStart, level);
        RefreshPreview();
    }

    void RefreshPreview()
    {
        var tool = _tool!.Value;
        var jobs = Designator.Preview(_map, tool, _material, _box);
        _previewJobsVersion = _map.Jobs.Version;
        _scene.Overlay.ShowPreview(jobs, tool);
        int w = Math.Abs(_box.End.X - _box.Start.X) + 1;
        int d = Math.Abs(_box.End.Z - _box.Start.Z) + 1;
        int levels = Math.Abs(_box.End.Y - _box.Start.Y) + 1;
        Status = $"{ToolName(tool)}: {w} × {d}{(levels > 1 ? $" × {levels} levels" : "")} → {jobs.Count} blocks (release to confirm, Esc to cancel)";
    }

    void Finish()
    {
        Designator.Apply(_map, _tool!.Value, _material, _box);
        CancelDrag();
        Status = ToolHint(_tool);
    }

    void CancelDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        _scene.Overlay.ClearPreview();
    }

    static string ToolHint(DesignationTool? tool) => tool switch
    {
        null => "Camera: right-drag orbits, middle-drag pans, double-click a block to jump to its level.",
        DesignationTool.Dig => "Dig: drag over blocks. Hold the mouse and press PgDn to dig several levels deep.",
        DesignationTool.Wall => "Wall: drag a rectangle to build walls around its outline. Hold and press PgUp for taller walls.",
        DesignationTool.Floor => "Floor: drag to fill a rectangle with floor. Go up a level to put a floor on top of walls.",
        DesignationTool.Stairs => "Stairs: drag in the direction the stairs should climb, one block up per step.",
        _ => "Cancel: drag over marked jobs to remove them.",
    };

    static NVector3 ToNumerics(Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>The 12 edges of a cube of side <paramref name="size"/> centered on the origin.</summary>
    static ArrayMesh WireCube(float size)
    {
        float h = size / 2;
        var corners = new Vector3[8];
        for (int i = 0; i < 8; i++) corners[i] = new Vector3((i & 1) == 0 ? -h : h, (i & 2) == 0 ? -h : h, (i & 4) == 0 ? -h : h);
        var lines = new List<Vector3>();
        for (int i = 0; i < 8; i++)
            for (int bit = 1; bit < 8; bit <<= 1)
                if ((i & bit) == 0) { lines.Add(corners[i]); lines.Add(corners[i | bit]); }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = lines.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
        return mesh;
    }
}
