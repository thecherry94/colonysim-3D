using System.Diagnostics;
using ColonySim.Core;
using Godot;

namespace ColonySim.Game;

/// <summary>
/// Root of the map scene: owns the map, the lighting, the renderers, the camera, the slice view, the designation
/// tools with their overlay, instant work, the simulation with its colonists, the HUD and the debug overlay.
/// </summary>
public partial class MapScene : Node3D
{
    public const string ScenePath = "res://scenes/map.tscn";
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
    public SimRunner SimRunner { get; private set; } = null!;
    public ColonistRenderer ColonistRenderer { get; private set; } = null!;
    public Hud Hud { get; private set; } = null!;
    public DebugOverlay DebugOverlay { get; private set; } = null!;

    /// <summary>The selected colonist (shown in the info panel), or null.</summary>
    public Colonist? Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            bool following = Following;
            _selected = value;
            // Following continues with the newly selected colonist, and ends with the selection.
            Following = following && value != null;
            SelectionChanged?.Invoke();
        }
    }

    public event Action? SelectionChanged;

    /// <summary>Whether the camera follows the selected colonist (and the view level its floor, when sliced).</summary>
    public bool Following
    {
        get => Rig.IsFollowing;
        set
        {
            if (value && _selected is { } c)
            {
                Rig.Follow(() => ColonistRenderer.RenderPosition(c));
                Slice.FollowFeetY = () => c.Cell.Y;
            }
            else
            {
                Rig.Follow(null);
                Slice.FollowFeetY = null;
            }
        }
    }

    Colonist? _selected;

    /// <summary>How long the fallback generation took, or null when the map came from <see cref="GameSession"/>.</summary>
    public double? GenerateMs { get; private set; }

    public override void _Ready()
    {
        InputActions.Register();
        string shotsDir = OS.GetEnvironment(ShotsEnv);
        bool shots = !string.IsNullOrEmpty(shotsDir);
        // The harness enters without a map; maps it later opens through the menu don't start it again.
        bool startHarness = shots && GameSession.Map == null;

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

        // Building the simulation builds navigation (up to half a second on Large): here, not mid-game. It also has to
        // exist before instant work runs, which checks for colonists in the way through the simulation's body map.
        SimRunner = new SimRunner { Name = "SimRunner" };
        AddChild(SimRunner);
        SimRunner.Setup(new Simulation(Map));
        // The harness's step 1 scenarios edit the world and its job queue directly: colonists stay out of them.
        if (shots) SimRunner.Speed = 0;

        ColonistRenderer = new ColonistRenderer { Name = "Colonists" };
        AddChild(ColonistRenderer);
        ColonistRenderer.Setup(Map, SimRunner, Slice);

        Work = new InstantWorkRunner { Name = "InstantWork" };
        AddChild(Work);
        Work.Setup(Map);

        Hud = new Hud { Name = "Hud" };
        Hud.Setup(this);
        AddChild(Hud);

        DebugOverlay = new DebugOverlay { Name = "DebugOverlay" };
        DebugOverlay.Setup(this);
        AddChild(DebugOverlay);

        if (startHarness)
        {
            var visualShots = new VisualShots { Name = "VisualShots" };
            visualShots.Setup(this, shotsDir);
            AddChild(visualShots);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!@event.IsActionPressed(InputActions.FollowColonist)) return;
        if (_selected != null) Following = !Following;
        GetViewport().SetInputAsHandled();
    }

    static MapSize ParseSize(string value) =>
        Enum.TryParse<MapSize>(value, ignoreCase: true, out var size) ? size : MapSize.Small;
}
