using System.Diagnostics;
using ColonySim.Core;
using Godot;

namespace ColonySim.Game;

/// <summary>
/// Root of the map scene: owns the map, the lighting, the renderers, the camera, the slice view, the designation
/// tools with their overlay, instant work and the HUD.
/// </summary>
public partial class MapScene : Node3D
{
    public const string ShotsEnv = "COLONYSIM_SHOTS";
    public const string ShotsSizeEnv = "COLONYSIM_SHOTS_SIZE";
    const int ShotsSeed = 12345;
    const int DefaultSeed = 1;

    public ColonyMap Map { get; private set; } = null!;
    public ChunkRenderer Chunks { get; private set; } = null!;
    public CameraRig Rig { get; private set; } = null!;
    public CapRenderer Caps { get; private set; } = null!;
    public SliceController Slice { get; private set; } = null!;
    public DesignationOverlay Overlay { get; private set; } = null!;
    public ToolController Tools { get; private set; } = null!;
    public InstantWorkRunner Work { get; private set; } = null!;
    public Hud Hud { get; private set; } = null!;

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

        Caps = new CapRenderer { Name = "Caps" };
        AddChild(Caps);
        Caps.Setup(Map);

        Slice = new SliceController { Name = "Slice" };
        AddChild(Slice);
        Slice.Setup(Map, Chunks, Caps, Rig);
        Slice.SetLevel(Slice.TopLevel);

        Overlay = new DesignationOverlay { Name = "Overlay" };
        AddChild(Overlay);
        Overlay.Setup(Map);

        Tools = new ToolController { Name = "Tools" };
        AddChild(Tools);
        Tools.Setup(this);

        Work = new InstantWorkRunner { Name = "InstantWork" };
        AddChild(Work);
        Work.Setup(Map);

        Hud = new Hud { Name = "Hud" };
        Hud.Setup(this);
        AddChild(Hud);

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
