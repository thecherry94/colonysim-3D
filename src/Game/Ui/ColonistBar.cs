using ColonySim.Core;
using Godot;

namespace ColonySim.Game;

/// <summary>
/// The colonist bar (top center): one button per colonist with its name and what it is doing. Click selects the colonist
/// and centers the camera on it, double-click follows it. A stuck colonist shows "⚠ stuck" in red. The Work button opens
/// the priorities window.
/// </summary>
public partial class ColonistBar : HBoxContainer
{
    MapScene _scene = null!;
    readonly List<(Colonist Colonist, Button Button, Label Label, Label Activity)> _entries = new();

    readonly Dictionary<int, (bool Selected, bool Stuck)> _styled = new();

    internal IReadOnlyList<(Colonist Colonist, Button Button, Label Label, Label Activity)> Entries => _entries;

    public void Setup(MapScene scene, Action toggleWork)
    {
        _scene = scene;
        Name = "ColonistBar";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop, LayoutPresetMode.Minsize);
        GrowHorizontal = GrowDirection.Both;
        OffsetTop = 12;
        AddThemeConstantOverride("separation", 6);
        var work = Hud.ActionButton("Work [P]", toggleWork);
        work.Name = "WorkButton";
        AddChild(work);
    }

    /// <summary>The activity word shown for a colonist: Idle, Walking, Digging, Building or Falling.</summary>
    public static string ActivityLabel(ColonyMap map, Colonist c)
    {
        if (c.Stuck) return "⚠ stuck";
        return c.Activity switch
        {
            ColonistActivity.Walking => "Walking",
            ColonistActivity.Falling => "Falling",
            ColonistActivity.Working when map.Jobs.TryGetById(c.JobId, out var job) =>
                job.Kind is JobKind.Dig or JobKind.CarveStairs ? "Digging" : "Building",
            ColonistActivity.Working => "Working",
            _ => "Idle",
        };
    }

    public override void _Process(double delta)
    {
        if (_scene == null) return;
        var colonists = _scene.Map.Colony.Colonists;
        if (!InSync(colonists)) Rebuild(colonists);
        foreach (var (c, button, name, activity) in _entries)
        {
            bool selected = _scene.Selected == c;
            button.SetPressedNoSignal(selected);
            string text = ActivityLabel(_scene.Map, c);
            if (activity.Text != text) activity.Text = text;
            bool stuck = c.Stuck;
            if (!_styled.TryGetValue(c.Id, out var shown) || shown != (selected, stuck))
            {
                _styled[c.Id] = (selected, stuck);
                name.AddThemeColorOverride("font_color", selected ? Hud.ActiveTextColor : Hud.TextColor);
                activity.AddThemeColorOverride("font_color", stuck ? Hud.ErrorColor : selected ? Hud.ActiveTextColor : Hud.DimTextColor);
            }
        }
    }

    bool InSync(List<Colonist> colonists)
    {
        if (colonists.Count != _entries.Count) return false;
        for (int i = 0; i < colonists.Count; i++)
            if (_entries[i].Colonist != colonists[i]) return false;
        return true;
    }

    void Rebuild(List<Colonist> colonists)
    {
        foreach (var entry in _entries) { RemoveChild(entry.Button); entry.Button.QueueFree(); }
        _entries.Clear();
        _styled.Clear();
        foreach (var c in colonists)
        {
            var button = new Button
            {
                Name = $"Colonist{c.Id}", ToggleMode = true, FocusMode = FocusModeEnum.None,
                CustomMinimumSize = new Vector2(100, 42),
            };
            Hud.StyleButton(button, Hud.ActiveColor, Hud.ActiveTextColor);
            var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Alignment = AlignmentMode.Center };
            column.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            var name = Hud.Text("Name", 13, Hud.TextColor);
            name.Text = c.Name;
            name.HorizontalAlignment = HorizontalAlignment.Center;
            var activity = Hud.Text("Activity", 11, Hud.DimTextColor);
            activity.HorizontalAlignment = HorizontalAlignment.Center;
            column.AddChild(name);
            column.AddChild(activity);
            button.AddChild(column);
            var colonist = c;
            button.GuiInput += e =>
            {
                if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press) return;
                _scene.Selected = colonist;
                if (press.DoubleClick) _scene.Following = true;
                else
                {
                    var feet = _scene.ColonistRenderer.RenderPosition(colonist);
                    _scene.Rig.SetFocus(feet.X, feet.Z);
                }
            };
            AddChild(button);
            MoveChild(button, _entries.Count);
            _entries.Add((c, button, name, activity));
        }
    }
}
