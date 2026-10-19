using ColonySim.Core;
using Godot;

namespace ColonySim.Game;

/// <summary>
/// Named view-level bookmarks (under the level indicator): "＋ Bookmark" stores the current view level under the typed
/// name ("Level {Y}" when blank); each bookmark is a button that jumps to its level, with an ✕ to delete it. The name field
/// gives up keyboard focus on Enter, Esc or a click elsewhere, so the hotkeys keep working.
/// </summary>
public partial class BookmarksPanel : VBoxContainer
{
    const int MaxNameLength = 24;

    MapScene _scene = null!;
    LineEdit _name = null!;
    VBoxContainer _list = null!;
    readonly List<ViewBookmark> _shown = new();

    internal LineEdit NameEdit => _name;
    internal Button AddButton { get; private set; } = null!;
    internal VBoxContainer List => _list;

    public void Setup(MapScene scene)
    {
        _scene = scene;
        Name = "Bookmarks";
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", 4);

        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 6);
        _name = new LineEdit { Name = "BookmarkName", MaxLength = MaxNameLength, CustomMinimumSize = new Vector2(96, 0), SelectAllOnFocus = true, PlaceholderText = "Bookmark name" };
        _name.AddThemeFontSizeOverride("font_size", 13);
        _name.TextSubmitted += _ => _name.ReleaseFocus();
        _name.GuiInput += e =>
        {
            if (!e.IsActionPressed("ui_cancel")) return;
            _name.ReleaseFocus();
            _name.AcceptEvent();
        };
        row.AddChild(_name);
        AddButton = Hud.ActionButton("＋ Bookmark", Add);
        AddButton.Name = "AddBookmark";
        row.AddChild(AddButton);
        AddChild(row);

        _list = new VBoxContainer { Name = "List", MouseFilter = MouseFilterEnum.Ignore };
        _list.AddThemeConstantOverride("separation", 3);
        AddChild(_list);
    }

    /// <summary>Stores the current view level under the typed name (or "Level {Y}").</summary>
    public void Add()
    {
        string text = _name.Text.Trim();
        int level = _scene.Slice.ViewLevel;
        _scene.Map.Colony.Bookmarks.Add(new ViewBookmark(text.Length > 0 ? text : $"Level {level}", level));
        _name.Clear();
        _name.ReleaseFocus();
    }

    public override void _Input(InputEvent @event)
    {
        // A click anywhere else ends typing; the world's hotkeys then work again.
        if (_name.HasFocus() && @event is InputEventMouseButton { Pressed: true } click && !_name.GetGlobalRect().HasPoint(click.GlobalPosition))
            _name.ReleaseFocus();
    }

    public override void _Process(double delta)
    {
        if (_scene == null) return;
        var bookmarks = _scene.Map.Colony.Bookmarks;
        if (!_shown.SequenceEqual(bookmarks)) Rebuild(bookmarks);
    }

    void Rebuild(List<ViewBookmark> bookmarks)
    {
        foreach (var child in _list.GetChildren()) { _list.RemoveChild(child); child.QueueFree(); }
        _shown.Clear();
        _shown.AddRange(bookmarks);
        foreach (var bookmark in bookmarks)
        {
            var b = bookmark;
            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            row.AddThemeConstantOverride("separation", 4);
            var jump = Hud.ActionButton($"{b.Name} (Y {b.Level})", () => _scene.Slice.SetLevel(b.Level));
            jump.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            jump.Alignment = HorizontalAlignment.Left;
            jump.ClipText = true;
            var delete = Hud.ActionButton("✕", () => _scene.Map.Colony.Bookmarks.Remove(b));
            row.AddChild(jump);
            row.AddChild(delete);
            _list.AddChild(row);
        }
    }
}
