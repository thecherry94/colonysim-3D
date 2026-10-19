using ColonySim.Core;
using Godot;

namespace ColonySim.Game;

/// <summary>
/// In-game HUD: the view-level indicator, the debug view toggles, Save (also Ctrl+S) and Menu with their messages
/// (top left), the tool status line (bottom center) and the toolbar (bottom): time controls with the day, tools, build material, debug instant work
/// and the queued job count. Menu asks first when blocks or jobs changed since the map was saved, loaded or generated. HUD buttons never take keyboard focus, so Tab, Esc and the number keys always reach
/// the tools.
/// </summary>
public partial class Hud : CanvasLayer
{
    static readonly Color PanelColor = new(20 / 255f, 24 / 255f, 32 / 255f, 0.93f);
    static readonly Color BorderColor = Color.FromHtml("#333a48");
    internal static readonly Color TextColor = Color.FromHtml("#e6e6e6");
    internal static readonly Color DimTextColor = Color.FromHtml("#9fb0d0");
    static readonly Color ButtonColor = Color.FromHtml("#2b3242");
    static readonly Color ButtonBorderColor = Color.FromHtml("#44506a");
    static readonly Color ButtonHoverColor = Color.FromHtml("#38425a");
    internal static readonly Color ActiveColor = Color.FromHtml("#f3c64d");
    internal static readonly Color ActiveTextColor = Color.FromHtml("#1b1f27");
    internal static readonly Color ErrorColor = Color.FromHtml("#ff6b6b");
    static readonly Color WorkOnColor = Color.FromHtml("#27ae60");
    const double MessageSeconds = 3, ErrorSeconds = 8;
    const string SaveAndLeaveAction = "save_and_leave";

    static readonly (DesignationTool? Tool, string Key)[] ToolButtons =
    [
        (null, "Esc"), (DesignationTool.Dig, "Z"), (DesignationTool.DigStairs, "X"), (DesignationTool.Wall, "C"),
        (DesignationTool.Floor, "V"), (DesignationTool.Stairs, "B"), (DesignationTool.Ramp, "N"),
        (DesignationTool.Ladder, "M"), (DesignationTool.Cancel, "Del"),
    ];

    MapScene _scene = null!;
    Label _level = null!, _status = null!, _jobs = null!, _message = null!;
    double _messageLeft;
    CheckBox _caps = null!, _fog = null!;
    readonly List<(Button Button, DesignationTool? Tool)> _tools = new();
    readonly List<(Button Button, BuildMaterial Material)> _materials = new();
    readonly List<(Button Button, int Speed)> _speeds = new();
    Button _work = null!, _pause = null!;
    Label _day = null!;
    readonly List<(Button Button, int Speed)> _simSpeeds = new();
    (int World, int Jobs) _savedVersion;

    public void Setup(MapScene scene) => _scene = scene;

    /// <summary>Returns to the new-game screen; exposed for the screenshot harness.</summary>
    internal Button MenuButton { get; private set; } = null!;

    /// <summary>"Leave without saving?", shown by Menu when there are unsaved changes; exposed for the harness.</summary>
    internal ConfirmationDialog LeaveDialog { get; private set; } = null!;

    /// <summary>Blocks, discovery or jobs changed since the map was saved, loaded or generated.</summary>
    public bool HasUnsavedChanges => CurrentVersion() != _savedVersion;

    /// <summary>The save/error message currently shown, or empty.</summary>
    public string Message => _message.Visible ? _message.Text : "";

    public override void _Ready()
    {
        AddChild(BuildTopLeft());
        AddChild(BuildBottom());
        AddChild(BuildLeaveDialog());
        // The scene starts on a freshly generated or loaded map: nothing to save yet.
        _savedVersion = CurrentVersion();
        _scene.Tools.StatusChanged += OnStatusChanged;
        OnStatusChanged();
    }

    public override void _ExitTree()
    {
        if (_scene?.Tools != null) _scene.Tools.StatusChanged -= OnStatusChanged;
    }

    public override void _Process(double delta)
    {
        if (_scene == null) return;
        // Depth is relative to the ground under the camera focus, which changes as the camera pans.
        SetText(_level, LevelText(_scene.Slice, _scene.Rig.SurfaceAtFocus()));
        int count = _scene.Map.Jobs.Count;
        SetText(_jobs, count == 0 ? "no jobs queued" : count == 1 ? "1 job queued" : $"{count} jobs queued");

        // State can also change from hotkeys, so the buttons mirror it every frame.
        foreach (var (button, tool) in _tools) button.SetPressedNoSignal(_scene.Tools.ActiveTool == tool);
        foreach (var (button, material) in _materials) button.SetPressedNoSignal(_scene.Tools.Material == material);
        _pause.SetPressedNoSignal(_scene.SimRunner.Speed == 0);
        foreach (var (button, speed) in _simSpeeds) button.SetPressedNoSignal(_scene.SimRunner.Speed == speed);
        SetText(_day, $"Day {SimClock.Day(_scene.Map.Colony.Tick)}");
        foreach (var (button, speed) in _speeds) button.SetPressedNoSignal(_scene.Work.SpeedMultiplier == speed);
        _work.SetPressedNoSignal(_scene.Work.Enabled);
        SetText(_work, _scene.Work.Enabled ? "⏸ Instant work (debug)" : "▶ Instant work (debug)");
        _caps.SetPressedNoSignal(_scene.Slice.CapsEnabled);
        _fog.SetPressedNoSignal(_scene.Slice.FogEnabled);

        if (_message.Visible && (_messageLeft -= delta) <= 0) _message.Visible = false;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!@event.IsActionPressed(InputActions.Save)) return;
        SaveGame();
        GetViewport().SetInputAsHandled();
    }

    /// <summary>Saves the map and shows "Saved {file}", or the error in red; returns whether it saved.</summary>
    bool SaveGame()
    {
        try
        {
            var version = CurrentVersion();
            ShowMessage($"Saved {SaveGames.Save(_scene.Map)}", error: false);
            _savedVersion = version;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowMessage($"Save failed: {e.Message}", error: true);
            return false;
        }
    }

    (int, int) CurrentVersion() => (_scene.Map.World.EditVersion, _scene.Map.Jobs.Version);

    void OnMenuPressed()
    {
        if (HasUnsavedChanges) LeaveDialog.PopupCentered();
        else ReturnToMenu();
    }

    ConfirmationDialog BuildLeaveDialog()
    {
        LeaveDialog = new ConfirmationDialog
        {
            Name = "LeaveDialog",
            Title = "Unsaved changes",
            DialogText = "Leave without saving?",
            OkButtonText = "Leave",
        };
        LeaveDialog.AddButton("Save & leave", right: false, action: SaveAndLeaveAction);
        LeaveDialog.Confirmed += ReturnToMenu;
        LeaveDialog.CustomAction += action =>
        {
            if (action != SaveAndLeaveAction) return;
            LeaveDialog.Hide();
            if (SaveGame()) ReturnToMenu();
        };
        return LeaveDialog;
    }

    void ShowMessage(string text, bool error)
    {
        _message.Text = text;
        _message.AddThemeColorOverride("font_color", error ? ErrorColor : TextColor);
        _message.Visible = true;
        _messageLeft = error ? ErrorSeconds : MessageSeconds;
    }

    void ReturnToMenu()
    {
        GameSession.Map = null;
        GetTree().ChangeSceneToFile(NewGameScreen.ScenePath);
    }

    Control BuildTopLeft()
    {
        var panel = Panel("TopLeftPanel", 10);
        panel.Position = new Vector2(12, 12);
        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 4);
        _level = Text("Level", 15, TextColor);
        box.AddChild(_level);
        _caps = Toggle("Cut caps", on => _scene.Slice.CapsEnabled = on);
        _fog = Toggle("Fog of war", on => _scene.Slice.FogEnabled = on);
        box.AddChild(_caps);
        box.AddChild(_fog);
        var buttons = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        buttons.AddThemeConstantOverride("separation", 6);
        buttons.AddChild(ActionButton("Save [Ctrl+S]", () => SaveGame()));
        MenuButton = ActionButton("Menu", OnMenuPressed);
        buttons.AddChild(MenuButton);
        box.AddChild(buttons);
        _message = Text("Message", 13, TextColor);
        _message.Visible = false;
        box.AddChild(_message);
        panel.AddChild(box);
        return panel;
    }

    Control BuildBottom()
    {
        // Full-width strip anchored to the bottom; children centered in it.
        var column = new VBoxContainer
        {
            Name = "Bottom",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Alignment = BoxContainer.AlignmentMode.End,
        };
        column.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide);
        column.GrowVertical = Control.GrowDirection.Begin;
        column.OffsetBottom = -12;
        column.AddThemeConstantOverride("separation", 8);

        var statusPanel = Panel("StatusPanel", 8);
        statusPanel.MouseFilter = Control.MouseFilterEnum.Ignore;
        statusPanel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        _status = Text("Status", 13, TextColor);
        statusPanel.AddChild(_status);
        column.AddChild(statusPanel);

        var toolbar = Panel("Toolbar", 10);
        toolbar.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        var rows = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        rows.AddThemeConstantOverride("separation", 6);
        rows.AddChild(BuildTimeControls());
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 6);

        var toolGroup = new ButtonGroup();
        foreach (var (tool, key) in ToolButtons)
        {
            var button = ToggleButton($"{ToolController.ToolName(tool)} [{key}]", toolGroup);
            button.Pressed += () => _scene.Tools.ActiveTool = tool;
            _tools.Add((button, tool));
            row.AddChild(button);
        }

        row.AddChild(new VSeparator());
        var materialGroup = new ButtonGroup();
        foreach (var material in Enum.GetValues<BuildMaterial>())
        {
            var button = ToggleButton(material.ToString(), materialGroup);
            button.Pressed += () => _scene.Tools.Material = material;
            _materials.Add((button, material));
            row.AddChild(button);
        }
        row.AddChild(Text("Tab", 12, DimTextColor));

        row.AddChild(new VSeparator());
        _work = ToggleButton("▶ Instant work (debug)", null);
        StyleButton(_work, WorkOnColor, TextColor);
        _work.Toggled += on => _scene.Work.Enabled = on;
        row.AddChild(_work);
        var speedGroup = new ButtonGroup();
        foreach (int speed in InstantWorkRunner.Speeds)
        {
            var button = ToggleButton($"{speed}×", speedGroup);
            button.Pressed += () => _scene.Work.SpeedMultiplier = speed;
            _speeds.Add((button, speed));
            row.AddChild(button);
        }

        row.AddChild(new VSeparator());
        _jobs = Text("Jobs", 13, DimTextColor);
        _jobs.CustomMinimumSize = new Vector2(130, 0);
        row.AddChild(_jobs);

        rows.AddChild(row);
        toolbar.AddChild(rows);
        column.AddChild(toolbar);
        return column;
    }

    /// <summary>The toolbar's top row, right-aligned: pause, 1× 2× 3× and the day.</summary>
    Control BuildTimeControls()
    {
        var row = new HBoxContainer { Name = "TimeControls", MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.End };
        row.AddThemeConstantOverride("separation", 6);
        _day = Text("Day", 13, TextColor);
        _day.CustomMinimumSize = new Vector2(64, 0);
        row.AddChild(_day);
        var group = new ButtonGroup();
        _pause = ToggleButton("⏸", group);
        _pause.Pressed += () => _scene.SimRunner.Speed = 0;
        row.AddChild(_pause);
        for (int speed = 1; speed <= SimRunner.MaxSpeed; speed++)
        {
            int s = speed;
            var button = ToggleButton($"{s}×", group);
            button.Pressed += () => _scene.SimRunner.Speed = s;
            _simSpeeds.Add((button, s));
            row.AddChild(button);
        }
        row.AddChild(Text("Space", 12, DimTextColor));
        return row;
    }

    void OnStatusChanged() => SetText(_status, _scene.Tools.Status);

    static void SetText(Label label, string text)
    {
        if (label.Text != text) label.Text = text;
    }

    static void SetText(Button button, string text)
    {
        if (button.Text != text) button.Text = text;
    }

    internal static PanelContainer Panel(string name, int radius)
    {
        var panel = new PanelContainer { Name = name };
        panel.AddThemeStyleboxOverride("panel", Box(PanelColor, BorderColor, radius, 14, 8));
        return panel;
    }

    internal static Label Text(string name, int size, Color color)
    {
        var label = new Label { Name = name, MouseFilter = Control.MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    static CheckBox Toggle(string text, Action<bool> onToggled)
    {
        var box = new CheckBox { Text = text, FocusMode = Control.FocusModeEnum.None };
        box.AddThemeFontSizeOverride("font_size", 13);
        box.AddThemeColorOverride("font_color", TextColor);
        box.Toggled += on => onToggled(on);
        return box;
    }

    static Button ActionButton(string text, Action onPressed)
    {
        var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
        button.AddThemeFontSizeOverride("font_size", 13);
        StyleButton(button, ActiveColor, ActiveTextColor);
        button.Pressed += onPressed;
        return button;
    }

    static Button ToggleButton(string text, ButtonGroup? group)
    {
        var button = new Button { Text = text, ToggleMode = true, ButtonGroup = group, FocusMode = Control.FocusModeEnum.None };
        button.AddThemeFontSizeOverride("font_size", 13);
        StyleButton(button, ActiveColor, ActiveTextColor);
        return button;
    }

    /// <summary>Prototype styling: dark buttons, the pressed (active) state filled with <paramref name="active"/>.</summary>
    internal static void StyleButton(Button button, Color active, Color activeText)
    {
        button.AddThemeStyleboxOverride("normal", Box(ButtonColor, ButtonBorderColor, 6, 8, 4));
        button.AddThemeStyleboxOverride("hover", Box(ButtonHoverColor, ButtonBorderColor, 6, 8, 4));
        button.AddThemeStyleboxOverride("pressed", Box(active, active, 6, 8, 4));
        button.AddThemeStyleboxOverride("hover_pressed", Box(active, active, 6, 8, 4));
        button.AddThemeColorOverride("font_color", TextColor);
        button.AddThemeColorOverride("font_hover_color", TextColor);
        button.AddThemeColorOverride("font_pressed_color", activeText);
        button.AddThemeColorOverride("font_hover_pressed_color", activeText);
    }

    static StyleBoxFlat Box(Color bg, Color border, int radius, int marginX, int marginY) => new()
    {
        BgColor = bg,
        BorderColor = border,
        BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
        CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
        ContentMarginLeft = marginX, ContentMarginRight = marginX, ContentMarginTop = marginY, ContentMarginBottom = marginY,
    };

    static string LevelText(SliceController slice, int surface)
    {
        if (!slice.IsSliced) return "View level: top (nothing hidden)";
        int rel = slice.ViewLevel - surface;
        string depth = rel < 0 ? $"{-rel} below ground" : rel == 0 ? "ground level" : $"{rel} above ground";
        return $"View level: Y {slice.ViewLevel} · {depth}";
    }
}
