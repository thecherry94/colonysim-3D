using Godot;

namespace ColonySim.Game;

/// <summary>In-game HUD. For now only the view-level indicator in the top-left corner.</summary>
public partial class Hud : CanvasLayer
{
    MapScene _scene = null!;
    Label _level = null!;

    public void Setup(MapScene scene) => _scene = scene;

    public override void _Ready()
    {
        var panel = new PanelContainer
        {
            Name = "LevelPanel",
            Position = new Vector2(12, 12),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(20 / 255f, 24 / 255f, 32 / 255f, 0.93f),
            BorderColor = Color.FromHtml("#333a48"),
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10, CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,
            ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 8, ContentMarginBottom = 8,
        });
        _level = new Label { Name = "Level", MouseFilter = Control.MouseFilterEnum.Ignore };
        _level.AddThemeFontSizeOverride("font_size", 15);
        _level.AddThemeColorOverride("font_color", Color.FromHtml("#e6e6e6"));
        panel.AddChild(_level);
        AddChild(panel);
    }

    public override void _Process(double delta)
    {
        if (_scene == null) return;
        // Depth is relative to the ground under the camera focus, which changes as the camera pans.
        string text = LevelText(_scene.Slice, SurfaceAtFocus());
        if (_level.Text != text) _level.Text = text;
    }

    static string LevelText(SliceController slice, int surface)
    {
        if (!slice.IsSliced) return "View level: top (nothing hidden)";
        int rel = slice.ViewLevel - surface;
        string depth = rel < 0 ? $"{-rel} below ground" : rel == 0 ? "ground level" : $"{rel} above ground";
        return $"View level: Y {slice.ViewLevel} · {depth}";
    }

    int SurfaceAtFocus()
    {
        var world = _scene.Map.World;
        var focus = _scene.Rig.FocusPoint;
        return world.GetSurfaceHeight(
            Math.Clamp((int)focus.X, 0, world.SizeX - 1),
            Math.Clamp((int)focus.Z, 0, world.SizeZ - 1));
    }
}
