using System.Diagnostics;
using ColonySim.Core;
using Godot;

namespace ColonySim.Game;

/// <summary>Step 2a colony checks: the three starting colonists, a dig worked by them, hiding above the view level, the time controls and the colonist UI.</summary>
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
    /// <c>select_by_click</c>: with the Camera tool, a click on a colonist's screen position selects it without moving the
    /// camera, and a click on empty ground clears the selection.
    /// </summary>
    async Task CheckSelectByClick(StartSite site)
    {
        PrepareSim();
        _scene.Tools.ActiveTool = null;
        _scene.Selected = null;
        var colonist = _scene.Map.Colony.Colonists[0];
        var feet = _scene.ColonistRenderer.RenderPosition(colonist);
        await View((int)feet.X, (int)feet.Z, _scene.Slice.TopLevel, yaw: 30, pitch: 50, distance: 16);
        await Frames(3);
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
}
