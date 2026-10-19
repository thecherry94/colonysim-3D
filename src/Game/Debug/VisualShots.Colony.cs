using System.Diagnostics;
using ColonySim.Core;
using Godot;

namespace ColonySim.Game;

/// <summary>Step 2a colony checks: the three starting colonists, a dig worked by them, hiding above the view level and the time controls.</summary>
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
}
