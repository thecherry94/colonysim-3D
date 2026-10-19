using ColonySim.Core;
using Godot;

namespace ColonySim.Game;

/// <summary>
/// Draws queued jobs as translucent cubes, one color per <see cref="JobKind"/>, plus the drag preview in the active
/// tool's color. Each layer is a <see cref="MultiMeshInstance3D"/>; the job layers are rebuilt when the queue's
/// version changes. Like the blocks, cubes above the view level are discarded by the shader.
/// </summary>
public partial class DesignationOverlay : Node3D
{
    const float CubeSize = 1.04f;
    const float Alpha = 0.45f;
    const int FloatsPerInstance = 12;
    const int MinCapacity = 64;

    static readonly Color DigColor = Color.FromHtml("#ffd23f");
    static readonly Color WallColor = Color.FromHtml("#4aa3ff");
    static readonly Color FloorColor = Color.FromHtml("#3fe0c5");
    static readonly Color StairsColor = Color.FromHtml("#b58cff");
    static readonly Color RampColor = Color.FromHtml("#ff9f43");
    static readonly Color LadderColor = Color.FromHtml("#c8d6e5");
    static readonly Color CarveColor = Color.FromHtml("#f368e0");
    static readonly Color CancelColor = Color.FromHtml("#ff5a5a");

    /// <summary>One MultiMesh of cubes; its buffer only grows or shrinks by powers of two.</summary>
    sealed class Layer
    {
        public required MultiMesh Mesh;
        public required ShaderMaterial Material;
        public float[] Buffer = [];
        public int Count;

        public void Begin(int count)
        {
            int capacity = Math.Max(MinCapacity, (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)Math.Max(1, count)));
            if (capacity != Mesh.InstanceCount && (capacity > Mesh.InstanceCount || capacity * 4 <= Mesh.InstanceCount))
            {
                Mesh.VisibleInstanceCount = 0;
                Mesh.InstanceCount = capacity;
                Buffer = new float[capacity * FloatsPerInstance];
            }
            Count = 0;
        }

        /// <summary>Identity basis, origin at the cell center (MultiMesh buffers store the 3x4 matrix row by row).</summary>
        public void Add(Int3 p)
        {
            int o = Count++ * FloatsPerInstance;
            Buffer[o + 0] = 1; Buffer[o + 1] = 0; Buffer[o + 2] = 0; Buffer[o + 3] = p.X + 0.5f;
            Buffer[o + 4] = 0; Buffer[o + 5] = 1; Buffer[o + 6] = 0; Buffer[o + 7] = p.Y + 0.5f;
            Buffer[o + 8] = 0; Buffer[o + 9] = 0; Buffer[o + 10] = 1; Buffer[o + 11] = p.Z + 0.5f;
        }

        public void End()
        {
            if (Count > 0) Mesh.Buffer = Buffer;
            Mesh.VisibleInstanceCount = Count;
        }
    }

    ColonyMap _map = null!;
    readonly Layer[] _kinds = new Layer[Enum.GetValues<JobKind>().Length];
    Layer _preview = null!;
    int _version = -1;

    public void Setup(ColonyMap map)
    {
        _map = map;
        var bounds = new Aabb(Vector3.Zero, new Vector3(map.World.SizeX, map.World.SizeY, map.World.SizeZ));
        var cube = new BoxMesh { Size = Vector3.One * CubeSize };
        foreach (var kind in Enum.GetValues<JobKind>())
            _kinds[(int)kind] = CreateLayer($"Jobs_{kind}", cube, bounds, KindColor(kind));
        _preview = CreateLayer("Preview", cube, bounds, DigColor);
    }

    public override void _Process(double delta)
    {
        if (_map == null || _map.Jobs.Version == _version) return;
        _version = _map.Jobs.Version;

        var counts = new int[_kinds.Length];
        foreach (var job in _map.Jobs.All) counts[(int)job.Kind]++;
        for (int k = 0; k < _kinds.Length; k++) _kinds[k].Begin(counts[k]);
        foreach (var job in _map.Jobs.All) _kinds[(int)job.Kind].Add(job.Position);
        foreach (var layer in _kinds) layer.End();
    }

    public void ShowPreview(IReadOnlyList<JobSpec> jobs, DesignationTool tool)
    {
        _preview.Material.SetShaderParameter("color", WithAlpha(ToolColor(tool)));
        _preview.Begin(jobs.Count);
        foreach (var job in jobs) _preview.Add(job.Position);
        _preview.End();
    }

    public void ClearPreview()
    {
        _preview.Begin(0);
        _preview.End();
    }

    Layer CreateLayer(string name, Mesh cube, Aabb bounds, Color color)
    {
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/overlay.gdshader") };
        material.SetShaderParameter("color", WithAlpha(color));
        var mesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = cube,
            CustomAabb = bounds,
        };
        AddChild(new MultiMeshInstance3D
        {
            Name = name,
            Multimesh = mesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        return new Layer { Mesh = mesh, Material = material };
    }

    static Color WithAlpha(Color c) => new(c, Alpha);

    static Color KindColor(JobKind kind) => kind switch
    {
        JobKind.Dig => DigColor,
        JobKind.BuildWall => WallColor,
        JobKind.BuildFloor => FloorColor,
        JobKind.BuildRamp => RampColor,
        JobKind.BuildLadder => LadderColor,
        JobKind.CarveStairs => CarveColor,
        _ => StairsColor,
    };

    static Color ToolColor(DesignationTool tool) => tool switch
    {
        DesignationTool.Dig => DigColor,
        DesignationTool.Wall => WallColor,
        DesignationTool.Floor => FloorColor,
        DesignationTool.Stairs => StairsColor,
        DesignationTool.Ramp => RampColor,
        DesignationTool.Ladder => LadderColor,
        DesignationTool.DigStairs => CarveColor,
        _ => CancelColor,
    };
}
