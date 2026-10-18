using System.Diagnostics;
using ColonySim.Core;
using Godot;

namespace ColonySim.Game;

/// <summary>Final-review checks: level steps and drags from the default unsliced view, a steady edit stream, and the
/// Menu's unsaved-changes confirmation.</summary>
public partial class VisualShots
{
    const int StreamEdits = 60;
    const int StreamDrainFrames = 30;

    /// <summary>
    /// Starts from the default view (unsliced, nothing hidden) with no double-click first, like a player who just
    /// opened the map, and drives the real input pipeline:
    /// <list type="bullet">
    /// <item><c>unsliced_first_pgdn</c>: the first PgDn lands on the ground at the focus (surface + 1) and the
    /// camera focus settles one above it, not up in the sky.</item>
    /// <item><c>unsliced_dig_pgdn</c>: a Dig drag plus 3×PgDn marks a box 4 levels deep from the clicked blocks; the
    /// view stays at the top with the camera at the ground, and afterwards the hover box sits on a real block.</item>
    /// <item><c>unsliced_wall_pgup</c>: a Wall drag plus 2×PgUp marks 3-level walls; Home mid-drag changes nothing.</item>
    /// </list>
    /// The jobs are cancelled again afterwards.
    /// </summary>
    async Task CheckUnslicedTools(Site s)
    {
        var tools = _scene.Tools;
        var slice = _scene.Slice;
        var rig = _scene.Rig;
        var map = _scene.Map;
        int h = s.H, ax = s.X + 2, az = s.Z + 2, bx = ax + 4, bz = az + 3; // a 5×4 rectangle
        static Vector3 Top(int x, int y, int z) => new(x + 0.5f, y + 1, z + 0.5f);
        bool InRect(Int3 p) => p.X >= ax && p.X <= bx && p.Z >= az && p.Z <= bz;
        int Count(JobKind kind, int y0, int y1) =>
            map.Jobs.All.Count(j => j.Kind == kind && j.Position.Y >= y0 && j.Position.Y <= y1 && InRect(j.Position));
        string ViewText() => slice.IsSliced ? slice.ViewLevel.ToString() : "top";
        bool Near(float a, float b) => Math.Abs(a - b) < 0.5f;
        Metric("unsliced_site", $"({s.X},{s.Z}) ground:{h}");

        await View(ax + 2, az + 2, slice.TopLevel, 0, 60, 36);
        await Frames(30);

        // (a) First PgDn from the top.
        int surface = rig.SurfaceAtFocus();
        await Key(Godot.Key.Pagedown);
        int level = slice.ViewLevel;
        await Frames(60); // the camera eases to the new level
        float focusY = rig.FocusPoint.Y;
        Metric("unsliced_first_pgdn", $"level:{level}/{surface + 1} focusY:{focusY:0.0}/{level + 1} " +
            $"ok:{level == surface + 1 && Near(focusY, level + 1)}");
        await Key(Godot.Key.Home);
        await Frames(60);

        // (b) The developer's repro: Dig drag from the default view, PgDn×3 while holding.
        await Key(Godot.Key.Key1);
        int before = map.Jobs.Count;
        await Press(Top(ax, h, az));
        await Move(Top(bx, h, bz), held: true);
        for (int i = 0; i < 3; i++) await Key(Godot.Key.Pagedown);
        string status = tools.Status;
        string viewMid = ViewText();
        await Shoot("drag_preview_unsliced");
        await Release(Top(bx, h, bz));
        int dug = map.Jobs.Count - before, inBox = Count(JobKind.Dig, h - 3, h);
        await Frames(30);
        focusY = rig.FocusPoint.Y;
        await Move(Top(ax + 1, h, az + 1));
        var hover = tools.GetNode<Node3D>("Hover");
        bool hoverOk = hover.Visible && hover.Position == new Vector3(ax + 1.5f, h + 0.5f, az + 1.5f);
        Metric("unsliced_dig_pgdn", $"jobs:{dug}/80(in_box:{inBox}) status:[{status}] view_mid:{viewMid} view_after:{ViewText()} " +
            $"focusY:{focusY:0.0}/{h + 1} hover_after:{(hoverOk ? "ok" : $"bad@{hover.Position}")} " +
            $"ok:{dug == 80 && inBox == 80 && viewMid == "top" && !slice.IsSliced && Near(focusY, h + 1) && hoverOk}");

        // (c) Wall drag from the default view, PgUp×2, then Home (ignored mid-drag).
        await Key(Godot.Key.Key2);
        before = map.Jobs.Count;
        await Press(Top(ax, h, az));
        await Move(Top(bx, h, bz), held: true);
        await Key(Godot.Key.Pageup);
        await Key(Godot.Key.Pageup);
        await Key(Godot.Key.Home);
        status = tools.Status;
        await Release(Top(bx, h, bz));
        int walls = map.Jobs.Count - before;
        inBox = Count(JobKind.BuildWall, h + 1, h + 3);
        Metric("unsliced_wall_pgup", $"jobs:{walls}/42(in_box:{inBox}) status:[{status}] view_after:{ViewText()} " +
            $"ok:{walls == 42 && inBox == 42 && !slice.IsSliced}");

        Designator.Apply(map, DesignationTool.Cancel, BuildMaterial.Wood, new DragBox(new(ax, h - 3, az), new(bx, h + 3, bz)));
        await Key(Godot.Key.Escape); // back to the Camera tool
    }

    /// <summary>
    /// <c>edit_stream_*</c>: one block placed every frame for <see cref="StreamEdits"/> frames, each in a different
    /// chunk column; for each edit, the frames (and ms) until every chunk it dirtied has swapped in its new mesh.
    /// The blocks are removed again afterwards.
    /// </summary>
    async Task MeasureEditStream()
    {
        var world = _scene.Map.World;
        const int s = VoxelWorld.ChunkSize;
        var pending = new List<(HashSet<Int3> Chunks, long Frame, long Start)>();
        var frames = new List<long>();
        double maxMs = 0;
        void OnApplied(IReadOnlyCollection<Int3> applied)
        {
            long now = (long)Engine.GetProcessFrames();
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                pending[i].Chunks.ExceptWith(applied);
                if (pending[i].Chunks.Count > 0) continue;
                frames.Add(now - pending[i].Frame);
                maxMs = Math.Max(maxMs, Stopwatch.GetElapsedTime(pending[i].Start).TotalMilliseconds);
                pending.RemoveAt(i);
            }
        }

        var placed = new List<(int X, int Y, int Z, ushort Old)>();
        int columns = world.ChunksX * world.ChunksZ;
        _scene.Chunks.EditsApplied += OnApplied;
        try
        {
            for (int i = 0; i < StreamEdits; i++)
            {
                // A stride coprime to the column count walks every column before repeating; chunk-column centers
                // keep each edit inside one horizontal chunk.
                int c = i * 7 % columns;
                int x = c % world.ChunksX * s + s / 2, z = c / world.ChunksX * s + s / 2;
                int y = world.GetSurfaceHeight(x, z) + 1 + i / columns;
                placed.Add((x, y, z, world.GetBlock(x, y, z)));
                world.SetBlock(x, y, z, Blocks.StoneBricks);
                pending.Add((DirtiedChunks(x, y, z), (long)Engine.GetProcessFrames(), Stopwatch.GetTimestamp()));
                await NextFrame();
            }
            for (int f = 0; f < StreamDrainFrames && pending.Count > 0; f++) await NextFrame();
        }
        finally
        {
            _scene.Chunks.EditsApplied -= OnApplied;
        }

        Metric("edit_stream", $"edits:{StreamEdits} applied:{frames.Count} " +
            string.Join(' ', frames.GroupBy(f => f).OrderBy(g => g.Key).Select(g => $"{g.Key}f:{g.Count()}")));
        Metric("edit_stream_frames_max", frames.Count == StreamEdits ? frames.Max() : -1);
        Metric("edit_stream_ms_max", maxMs);

        foreach (var (x, y, z, old) in placed) world.SetBlock(x, y, z, old);
        await WaitForRemesh();
    }

    /// <summary>The chunks a block change at (x, y, z) dirties: every chunk touching its 3×3×3 neighborhood.</summary>
    HashSet<Int3> DirtiedChunks(int x, int y, int z)
    {
        var world = _scene.Map.World;
        var chunks = new HashSet<Int3>();
        for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    if (world.InBounds(x + dx, y + dy, z + dz)) chunks.Add(VoxelWorld.ChunkOf(x + dx, y + dy, z + dz));
        return chunks;
    }

    /// <summary>
    /// <c>menu_leave_confirm</c>: with unsaved changes, Menu shows "Leave without saving?" instead of leaving, and
    /// Cancel keeps the map open (shot <c>menu_leave_confirm</c>).
    /// </summary>
    async Task CheckLeaveDialog()
    {
        var hud = _scene.Hud;
        bool unsaved = hud.HasUnsavedChanges;
        hud.MenuButton.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        bool shown = hud.LeaveDialog.Visible && _tree.CurrentScene == _scene;
        await Shoot("menu_leave_confirm");
        hud.LeaveDialog.GetCancelButton().EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        bool stays = !hud.LeaveDialog.Visible && _tree.CurrentScene == _scene;
        Metric("menu_leave_confirm", $"unsaved:{unsaved} dialog:{shown} cancel_stays:{stays} ok:{unsaved && shown && stays}");
    }
}
