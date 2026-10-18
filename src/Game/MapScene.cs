using System.Diagnostics;
using ColonySim.Core;
using Godot;

namespace ColonySim.Game;

/// <summary>Root of the map scene: owns the map, the lighting and the chunk renderer.</summary>
public partial class MapScene : Node3D
{
    public const string ShotsEnv = "COLONYSIM_SHOTS";
    public const string ShotsSizeEnv = "COLONYSIM_SHOTS_SIZE";
    const int ShotsSeed = 12345;
    const int DefaultSeed = 1;

    public ColonyMap Map { get; private set; } = null!;
    public ChunkRenderer Chunks { get; private set; } = null!;

    /// <summary>How long the fallback generation took, or null when the map came from <see cref="GameSession"/>.</summary>
    public double? GenerateMs { get; private set; }

    public override void _Ready()
    {
        string shotsDir = OS.GetEnvironment(ShotsEnv);
        bool shots = !string.IsNullOrEmpty(shotsDir);

        // Fallback so the scene also runs on its own (F6) and in the screenshot harness.
        if (GameSession.Map == null)
        {
            var settings = shots
                ? MapSettings.Create(ShotsSeed, ParseSize(OS.GetEnvironment(ShotsSizeEnv)))
                : MapSettings.Create(DefaultSeed, MapSize.Small);
            long start = Stopwatch.GetTimestamp();
            GameSession.Map = MapGenerator.Generate(settings);
            GenerateMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        Map = GameSession.Map;

        AddChild(new DirectionalLight3D
        {
            Name = "Sun",
            RotationDegrees = new Vector3(-55, -35, 0),
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 600,
        });

        var sky = Color.FromHtml("#9cc7e8");
        AddChild(new WorldEnvironment
        {
            Name = "Environment",
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = sky,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = sky,
                AmbientLightEnergy = 0.6f,
            },
        });

        Chunks = new ChunkRenderer { Name = "Chunks" };
        AddChild(Chunks);
        Chunks.Initialize(Map);

        AddOverviewCamera();

        if (shots)
        {
            var visualShots = new VisualShots { Name = "VisualShots" };
            visualShots.Setup(this, shotsDir);
            AddChild(visualShots);
        }
    }

    /// <summary>Temporary fixed camera looking at the map center; replaced by the camera rig.</summary>
    void AddOverviewCamera()
    {
        var w = Map.World;
        var camera = new Camera3D { Name = "Camera", Far = 2000, Current = true };
        AddChild(camera);
        var center = new Vector3(w.SizeX / 2f, w.GetSurfaceHeight(w.SizeX / 2, w.SizeZ / 2), w.SizeZ / 2f);
        camera.LookAtFromPosition(new Vector3(0.3f * w.SizeX, 220, 1.3f * w.SizeZ), center, Vector3.Up);
    }

    static MapSize ParseSize(string value) =>
        Enum.TryParse<MapSize>(value, ignoreCase: true, out var size) ? size : MapSize.Small;
}
