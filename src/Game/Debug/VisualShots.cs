using System.Diagnostics;
using System.Globalization;
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

    MapScene _scene = null!;
    string _dir = "";
    long _meshStart;

    /// <summary>Scenarios in run order: each sets up the view; the harness then shoots it.</summary>
    List<(string Name, Func<Task> Setup)> Scenarios() =>
    [
        ("overview", () => Task.CompletedTask),
        ("camera_rotated", () =>
        {
            _scene.Rig.Yaw = 135;
            _scene.Rig.Pitch = 35;
            _scene.Rig.Distance = 80;
            return Task.CompletedTask;
        }),
    ];

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

            while (_scene.Chunks.PendingChunks > 0)
            {
                if (Stopwatch.GetElapsedTime(_meshStart).TotalSeconds > MeshTimeoutSeconds)
                    throw new TimeoutException($"initial meshing did not finish ({_scene.Chunks.PendingChunks} chunks pending)");
                await NextFrame();
            }
            Metric("initial_mesh_ms", Stopwatch.GetElapsedTime(_meshStart).TotalMilliseconds);
            await Frames(SettleFrames);

            foreach (var (name, setup) in Scenarios())
            {
                await setup();
                await Shoot(name);
            }
            GetTree().Quit();
        }
        catch (Exception e)
        {
            GD.PushError($"VisualShots failed: {e}");
            GetTree().Quit(1);
        }
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
