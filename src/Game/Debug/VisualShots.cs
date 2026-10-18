using System.Diagnostics;
using System.Globalization;
using ColonySim.Core;
using Godot;

namespace ColonySim.Game;

/// <summary>
/// Screenshot harness, active when <c>COLONYSIM_SHOTS=&lt;dir&gt;</c> is set. Waits for the initial meshing,
/// runs each named scenario, saves <c>&lt;dir&gt;/&lt;name&gt;.png</c>, writes <c>name=value</c> lines to
/// <c>&lt;dir&gt;/metrics.txt</c> and quits.
/// </summary>
public partial class VisualShots : Node
{
    const int SettleFrames = 5;
    const double MeshTimeoutSeconds = 300;

    const int MidLevel = 100;
    const int LevelSteps = 20;
    const int CaveBand = 8;

    MapScene _scene = null!;
    string _dir = "";
    long _meshStart;

    /// <summary>
    /// Scenarios in run order: each sets up the view; the harness then shoots it. A null name runs the
    /// setup without a shot (measurements).
    /// </summary>
    List<(string? Name, Func<Task> Setup)> Scenarios()
    {
        var world = _scene.Map.World;
        var (caveX, caveZ) = CaveFocus();
        var (peakX, peakZ) = HighestColumn();
        // The sun shines toward +X/-Z; looking from that side shows the shadows cast toward the camera.
        const float shadowYaw = 145;
        return
        [
            ("overview", () => Task.CompletedTask),
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
                _scene.Map.View.FogEnabled = false;
                world.MarkAllDirty();
                await WaitForRemesh();
                await View(caveX, caveZ, MidLevel, 0, 60, 70);
            }),
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
            GetTree().Quit();
        }
        catch (Exception e)
        {
            GD.PushError($"VisualShots failed: {e}");
            GetTree().Quit(1);
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
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var img = GetViewport().GetTexture().GetImage();
        string path = Path.Combine(_dir, name + ".png");
        var err = img.SavePng(path);
        if (err != Error.Ok) throw new IOException($"saving {path} failed ({err})");
        GD.Print($"VisualShots: wrote {path}");
    }

    public async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await NextFrame();
    }

    public async Task NextFrame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
}
