using System.Diagnostics;
using ColonySim.Core;
using Godot;

namespace ColonySim.Game;

/// <summary>Step 1 final checks: whole-map frame rate, the debug overlay, save round trips and the menu flows.</summary>
public partial class VisualShots
{
    const int FpsWarmupFrames = 30;
    const int FpsFrames = 120;
    const int SceneTimeoutFrames = 3000;
    const string CorruptSave = "zz-harness-corrupt.csim";

    /// <summary>Block hash of the freshly generated harness map, before any scenario edits it.</summary>
    ulong _generatedHash;

    /// <summary>
    /// <c>overview_fps_avg</c>, <c>overview_frame_ms_p95</c>: zooms out until the whole map is in view, then times
    /// <see cref="FpsFrames"/> frames with VSync off, so the numbers show real headroom. Also records the render
    /// counters of the last frame for profiling.
    /// </summary>
    async Task MeasureOverviewFps()
    {
        var w = _scene.Map.World;
        var rig = _scene.Rig;
        await View(w.SizeX / 2, w.SizeZ / 2, _scene.Slice.TopLevel, yaw: 0, pitch: 60, distance: CameraRig.MinDistance);
        rig.FocusY = w.GetSurfaceHeight(w.SizeX / 2, w.SizeZ / 2) + 1;

        // Corners of the map's terrain box: zoom out until all of them are on screen.
        Vector3[] corners =
        [
            new(0, 128, 0), new(w.SizeX, 128, 0), new(0, 128, w.SizeZ), new(w.SizeX, 128, w.SizeZ),
            new(0, 200, 0), new(w.SizeX, 200, 0), new(0, 200, w.SizeZ), new(w.SizeX, 200, w.SizeZ),
        ];
        float distance = CameraRig.MinDistance;
        while (distance < CameraRig.MaxDistance)
        {
            rig.Distance = distance;
            if (corners.All(rig.Camera.IsPositionInFrustum)) break;
            distance += 5;
        }
        Metric("overview_distance", rig.Distance);
        Metric("overview_whole_map_in_view", corners.All(rig.Camera.IsPositionInFrustum) ? "true" : "false");

        var vsync = DisplayServer.WindowGetVsyncMode();
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        var viewport = GetViewport().GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(viewport, true);
        try
        {
            await Frames(FpsWarmupFrames);
            var frameMs = new double[FpsFrames];
            long start = Stopwatch.GetTimestamp(), last = start;
            for (int i = 0; i < FpsFrames; i++)
            {
                await NextFrame();
                long now = Stopwatch.GetTimestamp();
                frameMs[i] = Stopwatch.GetElapsedTime(last, now).TotalMilliseconds;
                last = now;
            }
            double total = Stopwatch.GetElapsedTime(start, last).TotalSeconds;
            Array.Sort(frameMs);
            Metric("overview_fps_avg", FpsFrames / total);
            Metric("overview_frame_ms_p95", frameMs[(int)Math.Ceiling(FpsFrames * 0.95) - 1]);
            Metric("overview_frame_ms_max", frameMs[^1]);
            Metric("overview_render", $"draw_calls:{Monitor(Performance.Monitor.RenderTotalDrawCallsInFrame)} " +
                $"objects:{Monitor(Performance.Monitor.RenderTotalObjectsInFrame)} " +
                $"primitives:{Monitor(Performance.Monitor.RenderTotalPrimitivesInFrame)} " +
                $"render_cpu_ms:{RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport):0.##} " +
                $"render_gpu_ms:{RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport):0.##} " +
                $"video_mem_mb:{Monitor(Performance.Monitor.RenderVideoMemUsed) / (1024 * 1024)}");
        }
        finally
        {
            RenderingServer.ViewportSetMeasureRenderTime(viewport, false);
            DisplayServer.WindowSetVsyncMode(vsync);
        }
    }

    static long Monitor(Performance.Monitor m) => (long)Performance.GetMonitor(m);

    Task RecordGeneratedHash() => Task.Run(() => _generatedHash = Fingerprint.Of(_scene.Map).Blocks);

    /// <summary><c>debug_overlay</c>: its state at start, after F3, after F3 again, and the text it showed.</summary>
    async Task CheckDebugOverlay()
    {
        var overlay = _scene.DebugOverlay;
        var states = new List<string> { $"start:{OnOff(overlay.Visible)}" };
        for (int i = 0; i < 2; i++)
        {
            await Key(Godot.Key.F3);
            states.Add($"f3:{OnOff(overlay.Visible)}");
        }
        states.Add($"text:[{overlay.Stats.Replace("\n", " | ")}]");
        Metric("debug_overlay", string.Join(' ', states));
    }

    static string OnOff(bool on) => on ? "on" : "off";

    /// <summary>
    /// <c>save_roundtrip_equal</c>: saves the current (built) map to a temp file, loads it back and compares blocks
    /// (the FNV hash of <c>TestWorlds.WorldHash</c>), discovery, surface heights, settings and jobs.
    /// </summary>
    async Task CheckSaveRoundtrip()
    {
        var map = _scene.Map;
        string path = Path.Combine(Path.GetTempPath(), $"colonysim-roundtrip-{System.Environment.ProcessId}.csim");
        try
        {
            long start = Stopwatch.GetTimestamp();
            using (var file = File.Create(path)) MapSerializer.Save(map, file);
            double saveMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            start = Stopwatch.GetTimestamp();
            ColonyMap loaded;
            using (var file = File.OpenRead(path)) loaded = MapSerializer.Load(file);
            double loadMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

            var (before, after) = await Task.Run(() => (Fingerprint.Of(map), Fingerprint.Of(loaded)));
            Metric("save_roundtrip_equal", before == after ? "true" : "false");
            Metric("save_roundtrip", $"{before.Compare(after)} save_ms:{saveMs:0} load_ms:{loadMs:0} " +
                $"size_mb:{new FileInfo(path).Length / 1048576.0:0.0}");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The menu flows, driven like a player through the real HUD and new-game screen:
    /// <list type="bullet">
    /// <item><c>menu_save</c>: queues a few dig jobs, presses Ctrl+S and reads the HUD message (shot <c>hud_saved</c>).</item>
    /// <item><c>menu_load_error</c>: Menu, then Load on a corrupt save: the red error, and the screen still usable
    /// (shot <c>menu_load_error</c>).</item>
    /// <item><c>menu_roundtrip_equal</c>: Load on the save just made: the map comes back exactly as saved (shot
    /// <c>loaded</c>).</item>
    /// <item><c>menu_generate_same_seed</c>: Menu, then Generate with the harness seed and size: the same blocks as
    /// the harness's own generation (shots <c>menu_generating</c>, <c>menu_generated</c>).</item>
    /// </list>
    /// The harness's map scene is freed on the way, so from the first Menu press on it works on the tree only.
    /// </summary>
    async Task CheckMenu()
    {
        var map = _scene.Map;
        var site = FlatSite(4, 4, null);
        Designator.Apply(map, DesignationTool.Dig, BuildMaterial.Wood, new DragBox(new(site.X, site.H, site.Z), new(site.X + 3, site.H - 2, site.Z + 3)));
        await View(site.X + 2, site.Z + 2, _scene.Slice.TopLevel, 30, 50, 40);
        var before = await Task.Run(() => Fingerprint.Of(map));

        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Godot.Key.S, Keycode = Godot.Key.S, CtrlPressed = true, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Godot.Key.S, Keycode = Godot.Key.S, CtrlPressed = true, Pressed = false });
        await Frames(2);
        string message = _scene.Hud.Message;
        const string savedPrefix = "Saved ";
        if (!message.StartsWith(savedPrefix)) throw new InvalidOperationException($"Ctrl+S did not save: '{message}'");
        string saveFile = message[savedPrefix.Length..];
        Metric("menu_save", $"message:[{message}] jobs:{map.Jobs.Count} listed_first:{SaveGames.List().FirstOrDefault() == saveFile}");
        await Shoot("hud_saved");

        string corruptPath = Path.Combine(ProjectSettings.GlobalizePath(SaveGames.Dir), CorruptSave);
        File.WriteAllBytes(corruptPath, [.. "CSIM"u8, 1, 0, 0, 0, .. "not a brotli stream"u8]);
        try
        {
            var menu = await OpenMenu();
            int corrupt = FindSave(menu, CorruptSave);
            Select(menu, corrupt);
            menu.LoadButton.EmitSignal(BaseButton.SignalName.Pressed);
            for (int i = 0; i < SceneTimeoutFrames && menu.ErrorText == "" && _tree.CurrentScene == menu; i++) await NextFrame();
            bool usable = _tree.CurrentScene == menu && !menu.GenerateButton.Disabled && !menu.LoadButton.Disabled;
            Metric("menu_load_error", $"error:[{menu.ErrorText}] usable:{usable}");
            await Shoot("menu_load_error");

            Select(menu, FindSave(menu, saveFile));
            menu.LoadButton.EmitSignal(BaseButton.SignalName.Pressed);
            _scene = await WaitForMapScene();
            await Frames(SettleFrames);
            var after = await Task.Run(() => Fingerprint.Of(_scene.Map));
            Metric("menu_roundtrip_equal", before == after ? "true" : "false");
            Metric("menu_roundtrip", before.Compare(after));
            await Shoot("loaded");
        }
        finally
        {
            File.Delete(corruptPath);
            File.Delete(Path.Combine(ProjectSettings.GlobalizePath(SaveGames.Dir), saveFile));
        }

        var settings = _scene.Map.Settings;
        var newGame = await OpenMenu();
        newGame.SeedField.Text = settings.Seed.ToString();
        var size = Enum.GetValues<MapSize>().First(s => MapSizes.Dimension(s) == settings.SizeX);
        newGame.SizeField.Select(newGame.SizeField.GetItemIndex((int)size));
        long start = Stopwatch.GetTimestamp();
        newGame.GenerateButton.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(20);
        await Shoot("menu_generating");
        _scene = await WaitForMapScene();
        double generateMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        ulong hash = await Task.Run(() => Fingerprint.Of(_scene.Map).Blocks);
        Metric("menu_generate_same_seed", hash == _generatedHash ? "true" : "false");
        Metric("menu_generate", $"seed:{settings.Seed} size:{size} ms_to_map:{generateMs:0} hash:{hash:x16}");
        await Frames(SettleFrames);
        await Shoot("menu_generated");
    }

    /// <summary>Presses the HUD's Menu button and waits for the new-game screen.</summary>
    async Task<NewGameScreen> OpenMenu()
    {
        _scene.Hud.MenuButton.EmitSignal(BaseButton.SignalName.Pressed);
        var menu = await WaitForScene<NewGameScreen>();
        await Frames(SettleFrames);
        return menu;
    }

    async Task<MapScene> WaitForMapScene()
    {
        var scene = await WaitForScene<MapScene>();
        _meshStart = Stopwatch.GetTimestamp();
        await WaitForMeshing();
        return scene;
    }

    async Task<T> WaitForScene<T>() where T : Node
    {
        for (int i = 0; i < SceneTimeoutFrames; i++)
        {
            if (_tree.CurrentScene is T scene && scene.IsNodeReady()) return scene;
            await NextFrame();
        }
        throw new TimeoutException($"{typeof(T).Name} did not open");
    }

    static int FindSave(NewGameScreen menu, string file)
    {
        for (int i = 0; i < menu.SaveList.ItemCount; i++)
            if (menu.SaveList.GetItemText(i) == file) return i;
        throw new InvalidOperationException($"save {file} is not listed");
    }

    static void Select(NewGameScreen menu, int index)
    {
        menu.SaveList.Select(index);
        menu.SaveList.EmitSignal(ItemList.SignalName.ItemSelected, index);
    }

    /// <summary>Everything a save must preserve, hashed.</summary>
    readonly record struct Fingerprint(ulong Blocks, ulong Discovery, ulong Surface, MapSettings Settings, string Jobs)
    {
        const ulong Offset = 14695981039346656037UL, Prime = 1099511628211UL;

        /// <summary><c>Blocks</c> is the FNV-1a hash of <c>TestWorlds.WorldHash</c> (y, z, x order, two bytes per block).</summary>
        public static Fingerprint Of(ColonyMap map)
        {
            var w = map.World;
            ulong blocks = Offset, discovery = Offset, surface = Offset;
            for (int y = 0; y < w.SizeY; y++)
                for (int z = 0; z < w.SizeZ; z++)
                    for (int x = 0; x < w.SizeX; x++)
                    {
                        ushort b = w.GetBlock(x, y, z);
                        blocks = (blocks ^ (byte)b) * Prime;
                        blocks = (blocks ^ (byte)(b >> 8)) * Prime;
                        discovery = (discovery ^ (w.IsDiscovered(x, y, z) ? 1UL : 0UL)) * Prime;
                    }
            for (int z = 0; z < w.SizeZ; z++)
                for (int x = 0; x < w.SizeX; x++)
                    surface = (surface ^ (ulong)w.GetSurfaceHeight(x, z)) * Prime;
            string jobs = string.Join(';', map.Jobs.All.Select(j => $"{j.Position.X},{j.Position.Y},{j.Position.Z},{j.Kind},{j.Block}"));
            return new Fingerprint(blocks, discovery, surface, map.Settings, jobs);
        }

        public string Compare(Fingerprint o) =>
            $"blocks:{Same(Blocks == o.Blocks)} discovery:{Same(Discovery == o.Discovery)} surface:{Same(Surface == o.Surface)} " +
            $"settings:{Same(Settings == o.Settings)} jobs:{(Jobs == o.Jobs ? "same" : "differ")}({Count(Jobs)}/{Count(o.Jobs)}) " +
            $"hash:{Blocks:x16}";

        static string Same(bool same) => same ? "same" : "differ";

        static int Count(string jobs) => jobs.Length == 0 ? 0 : jobs.Count(c => c == ';') + 1;
    }
}
