using ColonySim.Core;
using Godot;

namespace ColonySim.Game;

/// <summary>
/// The work priorities grid (P toggles it): colonists down, work types across. A cell shows the colonist's priority for
/// that work (1 = first, 4 = last, "–" = off) and is tinted by its skill level; a left click cycles 1 → 2 → 3 → 4 → off → 1,
/// a right click backwards. Cell tooltips show the skill.
/// </summary>
public partial class WorkPrioritiesWindow : PanelContainer
{
    static readonly Color OffColor = Color.FromHtml("#232833");
    static readonly Color UnskilledColor = Color.FromHtml("#4a5160");
    static readonly Color SkillLowColor = Color.FromHtml("#2c3447");
    static readonly Color SkillHighColor = Color.FromHtml("#38c172");

    sealed class Cell(Colonist colonist, int work, Button button)
    {
        public Colonist Colonist = colonist;
        public int Work = work;
        public Button Button = button;
        public (int Priority, int Level) Shown = (-1, -1);
    }

    MapScene _scene = null!;
    GridContainer _grid = null!;
    readonly List<Colonist> _rows = new();
    readonly List<Cell> _cells = new();

    internal IReadOnlyList<Button> CellButtons => _cells.Select(c => c.Button).ToList();

    public void Setup(MapScene scene)
    {
        _scene = scene;
        Name = "WorkPriorities";
        AddThemeStyleboxOverride("panel", Hud.Box(new Color(20 / 255f, 24 / 255f, 32 / 255f, 0.96f), Color.FromHtml("#333a48"), 10, 16, 12));
        SetAnchorsAndOffsetsPreset(LayoutPreset.Center, LayoutPresetMode.Minsize);
        GrowHorizontal = GrowDirection.Both;
        GrowVertical = GrowDirection.Both;
        Visible = false;

        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 8);
        var header = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        var title = Hud.Text("Title", 16, Hud.TextColor);
        title.Text = "Work priorities [P]";
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(title);
        var close = Hud.ActionButton("✕", () => Visible = false);
        header.AddChild(close);
        box.AddChild(header);
        var hint = Hud.Text("Hint", 12, Hud.DimTextColor);
        hint.Text = "1 = first, 4 = last, – = off. Left click raises the number, right click lowers it.";
        box.AddChild(hint);
        _grid = new GridContainer { Name = "Grid", Columns = WorkTypes.All.Count + 1, MouseFilter = MouseFilterEnum.Ignore };
        _grid.AddThemeConstantOverride("h_separation", 6);
        _grid.AddThemeConstantOverride("v_separation", 6);
        box.AddChild(_grid);
        AddChild(box);
    }

    public void Toggle() => Visible = !Visible;

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!@event.IsActionPressed(InputActions.WorkPriorities)) return;
        Toggle();
        GetViewport().SetInputAsHandled();
    }

    /// <summary>The next priority: 1 → 2 → 3 → 4 → 0 (off) → 1, or backwards.</summary>
    public static int Cycle(int priority, bool forward) =>
        forward ? (priority + 1) % 5 : (priority + 4) % 5;

    public override void _Process(double delta)
    {
        if (_scene == null || !Visible) return;
        var colonists = _scene.Map.Colony.Colonists;
        if (!_rows.SequenceEqual(colonists)) Rebuild(colonists);
        foreach (var cell in _cells) Refresh(cell);
    }

    void Rebuild(List<Colonist> colonists)
    {
        foreach (var child in _grid.GetChildren()) { _grid.RemoveChild(child); child.QueueFree(); }
        _rows.Clear();
        _rows.AddRange(colonists);
        _cells.Clear();
        _grid.AddChild(Hud.Text("Corner", 13, Hud.DimTextColor));
        foreach (var work in WorkTypes.All)
        {
            var label = Hud.Text($"Head{work.Id}", 13, Hud.TextColor);
            label.Text = work.Label;
            label.HorizontalAlignment = HorizontalAlignment.Center;
            _grid.AddChild(label);
        }
        foreach (var colonist in colonists)
        {
            var name = Hud.Text($"Row{colonist.Id}", 13, Hud.TextColor);
            name.Text = colonist.Name;
            name.CustomMinimumSize = new Vector2(90, 0);
            _grid.AddChild(name);
            for (int w = 0; w < WorkTypes.All.Count; w++)
            {
                var button = new Button
                {
                    Name = $"Cell{colonist.Id}_{WorkTypes.All[w].Id}", FocusMode = FocusModeEnum.None,
                    CustomMinimumSize = new Vector2(84, 34),
                };
                button.AddThemeFontSizeOverride("font_size", 15);
                var cell = new Cell(colonist, w, button);
                button.GuiInput += e =>
                {
                    if (e is not InputEventMouseButton { Pressed: true } press) return;
                    if (press.ButtonIndex is not (MouseButton.Left or MouseButton.Right)) return;
                    colonist.Priorities[cell.Work] = Cycle(colonist.Priorities[cell.Work], press.ButtonIndex == MouseButton.Left);
                    _scene.Map.MarkColonyChanged();
                    button.AcceptEvent();
                };
                _cells.Add(cell);
                _grid.AddChild(button);
            }
        }
    }

    void Refresh(Cell cell)
    {
        var work = WorkTypes.All[cell.Work];
        int priority = cell.Colonist.Priorities[cell.Work];
        int level = work.Skill is { } skill ? cell.Colonist.Skills[(int)skill].Level : -1;
        if (cell.Shown == (priority, level)) return;
        cell.Shown = (priority, level);

        var button = cell.Button;
        button.Text = priority == 0 ? "–" : priority.ToString();
        button.TooltipText = work.Skill is { } s ? $"{s} skill {level}" : "No skill";
        Color tint = level < 0 ? UnskilledColor : SkillLowColor.Lerp(SkillHighColor, level / (float)Skills.MaxLevel);
        if (priority == 0) tint = tint.Lerp(OffColor, 0.7f);
        var box = Hud.Box(tint, Color.FromHtml("#44506a"), 6, 8, 4);
        button.AddThemeStyleboxOverride("normal", box);
        button.AddThemeStyleboxOverride("hover", Hud.Box(tint.Lightened(0.15f), Color.FromHtml("#8aa0c8"), 6, 8, 4));
        button.AddThemeStyleboxOverride("pressed", box);
        button.AddThemeColorOverride("font_color", priority == 0 ? Hud.DimTextColor : Hud.TextColor);
        button.AddThemeColorOverride("font_hover_color", Hud.TextColor);
    }
}
