using ColonySim.Core;
using Godot;

namespace ColonySim.Game;

/// <summary>
/// Draws every colonist as a blocky person (1 wide, 1.8 tall; placeholder art) from six instanced boxes: head, torso,
/// two arms and two legs, one <see cref="MultiMeshInstance3D"/> per part, so the cost doesn't grow with scene nodes.
/// Positions are interpolated between ticks by <see cref="SimRunner.Alpha"/>; a colonist faces where it walks and
/// swings its limbs while moving. Colonists standing above the view level are scaled to nothing.
/// </summary>
public partial class ColonistRenderer : Node3D
{
    const float SwingDegrees = 30, SwingHz = 2, HopHeight = 0.4f;
    const int InitialCapacity = 32;

    enum Part { Head, Torso, ArmLeft, ArmRight, LegLeft, LegRight }

    /// <summary>A box of <c>Size</c> centered <c>Center</c> from its joint at <c>Pivot</c>, swung about the joint by <c>Swing</c> times the swing angle.</summary>
    readonly record struct Shape(Vector3 Size, Vector3 Pivot, Vector3 Center, float Swing);

    // Local space: feet at the origin, facing -Z. Arms swing against the legs on the same side.
    static readonly Shape[] Shapes =
    [
        new(new(0.5f, 0.5f, 0.5f), new(0, 1.3f, 0), new(0, 0.25f, 0), 0),
        new(new(0.6f, 0.6f, 0.34f), new(0, 1.0f, 0), Vector3.Zero, 0),
        new(new(0.2f, 0.6f, 0.2f), new(-0.4f, 1.3f, 0), new(0, -0.3f, 0), 1),
        new(new(0.2f, 0.6f, 0.2f), new(0.4f, 1.3f, 0), new(0, -0.3f, 0), -1),
        new(new(0.3f, 0.7f, 0.3f), new(-0.15f, 0.7f, 0), new(0, -0.35f, 0), -1),
        new(new(0.3f, 0.7f, 0.3f), new(0.15f, 0.7f, 0), new(0, -0.35f, 0), 1),
    ];

    static readonly Color[] Shirts =
    [
        Color.FromHtml("#c0392b"), Color.FromHtml("#2e86c1"), Color.FromHtml("#27ae60"), Color.FromHtml("#f1c40f"),
        Color.FromHtml("#8e44ad"), Color.FromHtml("#e67e22"), Color.FromHtml("#ecf0f1"), Color.FromHtml("#16a085"),
    ];
    static readonly Color[] Skins = [Color.FromHtml("#f1c7a0"), Color.FromHtml("#d9a577"), Color.FromHtml("#a8754f"), Color.FromHtml("#6f4a33")];
    static readonly Color Pants = Color.FromHtml("#2c3e66");

    SimRunner _runner = null!;
    SliceController _slice = null!;
    MultiMesh[] _meshes = null!;
    int[] _style = [];
    readonly Dictionary<int, float> _yaw = new();

    /// <summary>Colonists drawn (not hidden by the view level) in the last frame.</summary>
    public int VisibleCount { get; private set; }

    public void Setup(ColonyMap map, SimRunner runner, SliceController slice)
    {
        _runner = runner;
        _slice = slice;
        var material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 1, MetallicSpecular = 0 };
        var bounds = new Aabb(Vector3.Zero, new Vector3(map.World.SizeX, map.World.SizeY, map.World.SizeZ));
        _meshes = new MultiMesh[Shapes.Length];
        foreach (var part in Enum.GetValues<Part>())
        {
            var mesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                Mesh = new BoxMesh { Size = Vector3.One, Material = material },
                CustomAabb = bounds,
            };
            _meshes[(int)part] = mesh;
            AddChild(new MultiMeshInstance3D { Name = part.ToString(), Multimesh = mesh });
        }
        Resize(InitialCapacity);
    }

    void Resize(int capacity)
    {
        foreach (var mesh in _meshes) mesh.InstanceCount = capacity; // clears the instances' colors
        _style = new int[capacity];
        Array.Fill(_style, -1);
    }

    /// <summary>Where the colonist's feet are now: the cell's bottom center, moved along its motion by the tick fraction (hops arc 0.4 above the line).</summary>
    public Vector3 RenderPosition(Colonist c)
    {
        var from = new Vector3(c.Cell.X + 0.5f, c.Cell.Y, c.Cell.Z + 0.5f);
        if (c.Motion is not { } m) return from;
        var to = new Vector3(m.To.X + 0.5f, m.To.Y, m.To.Z + 0.5f);
        float t = Math.Clamp((m.Elapsed + _runner.Alpha) / m.TotalTicks, 0f, 1f);
        var p = from.Lerp(to, t);
        if (m.Kind == MoveKind.Hop) p.Y += HopHeight * 4 * t * (1 - t);
        return p;
    }

    /// <summary>
    /// Whether the colonist is drawn: always when the view isn't sliced, else unless it stands on a block above the
    /// view level (its feet cell is the one above the block it stands on).
    /// </summary>
    public bool IsVisible(Colonist c) => !_slice.IsSliced || c.Cell.Y - 1 <= _slice.ViewLevel;

    public override void _Process(double delta)
    {
        if (_runner?.Sim == null) return;
        var colonists = _runner.Sim.Colony.Colonists;
        if (colonists.Count > _style.Length) Resize(Math.Max(colonists.Count, _style.Length * 2));
        foreach (var mesh in _meshes) mesh.VisibleInstanceCount = colonists.Count;

        double tick = _runner.Sim.Colony.Tick + _runner.Alpha;
        int visible = 0;
        for (int i = 0; i < colonists.Count; i++)
        {
            var c = colonists[i];
            var feet = RenderPosition(c);
            if (!IsVisible(c))
            {
                var gone = new Transform3D(Basis.FromScale(Vector3.Zero), feet);
                foreach (var mesh in _meshes) mesh.SetInstanceTransform(i, gone);
                continue;
            }
            visible++;
            if (c.Motion is { } m && (m.To.X != m.From.X || m.To.Z != m.From.Z))
                _yaw[c.Id] = Mathf.Atan2(-(m.To.X - m.From.X), -(m.To.Z - m.From.Z));
            var root = new Transform3D(new Basis(Vector3.Up, _yaw.GetValueOrDefault(c.Id)), feet);
            float swing = c.Motion == null ? 0
                : Mathf.DegToRad(SwingDegrees) * MathF.Sin((float)(tick / SimClock.TicksPerSecond * SwingHz * Mathf.Tau) + c.Id);
            for (int p = 0; p < Shapes.Length; p++)
            {
                var s = Shapes[p];
                var joint = new Transform3D(new Basis(Vector3.Right, swing * s.Swing), s.Pivot);
                var box = new Transform3D(Basis.FromScale(s.Size), s.Center);
                _meshes[p].SetInstanceTransform(i, root * joint * box);
            }
            Dress(i, c);
        }
        VisibleCount = visible;
    }

    /// <summary>Sets the instance colors of slot <paramref name="i"/> when its colonist's look changed (colors persist between frames).</summary>
    void Dress(int i, Colonist c)
    {
        int style = c.ShirtColor + 8 * (c.Id % Skins.Length);
        if (_style[i] == style) return;
        _style[i] = style;
        var shirt = Shirts[c.ShirtColor & 7];
        var skin = Skins[c.Id % Skins.Length];
        _meshes[(int)Part.Head].SetInstanceColor(i, skin);
        _meshes[(int)Part.Torso].SetInstanceColor(i, shirt);
        _meshes[(int)Part.ArmLeft].SetInstanceColor(i, shirt);
        _meshes[(int)Part.ArmRight].SetInstanceColor(i, shirt);
        _meshes[(int)Part.LegLeft].SetInstanceColor(i, Pants);
        _meshes[(int)Part.LegRight].SetInstanceColor(i, Pants);
    }
}
