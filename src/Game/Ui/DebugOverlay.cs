using Godot;

namespace ColonySim.Game;

/// <summary>
/// Debug overlay (top right): FPS, meshed/total chunks, meshing queue, last level-change time and queued jobs, colonists, tick time, pending paths and navigation.
/// F3 toggles it; it starts on in debug builds. F9 spawns <see cref="MapScene.DebugSpawnCount"/> colonists at home, shown or not.
/// </summary>
public partial class DebugOverlay : CanvasLayer
{
    const double RefreshSeconds = 0.25;

    MapScene _scene = null!;
    Label _stats = null!;
    double _sinceRefresh = RefreshSeconds;

    public void Setup(MapScene scene) => _scene = scene;

    /// <summary>The text currently shown.</summary>
    public string Stats => _stats.Text;

    public override void _Ready()
    {
        Layer = 2; // above the HUD
        var panel = Hud.Panel("DebugPanel", 10);
        panel.MouseFilter = Control.MouseFilterEnum.Ignore;
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        panel.GrowHorizontal = Control.GrowDirection.Begin;
        panel.OffsetRight = -12;
        panel.OffsetTop = 12;
        _stats = Hud.Text("Stats", 13, Hud.TextColor);
        _stats.CustomMinimumSize = new Vector2(230, 0); // stops the panel jittering as the numbers change width
        panel.AddChild(_stats);
        AddChild(panel);
        Visible = OS.IsDebugBuild();
        Refresh();
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        _sinceRefresh += delta;
        if (_sinceRefresh >= RefreshSeconds) Refresh();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed(InputActions.DebugOverlay)) Visible = !Visible;
        else if (@event.IsActionPressed(InputActions.DebugSpawnColonists)) _scene.SpawnColonists(MapScene.DebugSpawnCount);
        else return;
        if (Visible) Refresh();
        GetViewport().SetInputAsHandled();
    }

    void Refresh()
    {
        _sinceRefresh = 0;
        var chunks = _scene.Chunks;
        var sim = _scene.SimRunner.Sim;
        _stats.Text =
            $"FPS {Engine.GetFramesPerSecond():0}\n" +
            $"Chunks meshed {chunks.MeshedChunks} / {chunks.TotalChunks}\n" +
            $"Meshing queue {chunks.PendingChunks}\n" +
            $"Level change {_scene.Slice.LastLevelChangeMs:0.0} ms\n" +
            $"Jobs {_scene.Map.Jobs.Count}\n" +
            $"Colonists {_scene.Map.Colony.Colonists.Count} (F9 +{MapScene.DebugSpawnCount})\n" +
            $"Tick {_scene.SimRunner.AvgTickMs:0.00} ms avg · {_scene.SimRunner.LastTickMs:0.00} last\n" +
            $"Pending paths {sim.Paths.Pending}\n" +
            $"Nav sections {sim.Nav.SectionCount} · version {sim.Nav.Version}";
    }
}
