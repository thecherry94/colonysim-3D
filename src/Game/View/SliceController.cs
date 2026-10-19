using System.Diagnostics;
using ColonySim.Core;
using Godot;

namespace ColonySim.Game;

/// <summary>
/// Owns the view level. Everything above it is hidden: the block shaders discard above the global
/// <c>slice_height</c>, chunks entirely above it are hidden, and solid blocks the cut passes through get caps.
/// PgUp/PgDn and Shift+wheel step one level (the first step down from the top lands on the ground at the camera
/// focus), Home returns to the top (nothing hidden). While a tool drag is in progress the tool controller takes these
/// inputs first (see <see cref="ToolController"/>). While the camera follows a colonist (<see cref="FollowFeetY"/>) in
/// a slice, the view level tracks the floor it stands on.
/// </summary>
public partial class SliceController : Node
{
    const string SliceHeightParam = "slice_height";
    const float UnslicedHeight = 100000;
    const int MinLevel = 1;

    ColonyMap _map = null!;
    ChunkRenderer _chunks = null!;
    CapRenderer _caps = null!;
    CameraRig _camera = null!;
    bool _capsEnabled = true;
    readonly HashSet<(int, int)> _dirtyColumns = new();
    int _followedY = int.MinValue;

    /// <summary>The highest block Y still drawn. At <see cref="TopLevel"/> nothing is hidden.</summary>
    public int ViewLevel { get; private set; } = VoxelWorld.Height - 1;

    /// <summary>The followed colonist's feet cell y, set by the map scene; consulted only while the camera follows.</summary>
    public Func<int>? FollowFeetY { get; set; }

    public int TopLevel => VoxelWorld.Height - 1;

    public bool IsSliced => ViewLevel < TopLevel;

    /// <summary>Time the last <see cref="SetLevel"/> took, including the cap mesh upload.</summary>
    public float LastLevelChangeMs { get; private set; }

    /// <summary>Dark caps on (default) or off. Off still closes the cut, with the blocks' raw top texture.</summary>
    public bool CapsEnabled
    {
        get => _capsEnabled;
        set { _capsEnabled = value; _caps.Dark = value; }
    }

    /// <summary>Fog of war on (default) or off: remeshes every chunk and re-slices, since what is visible changes everywhere.</summary>
    public bool FogEnabled
    {
        get => _map.View.FogEnabled;
        set
        {
            if (_map.View.FogEnabled == value) return;
            _map.View.FogEnabled = value;
            _map.World.MarkAllDirty();
            SetLevel(ViewLevel);
        }
    }

    public void Setup(ColonyMap map, ChunkRenderer chunks, CapRenderer caps, CameraRig camera)
    {
        _map = map;
        _chunks = chunks;
        _caps = caps;
        _camera = camera;
        _caps.Dark = _capsEnabled;
        _chunks.EditsApplied += OnEditsApplied;
    }

    public override void _ExitTree()
    {
        if (_chunks != null) _chunks.EditsApplied -= OnEditsApplied;
    }

    /// <summary>Sets the view level (clamped to [1, <see cref="TopLevel"/>]) and re-slices, even if unchanged.</summary>
    public void SetLevel(int level)
    {
        long start = Stopwatch.GetTimestamp();
        ViewLevel = Math.Clamp(level, MinLevel, TopLevel);
        bool sliced = IsSliced;

        RenderingServer.GlobalShaderParameterSet(SliceHeightParam, sliced ? ViewLevel + 1f : UnslicedHeight);
        _chunks.SetSliceLevel(ViewLevel);
        if (sliced) _caps.Rebuild(ViewLevel);
        else _caps.Clear();
        _camera.FocusOnLevel(ViewLevel, sliced);

        LastLevelChangeMs = (float)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    /// <summary>Following in a slice, a change of the colonist's floor moves the view level to it (and nothing else does).</summary>
    public override void _Process(double delta)
    {
        if (_map == null || FollowFeetY == null || !_camera.IsFollowing)
        {
            _followedY = int.MinValue; // the next follow starts by snapping to its colonist
            return;
        }
        if (!IsSliced) return;
        int y = FollowFeetY();
        if (y == _followedY) return;
        _followedY = y;
        if (y - 1 != ViewLevel) SetLevel(y - 1);
    }

    /// <summary>One level up (+1) or down (-1); see <see cref="ToolTargeting.StepLevel"/>.</summary>
    void Step(int delta)
    {
        int target = ToolTargeting.StepLevel(ViewLevel, delta, TopLevel, _camera.SurfaceAtFocus());
        if (target != ViewLevel) SetLevel(target);
    }

    /// <summary>+1 or -1 if <paramref name="e"/> is a level step (PgUp/PgDn, Shift+wheel), else 0.</summary>
    public static int StepDelta(InputEvent e)
    {
        if (e.IsActionPressed(InputActions.LevelUp, allowEcho: true)) return +1;
        if (e.IsActionPressed(InputActions.LevelDown, allowEcho: true)) return -1;
        // Some platforms turn Shift+wheel into horizontal scrolling, so left/right count as up/down.
        if (e is InputEventMouseButton { Pressed: true, ShiftPressed: true } wheel)
            return wheel.ButtonIndex switch
            {
                MouseButton.WheelUp or MouseButton.WheelLeft => +1,
                MouseButton.WheelDown or MouseButton.WheelRight => -1,
                _ => 0,
            };
        return 0;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_map == null) return;
        if (StepDelta(@event) is var delta and not 0) Step(delta);
        else if (@event.IsActionPressed(InputActions.LevelTop)) { if (IsSliced) SetLevel(TopLevel); }
        else return;
        GetViewport().SetInputAsHandled();
    }

    /// <summary>
    /// An edit near the cut changes the caps of its column: rebuild those columns in the frame the edited chunks
    /// show their new meshes, so caps and chunk meshes never disagree.
    /// </summary>
    void OnEditsApplied(IReadOnlyCollection<Int3> chunks)
    {
        if (!IsSliced) return;
        int cut = ViewLevel;
        _dirtyColumns.Clear();
        foreach (var c in chunks)
        {
            int minY = c.Y * VoxelWorld.ChunkSize, maxY = minY + VoxelWorld.ChunkSize - 1;
            if (cut + 1 >= minY && cut <= maxY) _dirtyColumns.Add((c.X, c.Z));
        }
        if (_dirtyColumns.Count > 0) _caps.RebuildColumns(_dirtyColumns, cut);
    }
}
