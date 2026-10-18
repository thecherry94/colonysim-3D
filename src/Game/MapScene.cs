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
    public CameraRig Rig { get; private set; } = null!;

    /// <summary>How long the fallback generation took, or null when the map came from <see cref="GameSession"/>.</summary>
    public double? GenerateMs { get; private set; }

    public override void _Ready()
    {
        InputActions.Register();
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

        Rig = new CameraRig { Name = "CameraRig" };
        AddChild(Rig);
        Rig.Setup(Map);

        if (shots)
        {
            var visualShots = new VisualShots { Name = "VisualShots" };
            visualShots.Setup(this, shotsDir);
            AddChild(visualShots);
        }
    }

    static MapSize ParseSize(string value) =>
        Enum.TryParse<MapSize>(value, ignoreCase: true, out var size) ? size : MapSize.Small;
}
