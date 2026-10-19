using System.Diagnostics;
using System.Globalization;
using ColonySim.Core;
using Godot;

namespace ColonySim.Game;

/// <summary>
/// Screenshot harness, active when <c>COLONYSIM_SHOTS=&lt;dir&gt;</c> is set. Waits for the initial meshing,
/// runs each named scenario, saves <c>&lt;dir&gt;/&lt;name&gt;.png</c>, writes <c>name=value</c> lines to
/// <c>&lt;dir&gt;/metrics.txt</c>, finishes with the menu checks (save, menu, load, generate) and quits.
/// </summary>
public partial class VisualShots : Node
{
    const int SettleFrames = 5;
    const double MeshTimeoutSeconds = 300;

    const int MidLevel = 100;
    const int LevelSteps = 20;
    const int CaveBand = 8;

    // Designations site: the 9×7 room at its origin, the 8×8 pit 12 blocks east of it.
    const int HouseSiteW = 20, HouseSiteD = 8, PitOffsetX = 12;
    // Tool check site: drags over a 5×4 rectangle starting 2 blocks in.
    const int ToolSiteW = 9, ToolSiteD = 8;
    const int WorkTimeoutFrames = 3000;

    MapScene _scene = null!;
    /// <summary>The harness outlives its map scene (menu checks), so it waits on the tree, not on itself.</summary>
    SceneTree _tree = null!;
    string _dir = "";
    long _meshStart;
    (int X, int Y, int Z) _cave;

    /// <summary>
    /// Scenarios in run order: each sets up the view; the harness then shoots it. A null name runs the
    /// setup without a shot (measurements).
    /// </summary>
    List<(string? Name, Func<Task> Setup)> Scenarios()
    {
        var world = _scene.Map.World;
        var (caveX, caveZ) = CaveFocus();
        var (peakX, peakZ) = HighestColumn();
        var house = FlatSite(HouseSiteW, HouseSiteD, null);
        // The sun shines toward +X/-Z; looking from that side shows the shadows cast toward the camera.
        const float shadowYaw = 145;
        return
        [
            ("overview", () => Task.CompletedTask),
            ("overview_full", MeasureOverviewFps),
            (null, RecordGeneratedHash),
            (null, CheckDebugOverlay),
            ("camera_rotated", () =>
            {
                _scene.Rig.Yaw = 135;
                _scene.Rig.Pitch = 35;
                _scene.Rig.Distance = 80;
                return Task.CompletedTask;
            }),
            ("slice_mid_caps", () => View(caveX, caveZ, MidLevel, yaw: 0, pitch: 60, distance: 70)),
            ("slice_mid_nocaps", () => { _scene.Slice.CapsEnabled = false; return View(caveX, caveZ, MidLevel, 0, 60, 70); }),
            ("slice_surface", () =>
            {
                _scene.Slice.CapsEnabled = true;
                return View(caveX, caveZ, world.GetSurfaceHeight(caveX, caveZ) + 1, 0, 50, 90);
            }),
            // Shadow-pass check: the mountain is cut down to near the surrounding ground, so its long shadow must vanish.
            ("shadow_unsliced", () => View(peakX, peakZ, _scene.Slice.TopLevel, shadowYaw, 50, 160)),
            ("shadow_sliced", () => View(peakX, peakZ, GroundNear(peakX, peakZ) + 2, shadowYaw, 50, 160)),
            // Deep uniform rock (few cap rectangles), then a sweep through the surface band, where terrain, trees
            // and water break the caps into many rectangles.
            (null, () => MeasureLevelSteps("level_step_ms", MidLevel + LevelSteps)),
            (null, () => MeasureLevelSteps("level_step_surface_ms", world.GetSurfaceHeight(caveX, caveZ) + LevelSteps / 2)),
            (null, CheckLevelInput),
            // Z-fight check: stairs on the cut level keep the cut block's top face in the chunk mesh, coplanar with
            // its cap. The cap must win cleanly: a uniformly dark patch, no green speckle.
            ("slice_stairs_cap", async () =>
            {
                int h = world.GetSurfaceHeight(caveX, caveZ);
                for (int z = caveZ - 4; z < caveZ + 4; z++)
                    for (int x = caveX - 4; x < caveX + 4; x++)
                        if (world.GetSurfaceHeight(x, z) == h && world.GetBlock(x, h + 1, z) == Blocks.Air)
                            world.SetBlock(x, h + 1, z, Blocks.Stairs(BuildMaterial.Wood, Facing.North));
                await WaitForRemesh();
                await View(caveX, caveZ, h, 20, 55, 18);
            }),
            ("slice_fog_off", async () =>
            {
                _scene.Slice.FogEnabled = false;
                await WaitForRemesh();
                await View(caveX, caveZ, MidLevel, 0, 60, 70);
            }),
            (null, async () =>
            {
                _scene.Slice.FogEnabled = true;
                await WaitForRemesh();
            }),
            ("designations", () => Designations(house)),
            ("built", () => Built(house)),
            (null, CheckSaveRoundtrip),
            ("built_house", () => View(house.X + 4, house.Z + 3, _scene.Slice.TopLevel, 210, 35, 26)),
            ("built_house_slice", () => View(house.X + 4, house.Z + 3, house.H + 2, 210, 55, 26)),
            (null, MeasureEditLatency),
            (null, async () => await CheckTools(await LevelSite(FlatSite(ToolSiteW, ToolSiteD, house), ToolSiteW, ToolSiteD))),
            // The tool site now has walls, so this picks a fresh one.
            (null, async () => await CheckUnslicedTools(await LevelSite(FlatSite(ToolSiteW, ToolSiteD, house), ToolSiteW, ToolSiteD))),
            (null, MeasureEditStream),
            (null, MeasureEditBurst),
            (null, MeasureOverlayIncremental),
            (null, () => CheckDragSafety(house)),
            (null, () => ShowOverlayKinds(house)),
            ("cave_hidden", () => FindHiddenCave(house)),
            ("cave_revealed", RevealCave),
        ];
    }

    public void Setup(MapScene scene, string dir)
    {
        _scene = scene;
        _dir = Path.GetFullPath(dir);
        _meshStart = Stopwatch.GetTimestamp();
    }

    public override void _Ready()
    {
        _tree = GetTree();
        var window = GetWindow();
        window.ContentScaleFactor = 1;
        window.Size = new Vector2I(1280, 720);
        Run();
    }

    async void Run()
    {
        try
        {
            Directory.CreateDirectory(_dir);
            File.Delete(MetricsPath);
            if (_scene.GenerateMs is double gen) Metric("generate_ms", gen);

            await WaitForMeshing();
            Metric("initial_mesh_ms", Stopwatch.GetElapsedTime(_meshStart).TotalMilliseconds);
            await Frames(SettleFrames);

            foreach (var (name, setup) in Scenarios())
            {
                await setup();
                if (name != null) await Shoot(name);
            }
            await CheckMenu();
            _tree.Quit();
        }
        catch (Exception e)
        {
            GD.PushError($"VisualShots failed: {e}");
            _tree.Quit(1);
        }
    }

    async Task WaitForMeshing()
    {
        long start = Stopwatch.GetTimestamp();
        while (_scene.Chunks.PendingChunks > 0)
        {
            if (Stopwatch.GetElapsedTime(start).TotalSeconds > MeshTimeoutSeconds)
                throw new TimeoutException($"meshing did not finish ({_scene.Chunks.PendingChunks} chunks pending)");
            await NextFrame();
        }
    }

    /// <summary>Waits until chunks dirtied by the scenario are remeshed.</summary>
    async Task WaitForRemesh()
    {
        // process_frame fires before the nodes' _Process, so the renderer only queues the dirty chunks during
        // the frame after next; until then PendingChunks may still read 0.
        await Frames(2);
        await WaitForMeshing();
    }

    /// <summary>Focuses the camera on column (x, z) at <paramref name="level"/>, with the focus height already settled.</summary>
    Task View(int x, int z, int level, float yaw, float pitch, float distance)
    {
        var rig = _scene.Rig;
        rig.Yaw = yaw;
        rig.Pitch = pitch;
        rig.Distance = distance;
        rig.SetFocus(x + 0.5f, z + 0.5f);
        _scene.Slice.SetLevel(level);
        rig.FocusY = (_scene.Slice.IsSliced ? _scene.Slice.ViewLevel : _scene.Map.World.GetSurfaceHeight(x, z)) + 1;
        return Task.CompletedTask;
    }

    /// <summary>
    /// <c>{prefix}_avg</c>/<c>_max</c>: <see cref="LevelSteps"/> single steps down from <paramref name="top"/>, caps on.
    /// </summary>
    async Task MeasureLevelSteps(string prefix, int top)
    {
        var slice = _scene.Slice;
        slice.SetLevel(top);
        await NextFrame();
        double sum = 0, max = 0;
        for (int i = 0; i < LevelSteps; i++)
        {
            slice.SetLevel(slice.ViewLevel - 1);
            sum += slice.LastLevelChangeMs;
            max = Math.Max(max, slice.LastLevelChangeMs);
            await NextFrame();
        }
        Metric(prefix + "_avg", sum / LevelSteps);
        Metric(prefix + "_max", max);
    }

    /// <summary>
    /// <c>input_check</c>: feeds the level controls synthetic input through the real input pipeline and records
    /// the level after each event, plus where the camera focus settled.
    /// </summary>
    async Task CheckLevelInput()
    {
        var slice = _scene.Slice;
        var steps = new List<string>();
        async Task Send(string label, InputEvent e)
        {
            Input.ParseInputEvent(e);
            await Frames(2);
            steps.Add($"{label}:{slice.ViewLevel}");
        }
        static InputEventKey Key(Key key) => new() { PhysicalKeycode = key, Keycode = key, Pressed = true };
        static InputEventMouseButton Wheel(MouseButton button, bool shift) =>
            new() { ButtonIndex = button, Pressed = true, ShiftPressed = shift, Factor = 1 };

        slice.SetLevel(120);
        await Send("PgDn", Key(Godot.Key.Pagedown));
        await Send("PgUp", Key(Godot.Key.Pageup));
        await Send("ShiftWheelDown", Wheel(MouseButton.WheelDown, true));
        await Send("ShiftWheelUp", Wheel(MouseButton.WheelUp, true));
        await Send("ShiftWheelRight", Wheel(MouseButton.WheelRight, true));
        await Send("Wheel", Wheel(MouseButton.WheelDown, false));
        await Frames(60);
        steps.Add($"focusY:{_scene.Rig.FocusPoint.Y:0.0}");
        await Send("Home", Key(Godot.Key.Home));
        Metric("input_check", string.Join(' ', steps));
    }

    /// <summary>
    /// Center of the river-free chunk column (away from the map edge) with the most undiscovered cave cells under
    /// it and its neighbors, counted in the few levels just under <see cref="MidLevel"/>, so the mid-level slice
    /// shots look at hidden caves.
    /// </summary>
    (int X, int Z) CaveFocus()
    {
        var w = _scene.Map.World;
        const int s = VoxelWorld.ChunkSize;
        var hidden = new long[w.ChunksX, w.ChunksZ];
        var river = new bool[w.ChunksX, w.ChunksZ];
        Parallel.For(0, w.ChunksX * w.ChunksZ, i =>
        {
            int cx = i % w.ChunksX, cz = i / w.ChunksX;
            for (int z = cz * s; z < cz * s + s; z++)
                for (int x = cx * s; x < cx * s + s; x++)
                {
                    int h = w.GetSurfaceHeight(x, z);
                    if (w.GetBlock(x, h + 1, z) == Blocks.Water) river[cx, cz] = true;
                    for (int y = MidLevel - CaveBand; y <= Math.Min(MidLevel, h - 1); y++)
                        if (!Blocks.Registry.Get(w.GetBlock(x, y, z)).IsSolid && !w.IsDiscovered(x, y, z)) hidden[cx, cz]++;
                }
        });
        long best = -1;
        (int, int) focus = (w.SizeX / 2, w.SizeZ / 2);
        // Two chunks from the edge, so the slice shots show no map border.
        for (int cz = 2; cz < w.ChunksZ - 2; cz++)
            for (int cx = 2; cx < w.ChunksX - 2; cx++)
            {
                if (river[cx, cz]) continue;
                long score = 0;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                        score += hidden[cx + dx, cz + dz];
                if (score > best) { best = score; focus = (cx * s + s / 2, cz * s + s / 2); }
            }
        return focus;
    }

    /// <summary>Typical ground height around (x, z): the 25th percentile of the surface within 48 blocks.</summary>
    int GroundNear(int x, int z)
    {
        var w = _scene.Map.World;
        var heights = new List<int>();
        for (int dz = -48; dz <= 48; dz += 2)
            for (int dx = -48; dx <= 48; dx += 2)
                if (w.InBounds(x + dx, 0, z + dz)) heights.Add(w.GetSurfaceHeight(x + dx, z + dz));
        heights.Sort();
        return heights[heights.Count / 4];
    }

    /// <summary>A map area for the tool scenarios: <c>H</c> is its highest ground; <c>Range</c> its height spread.</summary>
    readonly record struct Site(int X, int Z, int H, int Range);

    /// <summary>
    /// The flattest <paramref name="w"/>×<paramref name="d"/> area (plus a 1-block margin) with only air for 12 blocks
    /// above the ground (no trees, no water), nearest the map center and well away from <paramref name="avoid"/>.
    /// </summary>
    Site FlatSite(int w, int d, Site? avoid)
    {
        var world = _scene.Map.World;
        const int margin = 40, clearance = 12;
        Site best = default;
        long bestScore = long.MaxValue;
        for (int z = margin; z + d + margin <= world.SizeZ; z += 4)
            for (int x = margin; x + w + margin <= world.SizeX; x += 4)
            {
                if (avoid is { } a && Math.Abs(x - a.X) < 48 && Math.Abs(z - a.Z) < 48) continue;
                int lo = int.MaxValue, hi = int.MinValue;
                bool clear = true;
                for (int dz = -1; dz <= d && clear; dz++)
                    for (int dx = -1; dx <= w && clear; dx++)
                    {
                        int h = world.GetSurfaceHeight(x + dx, z + dz);
                        lo = Math.Min(lo, h);
                        hi = Math.Max(hi, h);
                        for (int y = h + 1; y <= h + clearance && clear; y++)
                            clear = world.GetBlock(x + dx, y, z + dz) == Blocks.Air;
                    }
                if (!clear) continue;
                long cx = x + w / 2 - world.SizeX / 2, cz = z + d / 2 - world.SizeZ / 2;
                long score = (long)(hi - lo) * 1_000_000 + cx * cx + cz * cz;
                if (score < bestScore) { bestScore = score; best = new Site(x, z, hi, hi - lo); }
            }
        if (bestScore == long.MaxValue) throw new InvalidOperationException("no clear site for the tool scenarios");
        return best;
    }

    /// <summary>
    /// Flattens the site (plus a 2-block margin) down to its lowest ground, so synthetic clicks aimed at the top face
    /// of a cell can't land on a neighboring higher block. Removed blocks are open to the sky, so they are discovered.
    /// </summary>
    async Task<Site> LevelSite(Site s, int w, int d)
    {
        var world = _scene.Map.World;
        int lo = int.MaxValue;
        for (int z = s.Z - 2; z <= s.Z + d + 1; z++)
            for (int x = s.X - 2; x <= s.X + w + 1; x++)
                lo = Math.Min(lo, world.GetSurfaceHeight(x, z));
        for (int z = s.Z - 2; z <= s.Z + d + 1; z++)
            for (int x = s.X - 2; x <= s.X + w + 1; x++)
            {
                int h = world.GetSurfaceHeight(x, z);
                if (h == lo) continue;
                for (int y = lo + 1; y <= h; y++)
                {
                    world.SetBlock(x, y, z, Blocks.Air);
                    world.SetDiscovered(x, y, z);
                }
                world.SetBlock(x, lo, z, Blocks.Grass);
                world.SetSurfaceHeight(x, z, lo);
            }
        await WaitForRemesh();
        return s with { H = lo, Range = 0 };
    }

    /// <summary>
    /// <c>designations</c>: a 9×7 Wall box over 3 levels, a Floor on top of it, a 6-long Stairs inside (climbing
    /// east through the floor) and an 8×8 Dig box 12 levels deep next to it.
    /// </summary>
    async Task Designations(Site s)
    {
        var map = _scene.Map;
        int h = s.H, x0 = s.X, z0 = s.Z, px = s.X + PitOffsetX;
        Designator.Apply(map, DesignationTool.Wall, BuildMaterial.Stone, new DragBox(new(x0, h + 1, z0), new(x0 + 8, h + 3, z0 + 6)));
        Designator.Apply(map, DesignationTool.Floor, BuildMaterial.Wood, new DragBox(new(x0, h + 4, z0), new(x0 + 8, h + 4, z0 + 6)));
        Designator.Apply(map, DesignationTool.Stairs, BuildMaterial.Wood, new DragBox(new(x0 + 1, h + 1, z0 + 3), new(x0 + 6, h + 1, z0 + 3)));
        Designator.Apply(map, DesignationTool.Dig, BuildMaterial.Wood, new DragBox(new(px, h, z0), new(px + 7, h - 11, z0 + 7)));
        var kinds = map.Jobs.All.GroupBy(j => j.Kind).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}");
        Metric("designations", $"site:({s.X},{s.Z}) ground:{h} range:{s.Range} jobs:{map.Jobs.Count} {string.Join(' ', kinds)}");
        await View(x0 + 10, z0 + 4, _scene.Slice.TopLevel, 200, 50, 30);
    }

    /// <summary><c>built</c>: completes every job with instant work, then slices one level above the pit's lowest dug cell.</summary>
    async Task Built(Site s)
    {
        var map = _scene.Map;
        int ticks = 0;
        while (map.Jobs.Count > 0) { InstantWorker.Step(map, 500); ticks++; }
        await WaitForRemesh();
        int px = s.X + PitOffsetX, bottom = s.H - 11;
        Metric("built", $"steps:{ticks} pit_bottom_air:{map.World.GetBlock(px + 3, bottom, s.Z + 3) == Blocks.Air}");
        await View(px + 4, s.Z + 4, bottom + 1, 200, 60, 36);
    }

    /// <summary>
    /// <c>edit_apply_frames_*</c>: frames from a single-block edit at a chunk corner (several chunks dirtied) to the
    /// frame its whole batch of new chunk meshes is applied.
    /// </summary>
    async Task MeasureEditLatency()
    {
        var world = _scene.Map.World;
        long applied = -1;
        double appliedMs = 0;
        long editStart = 0;
        void OnApplied(IReadOnlyCollection<Int3> _)
        {
            applied = (long)Engine.GetProcessFrames();
            appliedMs = Stopwatch.GetElapsedTime(editStart).TotalMilliseconds;
        }
        _scene.Chunks.EditsApplied += OnApplied;
        var frames = new List<long>();
        var ms = new List<double>();
        for (int i = 0; i < 10; i++)
        {
            int x = (2 + i % 4) * VoxelWorld.ChunkSize, z = (2 + i / 4) * VoxelWorld.ChunkSize;
            int y = world.GetSurfaceHeight(x, z);
            applied = -1;
            editStart = Stopwatch.GetTimestamp();
            world.SetBlock(x, y, z, Blocks.Air);
            long edit = (long)Engine.GetProcessFrames();
            for (int f = 0; f < 30 && applied < 0; f++) await NextFrame();
            frames.Add(applied < 0 ? -1 : applied - edit);
            ms.Add(appliedMs);
        }
        _scene.Chunks.EditsApplied -= OnApplied;
        Metric("edit_apply_frames", string.Join(' ', frames));
        Metric("edit_apply_ms", string.Join(' ', ms.Select(m => m.ToString("0.0", CultureInfo.InvariantCulture))));
        Metric("edit_apply_frames_max", frames.Max());
        Metric("edit_apply_ms_max", ms.Max());
    }

    /// <summary>
    /// <c>tool_check</c>: drives the tools through the real input pipeline (synthetic mouse and key events at the
    /// screen positions of chosen cells) and records what each step produced. Shoots <c>drag_preview</c> and
    /// <c>drag_preview_slice</c> mid-drag.
    /// </summary>
    async Task CheckTools(Site s)
    {
        var tools = _scene.Tools;
        var slice = _scene.Slice;
        var map = _scene.Map;
        var results = new List<string>();
        int h = s.H, ax = s.X + 2, az = s.Z + 2, bx = ax + 4, bz = az + 3; // a 5×4 rectangle
        Vector3 Top(int x, int y, int z) => new(x + 0.5f, y + 1, z + 0.5f);
        bool InRect(Int3 p) => p.X >= ax && p.X <= bx && p.Z >= az && p.Z <= bz;
        int Count(Func<Job, bool> match) => map.Jobs.All.Count(match);
        Metric("tool_site", $"({s.X},{s.Z}) ground:{h} range:{s.Range}");

        await View(ax + 2, az + 2, slice.TopLevel, 0, 60, 36);
        await Frames(30);

        // Hover: shows the mouse mapping is right (the wire box sits on the cell under the synthetic cursor).
        await Key(Godot.Key.Z);
        results.Add($"tool_key:{ToolController.ToolName(tools.ActiveTool)}");
        await Move(Top(ax, h, az));
        var hover = tools.GetNode<Node3D>("Hover");
        results.Add($"hover:{(hover.Visible && hover.Position == new Vector3(ax + 0.5f, h + 0.5f, az + 0.5f) ? "ok" : $"bad@{hover.Position}")}");

        // Dig drag at the top level, with a mid-drag screenshot.
        int before = map.Jobs.Count;
        await Press(Top(ax, h, az));
        await Move(Top(bx, h, bz), held: true);
        Metric("drag_status", tools.Status);
        await Shoot("drag_preview");
        await Release(Top(bx, h, bz));
        int dug = Count(j => j.Kind == JobKind.Dig && j.Position.Y == h && InRect(j.Position));
        results.Add($"top_drag:{map.Jobs.Count - before}/20(at_ground:{dug})");

        // Cancel over the same rectangle.
        await Key(Godot.Key.Delete);
        await Press(Top(ax, h, az));
        await Move(Top(bx, h, bz), held: true);
        await Release(Top(bx, h, bz));
        results.Add($"cancel:{map.Jobs.Count - before}_left");

        // Esc cancels a drag, then a second Esc returns to the Camera tool.
        await Key(Godot.Key.Z);
        await Press(Top(ax, h, az));
        await Move(Top(bx, h, bz), held: true);
        await Key(Godot.Key.Escape);
        results.Add($"esc_drag:{(!tools.IsDragging ? "cancelled" : "still_dragging")}");
        await Release(Top(bx, h, bz));
        results.Add($"esc_jobs:{map.Jobs.Count - before}");
        await Key(Godot.Key.Escape);
        results.Add($"esc_tool:{ToolController.ToolName(tools.ActiveTool)}");

        // Double-click with the Camera tool jumps to the level you'd stand on.
        await Press(Top(ax, h, az), doubleClick: true);
        await Release(Top(ax, h, az));
        results.Add($"double_click:{slice.ViewLevel}/{h + 1}");
        await Frames(60); // the camera eases to the new level

        // Wall drag in that slice, made 3 levels tall with PgUp mid-drag; Tab switches to stone first.
        await Key(Godot.Key.C);
        await Key(Godot.Key.Tab);
        results.Add($"tab:{tools.Material}");
        before = map.Jobs.Count;
        await Press(Top(ax, h, az));
        await Move(Top(bx, h, bz), held: true);
        await Key(Godot.Key.Pageup);
        await Key(Godot.Key.Pageup);
        results.Add($"wall_status:[{tools.Status}]");
        await Release(Top(bx, h, bz));
        int walls = Count(j => j.Kind == JobKind.BuildWall && j.Position.Y >= h + 1 && j.Position.Y <= h + 3 && InRect(j.Position));
        results.Add($"slice_wall_pgup:{map.Jobs.Count - before}/42(in_box:{walls})");

        // Dig drag in an underground slice, deepened by 3 levels with PgDn mid-drag.
        int cut = h - 2;
        await View(ax + 2, az + 2, cut, 0, 60, 36);
        await Frames(30);
        await Key(Godot.Key.Z);
        before = map.Jobs.Count;
        await Press(Top(ax, cut, az));
        await Move(Top(bx, cut, bz), held: true);
        for (int i = 0; i < 3; i++) await Key(Godot.Key.Pagedown);
        results.Add($"dig_status:[{tools.Status}]");
        await Shoot("drag_preview_slice");
        await Release(Top(bx, cut, bz));
        int digs = Count(j => j.Kind == JobKind.Dig && j.Position.Y >= cut - 3 && j.Position.Y <= cut && InRect(j.Position));
        results.Add($"slice_dig_pgdn:{map.Jobs.Count - before}/80(in_box:{digs})");

        // Instant work (the HUD's runner at 25×) completes both.
        int frames = await RunInstantWork();
        int air = 0, stone = 0;
        for (int z = az; z <= bz; z++)
            for (int x = ax; x <= bx; x++)
            {
                for (int y = cut - 3; y <= cut; y++) if (map.World.GetBlock(x, y, z) == Blocks.Air) air++;
                for (int y = h + 1; y <= h + 3; y++) if (map.World.GetBlock(x, y, z) == Blocks.BuildBlock(BuildMaterial.Stone)) stone++;
            }
        results.Add($"instant_work:jobs_left:{map.Jobs.Count} frames:{frames} dug_air:{air}/80 stone_walls:{stone}/42");
        Metric("tool_check", string.Join(' ', results));
        await Key(Godot.Key.Escape); // back to the Camera tool: no hover box in later shots
    }

    /// <summary>Turns on the instant-work runner at 25× until the queue is empty; returns the frames it took.</summary>
    async Task<int> RunInstantWork()
    {
        var work = _scene.Work;
        work.SpeedMultiplier = 25;
        work.Enabled = true;
        int frames = 0;
        while (_scene.Map.Jobs.Count > 0 && frames < WorkTimeoutFrames) { await NextFrame(); frames++; }
        work.Enabled = false;
        await WaitForRemesh();
        return frames;
    }

    /// <summary>
    /// <c>cave_hidden</c>: the undiscovered cave cell closest to the surface (away from the edge and the house site),
    /// in a cave of at least 20 hidden cells nearby, sliced at its level with fog on: it must look like solid rock.
    /// </summary>
    Task FindHiddenCave(Site avoid)
    {
        var w = _scene.Map.World;
        const int margin = 40;
        int bestDepth = int.MaxValue;
        for (int z = margin; z < w.SizeZ - margin; z++)
            for (int x = margin; x < w.SizeX - margin; x++)
            {
                if (Math.Abs(x - avoid.X) < 48 && Math.Abs(z - avoid.Z) < 48) continue;
                int h = w.GetSurfaceHeight(x, z);
                for (int y = h - 3; y > h - 3 - Math.Min(bestDepth, 60); y--)
                {
                    if (Blocks.Registry.Get(w.GetBlock(x, y, z)).IsSolid || w.IsDiscovered(x, y, z)) continue;
                    if (HiddenAround(x, y, z) >= 20 && h - y < bestDepth) { bestDepth = h - y; _cave = (x, y, z); }
                    break;
                }
            }
        if (bestDepth == int.MaxValue) throw new InvalidOperationException("no hidden cave found");
        Metric("cave", $"({_cave.X},{_cave.Y},{_cave.Z}) depth:{bestDepth} hidden_nearby:{HiddenAround(_cave.X, _cave.Y, _cave.Z)}");
        return View(_cave.X, _cave.Z, _cave.Y, 0, 60, 30);
    }

    int HiddenAround(int cx, int cy, int cz)
    {
        var w = _scene.Map.World;
        int n = 0;
        for (int y = cy - 2; y <= cy + 2; y++)
            for (int z = cz - 4; z <= cz + 4; z++)
                for (int x = cx - 4; x <= cx + 4; x++)
                    if (w.InBounds(x, y, z) && !Blocks.Registry.Get(w.GetBlock(x, y, z)).IsSolid && !w.IsDiscovered(x, y, z)) n++;
        return n;
    }

    /// <summary><c>cave_revealed</c>: a 1×1 shaft from the surface into the cave, completed by instant work, same view.</summary>
    async Task RevealCave()
    {
        var map = _scene.Map;
        var (x, y, z) = _cave;
        int surface = map.World.GetSurfaceHeight(x, z);
        bool before = map.World.IsDiscovered(x, y, z);
        int hiddenBefore = HiddenAround(x, y, z);
        Designator.Apply(map, DesignationTool.Dig, BuildMaterial.Wood, new DragBox(new(x, surface, z), new(x, y + 1, z)));
        int frames = await RunInstantWork();
        Metric("cave_reveal", $"discovered_before:{before} after:{map.World.IsDiscovered(x, y, z)} " +
            $"visible_after:{(map.View.GetVisibleBlock(x, y, z) == Blocks.Air ? "air" : "rock")} " +
            $"hidden_nearby_before:{hiddenBefore} after:{HiddenAround(x, y, z)} frames:{frames}");
        await View(x, z, y, 0, 60, 30);
    }

    Vector2 ScreenOf(Vector3 world) =>
        GetViewport().GetFinalTransform() * _scene.Rig.Camera.UnprojectPosition(world);

    async Task Send(InputEvent e)
    {
        Input.ParseInputEvent(e);
        await Frames(2);
    }

    Task Move(Vector3 world, bool held = false)
    {
        var p = ScreenOf(world);
        return Send(new InputEventMouseMotion { Position = p, GlobalPosition = p, ButtonMask = held ? MouseButtonMask.Left : 0 });
    }

    Task Press(Vector3 world, bool doubleClick = false) => MouseButtonEvent(world, true, doubleClick);

    Task Release(Vector3 world) => MouseButtonEvent(world, false, false);

    Task MouseButtonEvent(Vector3 world, bool pressed, bool doubleClick)
    {
        var p = ScreenOf(world);
        return Send(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left, Pressed = pressed, DoubleClick = doubleClick,
            ButtonMask = pressed ? MouseButtonMask.Left : 0, Position = p, GlobalPosition = p,
        });
    }

    async Task Key(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = false });
        await Frames(2);
    }

    (int X, int Z) HighestColumn()
    {
        var w = _scene.Map.World;
        (int, int) best = (0, 0);
        int top = -1;
        for (int z = 0; z < w.SizeZ; z++)
            for (int x = 0; x < w.SizeX; x++)
                if (w.GetSurfaceHeight(x, z) > top) { top = w.GetSurfaceHeight(x, z); best = (x, z); }
        return best;
    }

    string MetricsPath => Path.Combine(_dir, "metrics.txt");

    /// <summary>Appends a <c>name=value</c> line to metrics.txt.</summary>
    public void Metric(string name, object value)
    {
        string text = value is IFormattable f ? f.ToString(value is double or float ? "0.##" : null, CultureInfo.InvariantCulture) : value.ToString() ?? "";
        File.AppendAllText(MetricsPath, $"{name}={text}\n");
    }

    async Task Shoot(string name)
    {
        // Let the new view settle, then grab right after a frame has been drawn.
        await Frames(2);
        await _tree.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var img = _tree.Root.GetTexture().GetImage();
        string path = Path.Combine(_dir, name + ".png");
        var err = img.SavePng(path);
        if (err != Error.Ok) throw new IOException($"saving {path} failed ({err})");
        GD.Print($"VisualShots: wrote {path}");
    }

    public async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await NextFrame();
    }

    public async Task NextFrame() => await _tree.ToSignal(_tree, SceneTree.SignalName.ProcessFrame);
}
