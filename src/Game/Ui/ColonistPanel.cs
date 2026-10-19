using ColonySim.Core;
using Godot;

namespace ColonySim.Game;

/// <summary>
/// The selected colonist's panel (bottom left, shown while there is a selection): name, what it is doing, the five skills
/// with level and experience bar, and the Follow toggle.
/// </summary>
public partial class ColonistPanel : PanelContainer
{
    MapScene _scene = null!;
    Label _name = null!, _activity = null!;
    Button _follow = null!;
    readonly Label[] _levels = new Label[Enum.GetValues<SkillType>().Length];
    readonly ProgressBar[] _bars = new ProgressBar[Enum.GetValues<SkillType>().Length];

    public void Setup(MapScene scene)
    {
        _scene = scene;
        Name = "ColonistPanel";
        AddThemeStyleboxOverride("panel", Hud.Box(new Color(20 / 255f, 24 / 255f, 32 / 255f, 0.93f), Color.FromHtml("#333a48"), 10, 14, 8));
        SetAnchorsAndOffsetsPreset(LayoutPreset.BottomLeft, LayoutPresetMode.Minsize);
        GrowVertical = GrowDirection.Begin;
        // Above the toolbar's rows and the status line.
        OffsetLeft = 12;
        OffsetBottom = -176;
        CustomMinimumSize = new Vector2(250, 0);
        Visible = false;

        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 4);
        _name = Hud.Text("Name", 17, Hud.TextColor);
        _activity = Hud.Text("Activity", 13, Hud.DimTextColor);
        _activity.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _activity.CustomMinimumSize = new Vector2(220, 0);
        box.AddChild(_name);
        box.AddChild(_activity);

        var skills = new GridContainer { Columns = 3, MouseFilter = MouseFilterEnum.Ignore };
        skills.AddThemeConstantOverride("h_separation", 8);
        foreach (var type in Enum.GetValues<SkillType>())
        {
            int i = (int)type;
            var skillName = Hud.Text($"Skill{type}", 13, Hud.TextColor);
            skillName.Text = type.ToString();
            skills.AddChild(skillName);
            _levels[i] = Hud.Text($"Level{type}", 13, Hud.TextColor);
            _levels[i].CustomMinimumSize = new Vector2(24, 0);
            _levels[i].HorizontalAlignment = HorizontalAlignment.Right;
            skills.AddChild(_levels[i]);
            _bars[i] = new ProgressBar
            {
                Name = $"Xp{type}", ShowPercentage = false, MinValue = 0, MaxValue = 1,
                CustomMinimumSize = new Vector2(100, 10), SizeFlagsVertical = SizeFlags.ShrinkCenter,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            _bars[i].AddThemeStyleboxOverride("background", Hud.Box(Color.FromHtml("#2b3242"), Color.FromHtml("#44506a"), 3, 0, 0));
            _bars[i].AddThemeStyleboxOverride("fill", Hud.Box(Color.FromHtml("#6fa8dc"), Color.FromHtml("#6fa8dc"), 3, 0, 0));
            skills.AddChild(_bars[i]);
        }
        box.AddChild(skills);

        _follow = Hud.ToggleButton("Follow [F]", null);
        _follow.Name = "Follow";
        _follow.Toggled += on => _scene.Following = on;
        box.AddChild(_follow);
        AddChild(box);
    }

    /// <summary>"Digging stone at (x, y, z)", "Building wood wall at …", "Stuck — can't reach the colony", or the plain state.</summary>
    public static string ActivityText(ColonyMap map, Colonist c)
    {
        if (c.Stuck) return "Stuck — can't reach the colony";
        switch (c.Activity)
        {
            case ColonistActivity.Walking: return "Walking";
            case ColonistActivity.Falling: return "Falling";
            case ColonistActivity.Working when map.Jobs.TryGetById(c.JobId, out var job):
                var p = job.Position;
                string where = $"at ({p.X}, {p.Y}, {p.Z})";
                if (job.Kind is JobKind.Dig or JobKind.CarveStairs)
                    return $"Digging {BlockName(map.World.GetBlock(p.X, p.Y, p.Z))} {where}";
                return $"Building {BlockName(job.Block)} {where}";
            case ColonistActivity.Working: return "Working";
            default: return "Idle";
        }
    }

    static string BlockName(ushort block) => Blocks.Registry.Get(block).Name.Replace('_', ' ').ToLowerInvariant();

    public override void _Process(double delta)
    {
        if (_scene == null) return;
        var c = _scene.Selected;
        Visible = c != null;
        if (c == null) return;
        SetText(_name, c.Name);
        SetText(_activity, ActivityText(_scene.Map, c));
        foreach (var type in Enum.GetValues<SkillType>())
        {
            var skill = c.Skills[(int)type];
            SetText(_levels[(int)type], skill.Level.ToString());
            _bars[(int)type].Value = skill.Level >= Skills.MaxLevel ? 1 : skill.Xp / Skills.XpForNextLevel(skill.Level);
        }
        _follow.SetPressedNoSignal(_scene.Following);
    }

    static void SetText(Label label, string text)
    {
        if (label.Text != text) label.Text = text;
    }
}
