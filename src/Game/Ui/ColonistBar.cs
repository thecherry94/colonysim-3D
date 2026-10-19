using ColonySim.Core;
using Godot;

namespace ColonySim.Game;

/// <summary>
/// The colonist bar (top center): one button per colonist with its name and what it is doing. Click selects the colonist
/// and centers the camera on it, double-click follows it. A stuck colonist shows "⚠ stuck" in red. The Work button opens
/// the priorities window. Past <see cref="CompactAbove"/> colonists the buttons go compact, and when they don't fit
/// between the top corners' panels they scroll sideways (mouse wheel over the bar). Each frame it only touches the
/// buttons whose text, selection or stuck state changed.
/// </summary>
public partial class ColonistBar : HBoxContainer
{
    /// <summary>Above this many colonists the buttons shrink to <see cref="CompactSize"/> with smaller type.</summary>
    public const int CompactAbove = 10;

    /// <summary>Width (canvas units) kept free on each side of the screen for the top-left panel and the debug overlay.</summary>
    const float SideReserve = 320;
    const int Separation = 6;
    static readonly Vector2 FullSize = new(100, 42), CompactSize = new(66, 34);

    /// <summary>One colonist's button and what it shows, so unchanged state costs no engine calls.</summary>
    internal sealed class Entry(Colonist colonist, Button button, Label name, Label activity)
    {
        public Colonist Colonist { get; } = colonist;
        public Button Button { get; } = button;
        public Label Name { get; } = name;
        public Label Activity { get; } = activity;
        public string ShownActivity = "";
        public bool? ShownSelected, ShownStuck;
    }

    MapScene _scene = null!;
    ScrollContainer _scroll = null!;
    HBoxContainer _list = null!;
    Button _work = null!;
    readonly List<Entry> _entries = new();
    bool _compact;
    float _scrollWidth = -1;

    internal IReadOnlyList<Entry> Entries => _entries;

    /// <summary>The buttons are compact (more than <see cref="CompactAbove"/> colonists).</summary>
    internal bool Compact => _compact;

    /// <summary>The scrolling strip the colonist buttons sit in; exposed for the harness.</summary>
    internal ScrollContainer Scroll => _scroll;

    public void Setup(MapScene scene, Action toggleWork)
    {
        _scene = scene;
        Name = "ColonistBar";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop, LayoutPresetMode.Minsize);
        GrowHorizontal = GrowDirection.Both;
        OffsetTop = 12;
        AddThemeConstantOverride("separation", Separation);
        _scroll = new ScrollContainer
        {
            Name = "Scroll", VerticalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass,
        };
        _list = new HBoxContainer { Name = "List", MouseFilter = MouseFilterEnum.Ignore };
        _list.AddThemeConstantOverride("separation", Separation);
        _scroll.AddChild(_list);
        AddChild(_scroll);
        _work = Hud.ActionButton("Work [P]", toggleWork);
        _work.Name = "WorkButton";
        _work.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        AddChild(_work);
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
        Sync(_scene.Map.Colony.Colonists);
        var selected = _scene.Selected;
        foreach (var e in _entries)
        {
            var c = e.Colonist;
            string text = ActivityLabel(_scene.Map, c);
            if (e.ShownActivity != text)
            {
                e.ShownActivity = text;
                e.Activity.Text = text;
            }
            bool isSelected = selected == c, stuck = c.Stuck;
            if (e.ShownSelected == isSelected && e.ShownStuck == stuck) continue;
            if (isSelected) _scroll.EnsureControlVisible(e.Button);
            e.ShownSelected = isSelected;
            e.ShownStuck = stuck;
            e.Button.SetPressedNoSignal(isSelected);
            e.Name.AddThemeColorOverride("font_color", isSelected ? Hud.ActiveTextColor : Hud.TextColor);
            e.Activity.AddThemeColorOverride("font_color", stuck ? Hud.ErrorColor : isSelected ? Hud.ActiveTextColor : Hud.DimTextColor);
        }
        FitScroll();
    }

    /// <summary>Brings the buttons in line with the colonists: appends new ones, rebuilds when others changed or the compact mode flips.</summary>
    void Sync(List<Colonist> colonists)
    {
        bool compact = colonists.Count > CompactAbove;
        bool prefix = compact == _compact && _entries.Count <= colonists.Count;
        for (int i = 0; prefix && i < _entries.Count; i++) prefix = _entries[i].Colonist == colonists[i];
        if (prefix && _entries.Count == colonists.Count) return;
        if (!prefix)
        {
            foreach (var e in _entries) { _list.RemoveChild(e.Button); e.Button.QueueFree(); }
            _entries.Clear();
            _compact = compact;
        }
        for (int i = _entries.Count; i < colonists.Count; i++) _entries.Add(Add(colonists[i]));
    }

    /// <summary>The strip is as wide as its buttons, up to the room between the side panels; past that it scrolls.</summary>
    void FitScroll()
    {
        float room = GetViewportRect().Size.X - 2 * SideReserve - _work.Size.X - Separation;
        float width = Math.Min(_list.GetCombinedMinimumSize().X, Math.Max(room, FullSize.X));
        if (width == _scrollWidth) return;
        _scrollWidth = width;
        _scroll.CustomMinimumSize = new Vector2(width, 0);
    }

    Entry Add(Colonist c)
    {
        var button = new Button
        {
            Name = $"Colonist{c.Id}", ToggleMode = true, FocusMode = FocusModeEnum.None,
            CustomMinimumSize = _compact ? CompactSize : FullSize,
        };
        Hud.StyleButton(button, Hud.ActiveColor, Hud.ActiveTextColor);
        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Alignment = AlignmentMode.Center };
        column.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        column.AddThemeConstantOverride("separation", _compact ? 0 : 4);
        var name = Hud.Text("Name", _compact ? 11 : 13, Hud.TextColor);
        name.Text = c.Name;
        name.HorizontalAlignment = HorizontalAlignment.Center;
        name.ClipText = true;
        var activity = Hud.Text("Activity", _compact ? 9 : 11, Hud.DimTextColor);
        activity.HorizontalAlignment = HorizontalAlignment.Center;
        activity.ClipText = true;
        column.AddChild(name);
        column.AddChild(activity);
        button.AddChild(column);
        var entry = new Entry(c, button, name, activity);
        button.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press) return;
            _scene.Selected = c;
            if (press.DoubleClick) _scene.Following = true;
            else
            {
                var feet = _scene.ColonistRenderer.RenderPosition(c);
                _scene.Rig.SetFocus(feet.X, feet.Z);
            }
        };
        // A click toggles the button itself; it shows the selection, nothing else.
        button.Toggled += _ => button.SetPressedNoSignal(_scene.Selected == c);
        _list.AddChild(button);
        return entry;
    }
}
