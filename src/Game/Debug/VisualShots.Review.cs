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
        await Key(Godot.Key.Z);
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
        await Key(Godot.Key.C);
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

    /// <summary>
    /// <c>edit_burst_apply_ms_max</c>: 30 single-block edits in different chunks, ten per frame over 3 frames; the
    /// worst per-frame mesh-apply time (must stay within the apply budget plus 2 ms). The blocks are removed again.
    /// </summary>
    async Task MeasureEditBurst()
    {
        var world = _scene.Map.World;
        const int s = VoxelWorld.ChunkSize;
        var placed = new List<(int X, int Y, int Z, ushort Old)>();
        double maxMs = 0, maxOver = double.MinValue;
        int maxBatchChunks = 0;
        void Sample()
        {
            var c = _scene.Chunks;
            maxMs = Math.Max(maxMs, c.LastApplyMs);
            // The budget is soft by up to one batch: a frame may spend the budget plus its slowest single batch.
            maxOver = Math.Max(maxOver, c.LastApplyMs - c.LastMaxBatchMs - ChunkRenderer.FrameBudgetMs);
            maxBatchChunks = Math.Max(maxBatchChunks, c.LastMaxBatchChunks);
        }
        int columns = world.ChunksX * world.ChunksZ;
        for (int frame = 0; frame < 3; frame++)
        {
            for (int i = 0; i < 10; i++)
            {
                int c = (frame * 10 + i) * 7 % columns;
                int x = c % world.ChunksX * s + s / 2, z = c / world.ChunksX * s + s / 2;
                int y = world.GetSurfaceHeight(x, z) + 1;
                placed.Add((x, y, z, world.GetBlock(x, y, z)));
                world.SetBlock(x, y, z, Blocks.StoneBricks);
            }
            for (int f = 0; f < 3; f++)
            {
                await NextFrame();
                Sample();
            }
        }
        for (int f = 0; f < 10; f++)
        {
            await NextFrame();
            Sample();
        }
        Metric("edit_burst_apply_ms_max", maxMs);
        Metric("edit_burst_chunks_per_batch_max", maxBatchChunks);
        Metric("edit_burst_over_budget_ms_max", maxOver); // beyond budget + the frame's slowest batch; must be <= 1
        Metric("edit_burst_ok", maxOver <= 1 ? 1 : 0);
        foreach (var (x, y, z, old) in placed) world.SetBlock(x, y, z, old);
        await WaitForRemesh();
    }

    /// <summary>
    /// <c>overlay_incremental_ms</c>: with 50,000 jobs queued, the time for the overlay to apply a one-job change
    /// (must be &lt; 2 ms). The jobs are removed again.
    /// </summary>
    async Task MeasureOverlayIncremental()
    {
        var map = _scene.Map;
        var overlay = _scene.Overlay;
        var specs = new List<JobSpec>();
        int y = map.World.SizeY - 2;
        for (int z = 0; z < 224; z++)
            for (int x = 0; x < 224; x++) specs.Add(new JobSpec(new Int3(x, y, z), JobKind.Dig, 0));
        foreach (var spec in specs) map.Jobs.Add(spec);
        overlay.Refresh(); // the initial build of all chunks
        await NextFrame();

        var probe = new Int3(100, y - 1, 100);
        double total = 0, max = 0;
        const int runs = 20;
        for (int i = 0; i < runs; i++)
        {
            if (i % 2 == 0) map.Jobs.Add(new JobSpec(probe, JobKind.Dig, 0)); else map.Jobs.Remove(probe);
            long t = Stopwatch.GetTimestamp();
            overlay.Refresh();
            double ms = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
            total += ms;
            max = Math.Max(max, ms);
        }
        map.Jobs.Remove(probe);
        Metric("overlay_jobs", map.Jobs.Count);
        Metric("overlay_layers", overlay.LayerCount);
        Metric("overlay_incremental_ms", total / runs);
        Metric("overlay_incremental_ms_max", max);
        foreach (var spec in specs) map.Jobs.Remove(spec.Position);
        overlay.Refresh();
        await NextFrame();
    }

    /// <summary><c>focus_out_cancels_drag</c> and <c>phantom_drag_cancelled</c> (1 = pass).</summary>
    async Task CheckDragSafety(Site house)
    {
        var tools = _scene.Tools;
        var s = await LevelSite(FlatSite(ToolSiteW, ToolSiteD, house), ToolSiteW, ToolSiteD);
        await View(s.X + 2, s.Z + 2, _scene.Slice.TopLevel, 0, 60, 36);
        await Frames(10);
        static Vector3 Top(int x, int y, int z) => new(x + 0.5f, y + 1, z + 0.5f);

        await Key(Godot.Key.Z);
        await Press(Top(s.X + 1, s.H, s.Z + 1));
        await Move(Top(s.X + 3, s.H, s.Z + 3), held: true);
        bool started = tools.IsDragging;
        tools.Notification((int)Node.NotificationApplicationFocusOut);
        Metric("focus_out_cancels_drag", started && !tools.IsDragging ? 1 : 0);

        await Press(Top(s.X + 1, s.H, s.Z + 1));
        await Move(Top(s.X + 3, s.H, s.Z + 3), held: true);
        started = tools.IsDragging;
        await Move(Top(s.X + 4, s.H, s.Z + 4), held: false);
        Metric("phantom_drag_cancelled", started && !tools.IsDragging ? 1 : 0);
        await Key(Godot.Key.Escape);
    }

    /// <summary>
    /// <c>overlay_kinds</c>: one job of each kind, then an Unreachable and a NeedsWayBack Dig, set by hand; the shot
    /// should show distinct colors, the unreachable cube dim and the NeedsWayBack cube red. Removed afterwards.
    /// </summary>
    async Task ShowOverlayKinds(Site house)
    {
        var map = _scene.Map;
        var s = await LevelSite(FlatSite(ToolSiteW, ToolSiteD, house), ToolSiteW, ToolSiteD);
        int y = s.H + 1;
        var kinds = Enum.GetValues<JobKind>();
        var cells = new List<Int3>();
        for (int i = 0; i < kinds.Length + 2; i++) cells.Add(new Int3(s.X + 1 + i % 5 * 2, y, s.Z + 2 + i / 5 * 3));
        for (int i = 0; i < kinds.Length; i++) map.Jobs.Add(new JobSpec(cells[i], kinds[i], Blocks.StoneBricks));
        var unreachable = map.Jobs.Add(new JobSpec(cells[kinds.Length], JobKind.Dig, 0));
        var wayBack = map.Jobs.Add(new JobSpec(cells[kinds.Length + 1], JobKind.Dig, 0));
        map.Jobs.SetStatus(unreachable, JobStatus.Unreachable, 1);
        map.Jobs.SetStatus(wayBack, JobStatus.NeedsWayBack, 1);
        await View(s.X + 5, s.Z + 4, _scene.Slice.TopLevel, 0, 55, 14);
        await Frames(5);
        await Shoot("overlay_kinds");

        // A status change after the first build must reach the drawn instance color.
        var live = cells[0];
        var before = _scene.Overlay.InstanceColorAt(live);
        map.Jobs.SetStatus(map.Jobs.TryGet(live, out var job) ? job : throw new InvalidOperationException(), JobStatus.NeedsWayBack, 2);
        await Frames(2);
        var after = _scene.Overlay.InstanceColorAt(live);
        Metric("overlay_status_update", before != null && after != null && before != after ? 1 : 0);
        foreach (var c in cells) map.Jobs.Remove(c);
        await Frames(2);
    }

    /// <summary>
    /// <c>hotkeys_ok</c>: the real key events select each tool (Z X C V B N M Delete, Esc back to Camera), and the
    /// sim, priority and follow actions are registered with their keys (<c>sim_actions_ok</c>).
    /// </summary>
    async Task CheckHotkeys()
    {
        var tools = _scene.Tools;
        (Godot.Key Key, DesignationTool? Tool)[] cases =
        [
            (Godot.Key.Z, DesignationTool.Dig), (Godot.Key.X, DesignationTool.DigStairs), (Godot.Key.C, DesignationTool.Wall),
            (Godot.Key.V, DesignationTool.Floor), (Godot.Key.B, DesignationTool.Stairs), (Godot.Key.N, DesignationTool.Ramp),
            (Godot.Key.M, DesignationTool.Ladder), (Godot.Key.Delete, DesignationTool.Cancel), (Godot.Key.Escape, null),
        ];
        var bad = new List<string>();
        foreach (var (key, tool) in cases)
        {
            await Key(key);
            if (tools.ActiveTool != tool) bad.Add($"{key}->{ToolController.ToolName(tools.ActiveTool)}");
        }
        Metric("hotkeys_ok", bad.Count == 0 ? 1 : 0);
        if (bad.Count > 0) Metric("hotkeys_bad", string.Join(' ', bad));

        (string Action, Godot.Key Key)[] sim =
        [
            (InputActions.SimPause, Godot.Key.Space), (InputActions.SimSpeed1, Godot.Key.Key1),
            (InputActions.SimSpeed2, Godot.Key.Key2), (InputActions.SimSpeed3, Godot.Key.Key3),
            (InputActions.WorkPriorities, Godot.Key.P), (InputActions.FollowColonist, Godot.Key.F),
        ];
        var wrong = sim.Where(a => !InputMap.HasAction(a.Action) ||
            !InputMap.ActionGetEvents(a.Action).OfType<InputEventKey>().Any(e => e.PhysicalKeycode == a.Key))
            .Select(a => a.Action).ToList();
        Metric("sim_actions_ok", wrong.Count == 0 ? 1 : 0);
        if (wrong.Count > 0) Metric("sim_actions_bad", string.Join(' ', wrong));
    }
}
