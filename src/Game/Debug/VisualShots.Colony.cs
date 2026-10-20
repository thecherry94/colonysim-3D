using System.Diagnostics;
using ColonySim.Core;
using Godot;

namespace ColonySim.Game;

/// <summary>
/// Step 2a colony checks: the three starting colonists, a dig worked by them, hiding above the view level, the time
/// controls and the colonist UI; then, last, the spec's "done when" for 2a (a mine, a ladder shaft, a ledge climb, skill
/// growth) and, on Large, the bulk-spawn benchmark.
/// </summary>
public partial class VisualShots
{
    const int DigW = 10, DigD = 10, DigLayers = 3, DigGap = 7;
    const int DigSeconds = 30;

    /// <summary>Scenarios of this file, in run order. They run on the bare start site and leave the simulation paused with no jobs queued.</summary>
    List<(string? Name, Func<Task> Setup)> ColonyScenarios()
    {
        var site = _scene.Map.StartSite;
        // The dig starts just past the flat core of the start site, three layers down from the clearing's top block.
        var dig = new DragBox(
            new Int3(site.X + DigGap, site.Level - 1, site.Z - DigD / 2),
            new Int3(site.X + DigGap + DigW - 1, site.Level - DigLayers, site.Z + DigD / 2 - 1));
        return
        [
            ("done_start_site", () => DoneStartSite(site)),
            ("colonists_idle", () => ColonistsIdle(site)),
            (null, CheckSpeedControls),
            ("colonists_digging", () => ColonistsDigging(site, dig)),
            (null, () => ColonistsSliced(site, dig)),
            (null, () => CheckSelectByClick(site)),
            (null, () => CheckFollow(site)),
            ("colonist_panel", () => ColonistPanelView(site)),
            (null, CheckBookmarks),
            (null, CheckPriorities),
            ("work_priorities", WorkPrioritiesView),
            (null, ColonistUiCleanup),
        ];
    }

    /// <summary>Warms the simulation up (the first tick applies every edit earlier scenarios made to navigation) and pauses it.</summary>
    void PrepareSim()
    {
        var runner = _scene.SimRunner;
        runner.Speed = 0;
        runner.Sim.Tick();
    }

    Task ColonistsIdle(StartSite site)
    {
        PrepareSim();
        Metric("colonists_count", _scene.Map.Colony.Colonists.Count);
        return View(site.X, site.Z, _scene.Slice.TopLevel, yaw: 30, pitch: 35, distance: 14);
    }

    /// <summary>
    /// <c>speed_controls_ok</c>: Space pauses (no tick for a second), then 3 runs at 3× (60 ± 6 ticks per second over
    /// two seconds), counted through <c>Colony.Tick</c>. Leaves the simulation paused.
    /// </summary>
    async Task CheckSpeedControls()
    {
        var runner = _scene.SimRunner;
        var colony = _scene.Map.Colony;
        runner.Speed = 1;
        await Key(Godot.Key.Space);
        bool paused = runner.Speed == 0;
        long before = colony.Tick;
        await Seconds(1);
        long pausedTicks = colony.Tick - before;

        await Key(Godot.Key.Key3);
        bool three = runner.Speed == 3;
        before = colony.Tick;
        long start = Stopwatch.GetTimestamp();
        await Seconds(2);
        double rate = (colony.Tick - before) / Stopwatch.GetElapsedTime(start).TotalSeconds;
        bool ok = paused && pausedTicks == 0 && three && Math.Abs(rate - 3 * SimClock.TicksPerSecond) <= 6;
        Metric("speed_controls_ok", ok ? 1 : 0);
        Metric("speed_controls", $"paused:{paused} ticks_while_paused:{pausedTicks} speed3:{three} ticks_per_s_at_3x:{rate:0.#}");
        runner.Speed = 0;
    }

    async Task Seconds(double seconds)
    {
        long start = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(start).TotalSeconds < seconds) await NextFrame();
    }

    /// <summary>
    /// A <see cref="DigW"/>×<see cref="DigD"/>×<see cref="DigLayers"/> dig worked by the colonists at 3× for
    /// <see cref="DigSeconds"/> simulated seconds. <c>colonists_digging_blocks_done</c> counts the blocks removed,
    /// <c>sim_tick_ms_avg</c> is the runner's average tick time over the last two seconds of it.
    /// </summary>
    async Task ColonistsDigging(StartSite site, DragBox dig)
    {
        var map = _scene.Map;
        var runner = _scene.SimRunner;
        int queued = Designator.Apply(map, DesignationTool.Dig, BuildMaterial.Wood, dig);
        Metric("colonists_digging_queued", queued);
        long endTick = map.Colony.Tick + (long)DigSeconds * SimClock.TicksPerSecond;
        runner.Speed = 3;
        double tickMsSum = 0;
        int samples = 0;
        while (map.Colony.Tick < endTick)
        {
            await NextFrame();
            if (endTick - map.Colony.Tick < 2 * 3 * SimClock.TicksPerSecond) { tickMsSum += runner.AvgTickMs; samples++; }
        }
        runner.Speed = 0;
        int left = map.Jobs.Count;
        Metric("colonists_digging_blocks_done", queued - left);
        Metric("sim_tick_ms_avg", samples > 0 ? tickMsSum / samples : runner.AvgTickMs);
        Metric("sim_tick_ms_last", runner.LastTickMs);
        Metric("colonists_digging_activities", string.Join(' ', map.Colony.Colonists.Select(c => $"{c.Name}:{c.Activity}@{c.Cell.X},{c.Cell.Y},{c.Cell.Z}")));
        await WaitForRemesh();
        await View(site.X + DigGap + 1, site.Z - 2, _scene.Slice.TopLevel, yaw: 20, pitch: 50, distance: 14);
    }

    /// <summary>
    /// The view level at the dig's middle layer: colonists standing above it are hidden, the ones below it drawn whole.
    /// Metrics <c>colonists_sliced_visible</c> and <c>colonists_sliced_hidden</c>.
    /// </summary>
    async Task ColonistsSliced(StartSite site, DragBox dig)
    {
        int middle = site.Level - 2;
        // Colonists are still on the ground at the pit's edge only if they haven't started: land one there, above the cut.
        _scene.SimRunner.Sim.Spawn(new Int3(site.X, site.Level, site.Z));
        await View(site.X + DigGap + 1, site.Z - 2, middle, yaw: 20, pitch: 55, distance: 14);
        await Frames(3);
        var renderer = _scene.ColonistRenderer;
        var colonists = _scene.Map.Colony.Colonists;
        int visible = colonists.Count(renderer.IsVisible);
        Metric("colonists_sliced_visible", visible);
        Metric("colonists_sliced_hidden", colonists.Count - visible);
        Metric("colonists_sliced_level", middle);
        await Shoot("colonists_sliced");
        // Leave the map as the later scenarios expect it: no colonist jobs queued, the full view.
        Designator.Apply(_scene.Map, DesignationTool.Cancel, BuildMaterial.Wood, dig);
        _scene.Slice.SetLevel(_scene.Slice.TopLevel);
    }

    // A dig south-east of the first one, for the colonist UI checks: tall enough to cover whatever the ground does there.
    DragBox UiDig(StartSite site) => new(
        new Int3(site.X + DigGap, site.Level + 4, site.Z + 6), new Int3(site.X + DigGap + 5, site.Level - 2, site.Z + 11));

    /// <summary>The world point at a colonist's chest, where the harness aims its clicks.</summary>
    Vector3 ChestOf(Colonist c) => _scene.ColonistRenderer.RenderPosition(c) + new Vector3(0, 0.9f, 0);

    /// <summary>
    /// <c>select_by_click</c>: with the Camera tool, a click on a colonist's screen position (the first one in plain sight)
    /// selects it without moving the camera, and a click on empty ground clears the selection.
    /// </summary>
    async Task CheckSelectByClick(StartSite site)
    {
        PrepareSim();
        _scene.Tools.ActiveTool = null;
        _scene.Selected = null;
        // A colonist the camera sees: from this angle, one standing in a pit can be hidden behind its rim.
        Colonist? colonist = null;
        var feet = Vector3.Zero;
        foreach (var c in _scene.Map.Colony.Colonists)
        {
            feet = _scene.ColonistRenderer.RenderPosition(c);
            await View((int)feet.X, (int)feet.Z, _scene.Slice.TopLevel, yaw: 30, pitch: 50, distance: 16);
            await Frames(3);
            colonist = c;
            if (InSight(ChestOf(c))) break;
        }
        if (colonist == null) throw new InvalidOperationException("no colonists to select");
        var focusBefore = _scene.Rig.FocusPoint;
        await Press(ChestOf(colonist));
        await Release(ChestOf(colonist));
        bool selected = _scene.Selected == colonist;
        bool cameraStill = _scene.Rig.FocusPoint.DistanceTo(focusBefore) < 0.01f;

        // Empty ground: a spot on the ground surface well away from every colonist.
        var spot = new Vector3(feet.X + 4.5f, _scene.Map.World.GetSurfaceHeight((int)feet.X + 4, (int)feet.Z + 4) + 1, feet.Z + 4.5f);
        await Press(spot);
        await Release(spot);
        bool cleared = _scene.Selected == null;
        Metric("select_by_click", $"selected:{selected} camera_still:{cameraStill} cleared:{cleared}");
        Metric("select_by_click_ok", selected && cameraStill && cleared ? 1 : 0);
    }

    /// <summary>No block (as the player sees them) lies between the camera and <paramref name="point"/>.</summary>
    bool InSight(Vector3 point)
    {
        var origin = _scene.Rig.Camera.GlobalPosition;
        var dir = (point - origin).Normalized();
        float distance = origin.DistanceTo(point);
        return !VoxelRaycast.Cast(_scene.Map.View, new System.Numerics.Vector3(origin.X, origin.Y, origin.Z),
            new System.Numerics.Vector3(dir.X, dir.Y, dir.Z), distance, _scene.Slice.ViewLevel, out _);
    }

    /// <summary>
    /// <c>follow_tracks</c>: a dig is queued, the first colonist selected and followed; after 2 s of running at 3× the camera
    /// focus is within a block of it, and it did walk. <c>follow_stops_on_pan</c>: holding W ends the following.
    /// <c>follow_level</c>: following in a slice puts the view level on the colonist's floor.
    /// </summary>
    async Task CheckFollow(StartSite site)
    {
        var map = _scene.Map;
        var runner = _scene.SimRunner;
        var colonist = map.Colony.Colonists[0];
        int queued = Designator.Apply(map, DesignationTool.Dig, BuildMaterial.Wood, UiDig(site));
        Metric("follow_dig_queued", queued);
        _scene.Selected = colonist;
        _scene.Following = true;
        var start = _scene.ColonistRenderer.RenderPosition(colonist);
        runner.Speed = 3;
        float walked = 0, worst = 0;
        long t0 = Stopwatch.GetTimestamp();
        var last = start;
        while (Stopwatch.GetElapsedTime(t0).TotalSeconds < 2)
        {
            await NextFrame();
            var now = _scene.ColonistRenderer.RenderPosition(colonist);
            walked += now.DistanceTo(last);
            last = now;
        }
        runner.Speed = 0;
        await Frames(2);
        var feet = _scene.ColonistRenderer.RenderPosition(colonist);
        var focus = _scene.Rig.FocusPoint;
        worst = new Vector2(focus.X - feet.X, focus.Z - feet.Z).Length();
        Metric("follow_tracks", $"focus_distance:{worst:0.##} walked:{walked:0.#} activity:{colonist.Activity}");
        Metric("follow_tracks_ok", worst <= 1 && walked >= 2 && _scene.Rig.IsFollowing ? 1 : 0);

        // Sliced: the view level goes to the floor the colonist stands on.
        _scene.Slice.SetLevel(colonist.Cell.Y + 3);
        await Frames(3);
        Metric("follow_level", $"level:{_scene.Slice.ViewLevel} floor:{colonist.Cell.Y - 1}");
        Metric("follow_level_ok", _scene.Slice.ViewLevel == colonist.Cell.Y - 1 ? 1 : 0);
        _scene.Slice.SetLevel(_scene.Slice.TopLevel);

        // Panning with the keys stops following.
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Godot.Key.W, Keycode = Godot.Key.W, Pressed = true });
        await Frames(3);
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Godot.Key.W, Keycode = Godot.Key.W, Pressed = false });
        await Frames(2);
        Metric("follow_stops_on_pan", _scene.Rig.IsFollowing ? 0 : 1);
    }

    /// <summary>
    /// The panel and the bar with a working colonist selected: runs at 3× until one is digging (at most 20 s), pauses, and
    /// shoots a close view of it.
    /// </summary>
    async Task ColonistPanelView(StartSite site)
    {
        var colonists = _scene.Map.Colony.Colonists;
        _scene.SimRunner.Speed = 3;
        long t0 = Stopwatch.GetTimestamp();
        Colonist? worker = null;
        while (worker == null && Stopwatch.GetElapsedTime(t0).TotalSeconds < 20)
        {
            await NextFrame();
            worker = colonists.FirstOrDefault(c => c.Activity == ColonistActivity.Working);
        }
        _scene.SimRunner.Speed = 0;
        worker ??= colonists[0];
        _scene.Selected = worker;
        var feet = _scene.ColonistRenderer.RenderPosition(worker);
        await View((int)feet.X, (int)feet.Z, _scene.Slice.TopLevel, yaw: 30, pitch: 45, distance: 18);
        await Frames(2);
        var panel = _scene.Hud.ColonistPanel;
        Metric("colonist_panel", $"visible:{panel.Visible} activity:{ColonistPanel.ActivityText(_scene.Map, worker)}");
    }

    /// <summary>Sends a click (motion, press, release) at a window position; <see cref="ClickControl"/> converts from canvas coordinates.</summary>
    async Task ClickAt(Vector2 p, MouseButton button = MouseButton.Left)
    {
        await Send(new InputEventMouseMotion { Position = p, GlobalPosition = p });
        var mask = button == MouseButton.Left ? MouseButtonMask.Left : MouseButtonMask.Right;
        await Send(new InputEventMouseButton { ButtonIndex = button, Pressed = true, ButtonMask = mask, Position = p, GlobalPosition = p });
        await Send(new InputEventMouseButton { ButtonIndex = button, Pressed = false, Position = p, GlobalPosition = p });
    }

    Task ClickControl(Control control, MouseButton button = MouseButton.Left) => ClickAt(GetViewport().GetFinalTransform() * control.GetGlobalRect().GetCenter(), button);

    /// <summary><c>bookmark_roundtrip</c>: add (named "Level 100" by default), jump back to it from another level, delete.</summary>
    async Task CheckBookmarks()
    {
        var bookmarks = _scene.Map.Colony.Bookmarks;
        var panel = _scene.Hud.Bookmarks;
        bookmarks.Clear();
        _scene.Slice.SetLevel(100);
        await ClickControl(panel.AddButton);
        bool added = bookmarks.Count == 1 && bookmarks[0] == new ViewBookmark("Level 100", 100);
        await Frames(2);
        _scene.Slice.SetLevel(150);
        await Frames(2);
        var row = panel.List.GetChildCount() > 0 ? (HBoxContainer)panel.List.GetChild(0) : null;
        if (row != null) await ClickControl((Control)row.GetChild(0));
        bool jumped = _scene.Slice.ViewLevel == 100;
        if (row != null) await ClickControl((Control)row.GetChild(1));
        bool deleted = bookmarks.Count == 0;
        await Frames(2);
        bool listEmpty = panel.List.GetChildCount() == 0;
        // The name field gives focus up on Enter, so hotkeys work again.
        panel.NameEdit.GrabFocus();
        await Frames(2);
        bool focused = panel.NameEdit.HasFocus();
        await Key(Godot.Key.Enter);
        bool released = !panel.NameEdit.HasFocus();
        Metric("bookmark_roundtrip", $"added:{added} jumped:{jumped} deleted:{deleted} list_empty:{listEmpty} focus_taken:{focused} focus_released_on_enter:{released}");
        Metric("bookmark_roundtrip_ok", added && jumped && deleted && listEmpty && focused && released ? 1 : 0);
        _scene.Slice.SetLevel(_scene.Slice.TopLevel);
    }

    /// <summary><c>priorities_cycle</c>: P opens the window; a left click raises a cell's number through 1-4 and off, a right click goes back.</summary>
    async Task CheckPriorities()
    {
        var window = _scene.Hud.WorkPriorities;
        var colonist = _scene.Map.Colony.Colonists[0];
        colonist.Priorities[0] = 1;
        await Key(Godot.Key.P);
        await Frames(3);
        bool opened = window.Visible;
        var cell = window.CellButtons.FirstOrDefault();
        var seen = new List<int>();
        if (cell != null)
        {
            for (int i = 0; i < 5; i++) { await ClickControl(cell); seen.Add(colonist.Priorities[0]); }
            await ClickControl(cell, MouseButton.Right);
            seen.Add(colonist.Priorities[0]);
        }
        int[] expected = [2, 3, 4, 0, 1, 0];
        bool ok = opened && seen.SequenceEqual(expected);
        Metric("priorities_cycle", $"opened:{opened} sequence:{string.Join(',', seen)}");
        Metric("priorities_cycle_ok", ok ? 1 : 0);
        colonist.Priorities[0] = 3;
        await Key(Godot.Key.P);
        Metric("priorities_closed_by_key", window.Visible ? 0 : 1);
    }

    /// <summary>The work priorities window over the colony: a few cells off or at other numbers, so the tints and numbers show.</summary>
    async Task WorkPrioritiesView()
    {
        var colonists = _scene.Map.Colony.Colonists;
        for (int i = 0; i < colonists.Count; i++)
        {
            colonists[i].Priorities[0] = (i % 3) switch { 0 => 1, 1 => 2, _ => 0 };
            colonists[i].Priorities[1] = (i % 3) switch { 0 => 3, 1 => 1, _ => 4 };
        }
        await Key(Godot.Key.P);
        await Frames(3);
    }

    /// <summary>Closes the window, drops the selection and the jobs, and restores the defaults the later scenarios expect.</summary>
    async Task ColonistUiCleanup()
    {
        if (_scene.Hud.WorkPriorities.Visible) await Key(Godot.Key.P);
        foreach (var c in _scene.Map.Colony.Colonists) Array.Fill(c.Priorities, 3); // the factory's default
        _scene.Following = false;
        _scene.Selected = null;
        Designator.Apply(_scene.Map, DesignationTool.Cancel, BuildMaterial.Wood, UiDig(_scene.Map.StartSite));
        _scene.Slice.SetLevel(_scene.Slice.TopLevel);
        _scene.SimRunner.Speed = 0;
    }

    // ── Step 2a "done when" (spec items 1–4) ──────────────────────────────────────────────────────────────────────────

    const int StartingColonists = 3;
    const int DoneTimeoutSeconds = 10 * 60;
    const int GroundSearchRadius = 64;
    // The mine: a carved staircase 5 steps down East into a 4×3 room (2 high), and from its far side 4 more steps down
    // into a second room: 9 levels from the surface to the lower room's floor.
    const int MineRun1 = 5, MineRun2 = 4, MineRoomW = 4, MineW = MineRun1 + MineRoomW + MineRun2 + MineRoomW, MineD = 3;
    const int ShaftDepth = 6, LedgeHeight = 3;

    /// <summary>Ground the "done when" scenarios have claimed, so each finds its own.</summary>
    readonly List<Rect2I> _doneGround = new();

    /// <summary>The "done when" scenarios, then the bulk spawn (F9) and the benchmark (Large only).</summary>
    List<(string? Name, Func<Task> Setup)> DoneWhenScenarios()
    {
        var site = _scene.Map.StartSite;
        int top = 0, ex = 0, ez = 0;
        return
        [
            (null, async () => (ex, top, ez) = await DoneMine(site)),
            ("done_mine", () => MineView(ex, ez, top - 3)),
            ("done_mine_lower", () => MineView(ex, ez, top - 7)),
            (null, () => DoneLadderShaft(site)),
            (null, () => DoneOutdoorClimb(site)),
            (null, CheckSpawnKey),
            .. BenchScenarios(site),
        ];
    }

    /// <summary>
    /// Spec 2a item 1, <c>done_start_site</c>: the bare start site with the three starting colonists. <c>_ok</c> when
    /// there are 3 and the core (<see cref="StartSiteFinder.CoreRadius"/>) is level; also counts the slope blocks within
    /// <see cref="StartSiteFinder.BlendRadius"/> × 2.
    /// </summary>
    Task DoneStartSite(StartSite site)
    {
        var w = _scene.Map.World;
        int lo = int.MaxValue, hi = int.MinValue, slopes = 0;
        const int core = StartSiteFinder.CoreRadius, around = StartSiteFinder.BlendRadius * 2;
        for (int dz = -around; dz <= around; dz++)
            for (int dx = -around; dx <= around; dx++)
            {
                int x = site.X + dx, z = site.Z + dz, h = w.GetSurfaceHeight(x, z);
                if (Blocks.Registry.Get(w.GetBlock(x, h + 1, z)).IsSlope) slopes++;
                if (dx * dx + dz * dz > core * core) continue;
                lo = Math.Min(lo, h);
                hi = Math.Max(hi, h);
            }
        int count = _scene.Map.Colony.Colonists.Count;
        Metric("done_start_site", $"colonists:{count} core_range:{hi - lo} slopes_nearby:{slopes}");
        Metric("done_start_site_ok", count == StartingColonists && hi == lo ? 1 : 0);
        return View(site.X, site.Z, _scene.Slice.TopLevel, yaw: 25, pitch: 40, distance: 24);
    }

    /// <summary>
    /// Spec 2a items 2 and 3: a two-room mine 9 levels deep, reached by carved stairs (Dig stairs), dug by the three
    /// starting colonists at 3× (any later colonist's work is off meanwhile) within 10 simulated minutes.
    /// <list type="bullet">
    /// <item><c>done_mine</c>: 1 when every job is done in time, both staircases stand and a colonist reached the lower room.</item>
    /// <item><c>done_never_stranded</c>: 1 when no colonist was ever stuck, checked every frame of the run.</item>
    /// <item><c>done_skill_growth</c>: 1 when a starting colonist's Mining level rose.</item>
    /// </list>
    /// Returns the entrance (x, ground y, z) for the shots.
    /// </summary>
    async Task<(int X, int Top, int Z)> DoneMine(StartSite site)
    {
        PrepareSim();
        var map = _scene.Map;
        var runner = _scene.SimRunner;
        var colonists = map.Colony.Colonists;
        // Solid down to under the lower room's floor: a cave in the way could leave cells with nowhere to stand to dig them.
        var g = FindGround(site, MineW + 1, MineD, tolerance: 1, bare: false, solidDepth: MineRun1 + MineRun2 + 2);
        int ex = g.X, ez = g.Z + 1, top = map.World.GetSurfaceHeight(ex, ez);

        // Rooms are 2 high with their floor one below the last step's feet; the second staircase starts beside the first room.
        int roomA = ex + MineRun1, run2 = roomA + MineRoomW, roomB = run2 + MineRun2;
        int floorA = top - MineRun1 + 1, floorB = floorA - MineRun2; // feet level of each room
        int queued = 0;
        queued += Designator.Apply(map, DesignationTool.DigStairs, BuildMaterial.Wood, new DragBox(new(ex, top, ez), new(ex + MineRun1 - 1, top, ez)));
        queued += Designator.Apply(map, DesignationTool.Dig, BuildMaterial.Wood, new DragBox(new(roomA, floorA, ez - 1), new(roomA + MineRoomW - 1, floorA + 1, ez + 1)));
        queued += Designator.Apply(map, DesignationTool.DigStairs, BuildMaterial.Wood, new DragBox(new(run2, floorA - 1, ez), new(run2 + MineRun2 - 1, floorA - 1, ez)));
        queued += Designator.Apply(map, DesignationTool.Dig, BuildMaterial.Wood, new DragBox(new(roomB, floorB, ez - 1), new(roomB + MineRoomW - 1, floorB + 1, ez + 1)));

        var starters = colonists.Take(StartingColonists).ToList();
        int[] before = starters.Select(c => c.Skills[(int)SkillType.Mining].Level).ToArray();
        var resting = RestOthers(starters);
        long start = map.Colony.Tick, deadline = start + (long)DoneTimeoutSeconds * SimClock.TicksPerSecond;
        var stuck = new List<string>();
        int deepest = int.MaxValue;
        runner.Speed = 3;
        while (map.Jobs.Count > 0 && map.Colony.Tick < deadline)
        {
            await NextFrame();
            foreach (var c in colonists)
            {
                if (c.Stuck && !stuck.Any(s => s.StartsWith(c.Name + "@"))) stuck.Add($"{c.Name}@{c.Cell.X},{c.Cell.Y},{c.Cell.Z}:tick{map.Colony.Tick - start}");
                if (starters.Contains(c)) deepest = Math.Min(deepest, c.Cell.Y);
            }
        }
        runner.Speed = 0;
        Restore(resting);
        int left = map.Jobs.Count;
        double simSeconds = (map.Colony.Tick - start) / (double)SimClock.TicksPerSecond;

        int stairs = 0;
        for (int i = 0; i < MineRun1; i++) stairs += IsStairs(ex + i, top - i, ez) ? 1 : 0;
        for (int i = 0; i < MineRun2; i++) stairs += IsStairs(run2 + i, floorA - 1 - i, ez) ? 1 : 0;
        int levels = top + 1 - deepest;
        bool ok = left == 0 && stairs == MineRun1 + MineRun2 && deepest <= floorB;
        Metric("done_mine", ok ? 1 : 0);
        Metric("done_mine_detail", $"entrance:({ex},{top},{ez}) jobs:{queued} left:{left} sim_s:{simSeconds:0} " +
            $"stairs:{stairs}/{MineRun1 + MineRun2} levels_down:{levels}/{top + 1 - floorB} " +
            $"left_jobs:[{string.Join(' ', map.Jobs.All.Take(8).Select(j => $"{j.Kind}@{j.Position.X},{j.Position.Y},{j.Position.Z}:{j.Status}"))}]");
        Metric("done_never_stranded", stuck.Count == 0 ? 1 : 0);
        if (stuck.Count > 0) Metric("done_stranded", string.Join(' ', stuck));
        int[] after = starters.Select(c => c.Skills[(int)SkillType.Mining].Level).ToArray();
        Metric("done_skill_growth", after.Zip(before).Any(p => p.First > p.Second) ? 1 : 0);
        Metric("done_skill_mining", string.Join(' ', starters.Select((c, i) => $"{c.Name}:{before[i]}->{after[i]}")));
        Designator.Apply(map, DesignationTool.Cancel, BuildMaterial.Wood, new DragBox(new(ex, top + 2, ez - 1), new(roomB + MineRoomW, floorB - 1, ez + 1)));
        await WaitForRemesh();
        return (ex, top, ez);

        bool IsStairs(int x, int y, int z) => Blocks.Registry.Get(map.World.GetBlock(x, y, z)).Shape == BlockShape.Stairs;
    }

    /// <summary>The mine from above, sliced at <paramref name="level"/>.</summary>
    async Task MineView(int ex, int ez, int level)
    {
        await View(ex + MineW / 2, ez, level, yaw: 20, pitch: 55, distance: 16);
        await Frames(3);
    }

    /// <summary>
    /// <c>done_ladder_shaft</c>: a 1×1 shaft <see cref="ShaftDepth"/> deep, laddered all the way (both by instant work),
    /// with a new colonist at its bottom and a dig waiting on the surface: 1 when it climbs out by ladder and does it.
    /// </summary>
    async Task DoneLadderShaft(StartSite site)
    {
        var map = _scene.Map;
        var g = FindGround(site, 6, 3, tolerance: 0, bare: true, solidDepth: ShaftDepth + 1);
        int sx = g.X + 4, sz = g.Z + 1, top = g.Y;
        Designator.Apply(map, DesignationTool.Dig, BuildMaterial.Wood, new DragBox(new(sx, top, sz), new(sx, top - ShaftDepth + 1, sz)));
        await RunInstantWork();
        int ladders = Designator.Apply(map, DesignationTool.Ladder, BuildMaterial.Wood, new DragBox(new(sx, top, sz), new(sx, top - ShaftDepth + 1, sz)));
        await RunInstantWork();

        var bottom = new Int3(sx, top - ShaftDepth + 1, sz);
        var run = await RunAlone(bottom, new DragBox(new(g.X, top, sz), new(g.X, top, sz)));
        // The last rung may be left by a hop onto the rim instead: every other one is climbed.
        bool ok = run.Done && run.Ladder >= ShaftDepth - 1 && run.Out && run.Stuck == 0;
        Metric("done_ladder_shaft", ok ? 1 : 0);
        Metric("done_ladder_shaft_detail", $"shaft:({sx},{top},{sz}) depth:{ShaftDepth} ladders:{ladders} start:{At(run.Start)} " +
            $"ladder_moves:{run.Ladder} out:{run.Out} job_done:{run.Done} stuck_frames:{run.Stuck} sim_s:{run.Seconds:0.#} moves:{run.Kinds}");
    }

    /// <summary>
    /// <c>done_outdoor_climb</c>: a 3×3 pit <see cref="LedgeHeight"/> deep with a new colonist at its bottom and a dig
    /// waiting on the surface west of it. With a carved staircase up the west side it must walk out by the stairs, never
    /// climbing or hopping; in a second such pit without stairs it must climb the 3-block face.
    /// </summary>
    async Task DoneOutdoorClimb(StartSite site)
    {
        var withStairs = await ClimbPit(site, stairs: true);
        var without = await ClimbPit(site, stairs: false);
        bool stairsOk = withStairs.Done && withStairs.Out && withStairs.Climb == 0 && !withStairs.Kinds.Contains(nameof(MoveKind.Hop));
        bool climbOk = without.Done && without.Out && without.Climb == LedgeHeight;
        Metric("done_outdoor_climb", stairsOk && climbOk && withStairs.Stuck + without.Stuck == 0 ? 1 : 0);
        Metric("done_outdoor_climb_detail",
            $"with_stairs:[start:{At(withStairs.Start)} out:{withStairs.Out} job_done:{withStairs.Done} moves:{withStairs.Kinds}] " +
            $"without:[start:{At(without.Start)} out:{without.Out} job_done:{without.Done} climb_height:{without.Climb} moves:{without.Kinds}]");
    }

    /// <summary>
    /// Digs (by instant work) a pit with, when <paramref name="stairs"/>, a 3-step carved staircase up its west side, then
    /// runs a new colonist from the pit's middle to a dig past the stairs' top (<see cref="RunAlone"/>).
    /// </summary>
    async Task<SoloRun> ClimbPit(StartSite site, bool stairs)
    {
        var map = _scene.Map;
        // West to east: the dig in the second column, two columns, the stairs' three, then the pit's three.
        var g = FindGround(site, 9, 3, tolerance: 0, bare: true, solidDepth: LedgeHeight + 1);
        int px = g.X + 6, pz = g.Z, top = g.Y;
        Designator.Apply(map, DesignationTool.Dig, BuildMaterial.Wood, new DragBox(new(px, top, pz), new(px + 2, top - LedgeHeight + 1, pz + 2)));
        if (stairs)
            Designator.Apply(map, DesignationTool.DigStairs, BuildMaterial.Wood, new DragBox(new(px - 3, top, pz + 1), new(px - 1, top, pz + 1)));
        await RunInstantWork();
        return await RunAlone(new Int3(px + 1, top - LedgeHeight + 1, pz + 1), new DragBox(new(g.X + 1, top, pz + 1), new(g.X + 1, top, pz + 1)));
    }

    static string At(Int3 c) => $"({c.X},{c.Y},{c.Z})";

    /// <summary>What a colonist did in <see cref="RunAlone"/>.</summary>
    readonly record struct SoloRun(Int3 Start, bool Done, bool Out, int Ladder, int Climb, int Stuck, double Seconds, string Kinds);

    /// <summary>
    /// Spawns a colonist at <paramref name="at"/>, digs <paramref name="target"/> (a surface block) with only it at work,
    /// at 3× for up to a simulated minute. Reports the moves it made (ladder moves, the highest climb), whether it ended
    /// above its start (out), finished the dig and was ever stuck.
    /// </summary>
    async Task<SoloRun> RunAlone(Int3 at, DragBox target)
    {
        PrepareSim();
        var map = _scene.Map;
        var runner = _scene.SimRunner;
        var c = runner.Sim.Spawn(at);
        var spawnedAt = c.Cell;
        var resting = RestOthers([c]);
        Designator.Apply(map, DesignationTool.Dig, BuildMaterial.Wood, target);
        long start = map.Colony.Tick, deadline = start + 60 * SimClock.TicksPerSecond;
        var kinds = new SortedSet<string>();
        var ladderMoves = new HashSet<(Int3, Int3)>();
        int climb = 0, stuck = 0;
        runner.Speed = 3;
        while (map.Jobs.Count > 0 && map.Colony.Tick < deadline)
        {
            await NextFrame();
            if (c.Stuck) stuck++;
            if (c.Motion is not { } m) continue;
            kinds.Add(m.Kind.ToString());
            if (m.Kind == MoveKind.Ladder && m.To.Y > m.From.Y) ladderMoves.Add((m.From, m.To));
            if (m.Kind == MoveKind.Climb) climb = Math.Max(climb, m.To.Y - m.From.Y);
        }
        runner.Speed = 0;
        Restore(resting);
        bool done = map.Jobs.Count == 0;
        Designator.Apply(map, DesignationTool.Cancel, BuildMaterial.Wood, target);
        return new SoloRun(spawnedAt, done, c.Cell.Y >= target.Start.Y + 1, ladderMoves.Count, climb, stuck,
            (map.Colony.Tick - start) / (double)SimClock.TicksPerSecond, string.Join(',', kinds));
    }

    /// <summary>Turns off the work of every colonist but <paramref name="working"/>; returns what to <see cref="Restore"/>.</summary>
    List<(Colonist Colonist, int[] Priorities)> RestOthers(IReadOnlyCollection<Colonist> working)
    {
        var resting = _scene.Map.Colony.Colonists.Where(c => !working.Contains(c)).Select(c => (c, c.Priorities.ToArray())).ToList();
        foreach (var (c, _) in resting) Array.Fill(c.Priorities, 0);
        return resting;
    }

    static void Restore(List<(Colonist Colonist, int[] Priorities)> resting)
    {
        foreach (var (c, priorities) in resting) priorities.CopyTo(c.Priorities, 0);
    }

    /// <summary>
    /// The w×d patch of ground nearest the start site (within <see cref="GroundSearchRadius"/>) whose columns, with a
    /// one-column rim, are natural and solid on top (and, inside the patch, <paramref name="solidDepth"/> blocks further down: no cave),
    /// within <paramref name="tolerance"/> of the patch corner's height, open for three cells above (a natural slope block
    /// allowed on top unless <paramref name="bare"/>), on home's component and clear of colonists and of the ground earlier
    /// "done when" scenarios took. Returns its min corner at the corner's ground y, and takes it.
    /// </summary>
    Int3 FindGround(StartSite site, int w, int d, int tolerance, bool bare, int solidDepth)
    {
        PrepareSim(); // navigation up to date with the instant work of earlier scenarios
        var map = _scene.Map;
        var world = map.World;
        var sim = _scene.SimRunner.Sim;
        const int r = GroundSearchRadius;
        var offsets = new List<(int X, int Z)>();
        for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++) offsets.Add((dx, dz));
        int taken = 0, away = 0, unfit = 0;
        foreach (var (dx, dz) in offsets.OrderBy(o => o.X * o.X + o.Z * o.Z))
        {
            int x0 = site.X + dx - w / 2, z0 = site.Z + dz - d / 2;
            var rect = new Rect2I(x0 - 1, z0 - 1, w + 2, d + 2);
            if (x0 < 2 || z0 < 2 || x0 + w + 2 > world.SizeX || z0 + d + 2 > world.SizeZ) continue;
            if (_doneGround.Any(t => t.Grow(2).Intersects(rect))) { taken++; continue; }
            int h0 = world.GetSurfaceHeight(x0, z0);
            if (sim.Nav.ComponentOf(new Int3(x0, h0 + 1, z0)) != sim.HomeComponent) { away++; continue; }
            if (Fits(x0, z0, h0))
            {
                _doneGround.Add(rect);
                return new Int3(x0, h0, z0);
            }
            unfit++;
        }
        throw new InvalidOperationException($"no {w}×{d} patch of ground within {r} of the start site " +
            $"(taken:{taken} off home's component:{away} unfit:{unfit}, home component {sim.HomeComponent})");

        bool Fits(int x0, int z0, int h0)
        {
            for (int z = z0 - 1; z <= z0 + d; z++)
                for (int x = x0 - 1; x <= x0 + w; x++)
                {
                    int h = world.GetSurfaceHeight(x, z);
                    if (Math.Abs(h - h0) > tolerance) return false;
                    var ground = Blocks.Registry.Get(world.GetBlock(x, h, z));
                    if (!ground.IsNatural || !ground.IsSolid || ground.IsSlope) return false;
                    bool inside = x >= x0 && x < x0 + w && z >= z0 && z < z0 + d;
                    for (int y = h - solidDepth; inside && y < h; y++)
                        if (!Blocks.Registry.Get(world.GetBlock(x, y, z)).IsSolid) return false;
                    for (int y = h + 1; y <= h + 3; y++)
                    {
                        var above = Blocks.Registry.Get(world.GetBlock(x, y, z));
                        if (above.Id != Blocks.Air && (bare || y > h + 1 || !above.IsSlope)) return false;
                    }
                    if (map.Colony.Bodies.IsOccupied(new Int3(x, h + 1, z))) return false;
                }
            return true;
        }
    }

    /// <summary>
    /// <c>debug_spawn</c>: F9 adds <see cref="MapScene.DebugSpawnCount"/> colonists at home. <c>colonist_bar</c>: past
    /// <see cref="ColonistBar.CompactAbove"/> the bar is compact and stays on screen.
    /// </summary>
    async Task CheckSpawnKey()
    {
        PrepareSim();
        var colonists = _scene.Map.Colony.Colonists;
        int before = colonists.Count;
        await Key(Godot.Key.F9);
        int added = colonists.Count - before;
        // Spawned colonists land on the standable cell nearest home.
        bool found = _scene.SimRunner.Sim.Nav.Rules.FindStandableNear(_scene.Map.Colony.Home, ColonistFactory.SpawnRadius, out var home);
        bool atHome = found && colonists.Skip(before).All(c => c.Cell == home);
        Metric("debug_spawn", $"before:{before} added:{added} at_home:{atHome}");
        Metric("debug_spawn_ok", added == MapScene.DebugSpawnCount && atHome ? 1 : 0);
        await Frames(3);
        CheckColonistBar("colonist_bar");
    }

    /// <summary>Records <paramref name="name"/>: the bar's mode, width and whether it is on screen with every button in it.</summary>
    void CheckColonistBar(string name)
    {
        var bar = _scene.Hud.ColonistBar;
        var screen = GetViewport().GetVisibleRect();
        var rect = bar.GetGlobalRect();
        int count = _scene.Map.Colony.Colonists.Count;
        bool onScreen = screen.Encloses(rect);
        bool ok = bar.Entries.Count == count && bar.Compact == (count > ColonistBar.CompactAbove) && onScreen;
        Metric(name, $"colonists:{count} buttons:{bar.Entries.Count} compact:{bar.Compact} width:{rect.Size.X:0} on_screen:{onScreen}");
        Metric(name + "_ok", ok ? 1 : 0);
    }

    // ── Bulk-spawn benchmark (spec 2a item 4) ─────────────────────────────────────────────────────────────────────────

    const int BenchSize = 60, BenchDepth = 8, BenchStairRuns = 20, BenchRunLength = BenchDepth + 1;
    const double BenchSeconds = 15, BenchSkipSeconds = 3;
    const double BenchFrameMsAvgLimit = 16.7, BenchFrameMsP99Limit = 25;
    static readonly int[] BenchCounts = [20, 50, 100];

    /// <summary>The benchmark on Large (the spec's map), else a note that it was skipped.</summary>
    List<(string? Name, Func<Task> Setup)> BenchScenarios(StartSite site)
    {
        if (_scene.Map.World.SizeX != MapSizes.Dimension(MapSize.Large))
            return [(null, () => { Metric("bench", "skipped: Large maps only"); return Task.CompletedTask; })];
        var area = BenchArea(site);
        return
        [
            (null, () => BenchSetup(site, area)),
            .. BenchCounts.Select(n => ((string?)(n == BenchCounts[^1] ? $"bench_{n}" : null), (Func<Task>)(() => MeasureBench(n)))),
            (null, () => BenchCleanup(area)),
        ];
    }

    /// <summary>The 60×60 dig just east of the start site's level core, from its ground down <see cref="BenchDepth"/> levels.</summary>
    static DragBox BenchArea(StartSite site)
    {
        int x0 = site.X + StartSiteFinder.CoreRadius + 2, z0 = site.Z - BenchSize / 2;
        return new DragBox(new(x0, site.Level - 1, z0), new(x0 + BenchSize - 1, site.Level - BenchDepth, z0 + BenchSize - 1));
    }

    /// <summary>
    /// Queues the benchmark's work for busy colonists: the <see cref="BenchArea"/> dig plus <see cref="BenchStairRuns"/>
    /// Dig-stairs runs, each <see cref="BenchDepth"/> levels down, in a row along the area's south side. <c>bench_jobs</c>.
    /// </summary>
    async Task BenchSetup(StartSite site, DragBox area)
    {
        PrepareSim();
        var map = _scene.Map;
        int dig = Designator.Apply(map, DesignationTool.Dig, BuildMaterial.Wood, area);
        int stairs = 0;
        int z = area.End.Z + 2;
        for (int i = 0; i < BenchStairRuns; i++)
        {
            int x = area.Start.X + 1 + 3 * i, y = map.World.GetSurfaceHeight(x, z);
            stairs += Designator.Apply(map, DesignationTool.DigStairs, BuildMaterial.Wood, new DragBox(new(x, y, z), new(x, y, z + BenchRunLength - 1)));
        }
        Metric("bench_jobs", $"dig:{dig} dig_stairs_runs:{BenchStairRuns} dig_stairs_jobs:{stairs} area:({area.Start.X},{area.Start.Z})-({area.End.X},{area.End.Z}) y:{area.End.Y}..{area.Start.Y}");
        await View(site.X + BenchSize / 2, site.Z, _scene.Slice.TopLevel, yaw: 20, pitch: 50, distance: 55);
        await WaitForRemesh();
    }

    /// <summary>
    /// Spawns colonists at home up to <paramref name="n"/>, runs at 1× for <see cref="BenchSeconds"/> with the frame cap
    /// lifted and times the frames after the first <see cref="BenchSkipSeconds"/>. Frame times are wall-clock intervals
    /// between frames; tick times are the runner's, read on each frame a tick ran (at 1× that is at most one per frame).
    /// Records <c>bench_N_frame_ms_avg</c>/<c>_p99</c>/<c>_max</c>, <c>bench_N_tick_ms_avg</c>/<c>_max</c>,
    /// <c>bench_N_paths_pending_max</c>, <c>bench_N_jobs_done</c>/<c>_left</c>, a profile (render CPU and GPU time per frame,
    /// tick p99) and what the colonists were doing at the end. For 20, <c>bench_20_ok</c> checks the limits (avg ≤ 16.7 ms,
    /// p99 ≤ 25 ms), that all 20 are there and that work got done.
    /// </summary>
    async Task MeasureBench(int n)
    {
        PrepareSim();
        var map = _scene.Map;
        var colony = map.Colony;
        var runner = _scene.SimRunner;
        var paths = runner.Sim.Paths;
        _scene.SpawnColonists(Math.Max(0, n - colony.Colonists.Count));
        await Frames(2);
        int jobsBefore = map.Jobs.Count;

        var frameMs = new List<double>();
        var tickMs = new List<double>();
        int pendingMax = 0;
        double renderCpuMs = 0, renderGpuMs = 0;
        var viewport = GetViewport().GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(viewport, true);
        try
        {
            await Uncapped(async () =>
            {
                runner.Speed = 1;
                long start = Stopwatch.GetTimestamp(), last = start, lastTick = colony.Tick;
                while (true)
                {
                    await NextFrame();
                    long now = Stopwatch.GetTimestamp();
                    double elapsed = Stopwatch.GetElapsedTime(start, now).TotalSeconds;
                    if (elapsed >= BenchSeconds) break;
                    double ms = Stopwatch.GetElapsedTime(last, now).TotalMilliseconds;
                    last = now;
                    bool ticked = colony.Tick != lastTick;
                    lastTick = colony.Tick;
                    if (elapsed < BenchSkipSeconds) continue;
                    frameMs.Add(ms);
                    if (ticked) tickMs.Add(runner.LastTickMs);
                    pendingMax = Math.Max(pendingMax, paths.Pending);
                    renderCpuMs += RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport);
                    renderGpuMs += RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport);
                }
                runner.Speed = 0;
            });
        }
        finally
        {
            runner.Speed = 0;
            RenderingServer.ViewportSetMeasureRenderTime(viewport, false);
        }

        string p = $"bench_{n}";
        int frames = frameMs.Count;
        double avg = frameMs.Average(), p99 = Percentile(frameMs, 0.99);
        Metric(p + "_colonists", colony.Colonists.Count);
        Metric(p + "_frame_ms_avg", avg);
        Metric(p + "_frame_ms_p99", p99);
        Metric(p + "_frame_ms_max", frameMs.Max());
        Metric(p + "_tick_ms_avg", tickMs.Count > 0 ? tickMs.Average() : 0);
        Metric(p + "_tick_ms_max", tickMs.Count > 0 ? tickMs.Max() : 0);
        Metric(p + "_paths_pending_max", pendingMax);
        int jobsDone = jobsBefore - map.Jobs.Count;
        Metric(p + "_jobs_done", jobsDone);
        Metric(p + "_jobs_left", map.Jobs.Count);
        Metric(p + "_profile", $"frames:{frames} ticks:{tickMs.Count} render_cpu_ms_avg:{renderCpuMs / frames:0.##} render_gpu_ms_avg:{renderGpuMs / frames:0.##} " +
            $"tick_ms_p99:{(tickMs.Count > 0 ? Percentile(tickMs, 0.99) : 0):0.##}");
        var activities = colony.Colonists.GroupBy(c => c.Stuck ? "Stuck" : c.Activity.ToString()).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}");
        Metric(p + "_activity", $"{string.Join(' ', activities)} jobs_left:{map.Jobs.Count}");
        if (n == BenchCounts[0])
            Metric(p + "_ok", avg <= BenchFrameMsAvgLimit && p99 <= BenchFrameMsP99Limit && colony.Colonists.Count == n && jobsDone > 0 ? 1 : 0);
        if (n == BenchCounts[^1]) CheckColonistBar("bench_colonist_bar");
    }

    static double Percentile(List<double> values, double q)
    {
        var sorted = values.Order().ToList();
        return sorted[Math.Max(0, (int)Math.Ceiling(sorted.Count * q) - 1)];
    }

    /// <summary>Drops the benchmark's remaining jobs, so the menu checks save a map without them.</summary>
    Task BenchCleanup(DragBox area)
    {
        var map = _scene.Map;
        Designator.Apply(map, DesignationTool.Cancel, BuildMaterial.Wood, new DragBox(
            new(area.Start.X, 255, area.Start.Z), new(area.End.X, 1, area.End.Z + 2 + BenchRunLength)));
        Metric("bench_jobs_left_after_cleanup", map.Jobs.Count);
        return Task.CompletedTask;
    }
}
