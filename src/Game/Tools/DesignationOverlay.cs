using ColonySim.Core;
using Godot;

namespace ColonySim.Game;

/// <summary>
/// Draws queued jobs as translucent cubes: one <see cref="MultiMeshInstance3D"/> per chunk that has jobs, each cube
/// tinted by its job (kind color, dimmed when unreachable, red when it needs a way back), plus the drag preview in
/// the active tool's color. Only the chunks the queue reports as changed are rebuilt. Like the blocks, cubes above
/// the view level are discarded by the shader.
/// </summary>
public partial class DesignationOverlay : Node3D
{
    const float CubeSize = 1.04f;
    const float Alpha = 0.45f;
    // Transform3D (12 floats) followed by the instance color (4 floats).
    const int FloatsPerInstance = 16;
    const int MinCapacity = 64;
    const float UnreachableBrightness = 0.5f;

    static readonly Color DigColor = Color.FromHtml("#ffd23f");
    static readonly Color WallColor = Color.FromHtml("#4aa3ff");
    static readonly Color FloorColor = Color.FromHtml("#3fe0c5");
    static readonly Color StairsColor = Color.FromHtml("#b58cff");
    static readonly Color RampColor = Color.FromHtml("#ff9f43");
    static readonly Color LadderColor = Color.FromHtml("#c8d6e5");
    static readonly Color CarveColor = Color.FromHtml("#f368e0");
    static readonly Color CancelColor = Color.FromHtml("#ff5a5a");
    static readonly Color NeedsWayBackColor = Color.FromHtml("#ff5a5a");

    /// <summary>One MultiMesh of cubes; its buffer only grows or shrinks by powers of two.</summary>
    sealed class Layer
    {
        public required MultiMesh Mesh;
        public required MultiMeshInstance3D Node;
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
        public void Add(Int3 p, Color color)
        {
            int o = Count++ * FloatsPerInstance;
            Buffer[o + 0] = 1; Buffer[o + 1] = 0; Buffer[o + 2] = 0; Buffer[o + 3] = p.X + 0.5f;
            Buffer[o + 4] = 0; Buffer[o + 5] = 1; Buffer[o + 6] = 0; Buffer[o + 7] = p.Y + 0.5f;
            Buffer[o + 8] = 0; Buffer[o + 9] = 0; Buffer[o + 10] = 1; Buffer[o + 11] = p.Z + 0.5f;
            Buffer[o + 12] = color.R; Buffer[o + 13] = color.G; Buffer[o + 14] = color.B; Buffer[o + 15] = 1;
        }

        public void End()
        {
            if (Count > 0) Mesh.Buffer = Buffer;
            Mesh.VisibleInstanceCount = Count;
        }
    }

    ColonyMap _map = null!;
    readonly Dictionary<Int3, Layer> _chunks = new();
    readonly HashSet<Int3> _changed = new();
    BoxMesh _cube = null!;
    ShaderMaterial _jobMaterial = null!;
    Layer _preview = null!;

    /// <summary>Chunk layers currently alive (chunks with jobs).</summary>
    public int LayerCount => _chunks.Count;

    public void Setup(ColonyMap map)
    {
        _map = map;
        _cube = new BoxMesh { Size = Vector3.One * CubeSize };
        _jobMaterial = CreateMaterial(Colors.White);
        // Jobs already queued (a loaded save) draw too; later changes come through ConsumeChangedChunks.
        foreach (var job in map.Jobs.All) _changed.Add(JobQueue.ChunkOf(job.Position));
        _preview = CreateLayer("Preview", new Aabb(Vector3.Zero, new Vector3(map.World.SizeX, map.World.SizeY, map.World.SizeZ)),
            CreateMaterial(DigColor));
    }

    public override void _Process(double delta) => Refresh();

    /// <summary>Rebuilds the chunks whose jobs changed since the last call.</summary>
    public void Refresh()
    {
        if (_map == null) return;
        _map.Jobs.ConsumeChangedChunks(_changed);
        foreach (var chunk in _changed) RebuildChunk(chunk);
        _changed.Clear();
    }

    void RebuildChunk(Int3 chunk)
    {
        int count = 0;
        foreach (var _ in _map.Jobs.InChunk(chunk)) count++;
        if (count == 0)
        {
            if (_chunks.Remove(chunk, out var gone)) gone.Node.QueueFree();
            return;
        }
        if (!_chunks.TryGetValue(chunk, out var layer))
        {
            const int s = JobChunkSize;
            _chunks[chunk] = layer = CreateLayer($"Jobs_{chunk.X}_{chunk.Y}_{chunk.Z}",
                new Aabb(new Vector3(chunk.X * s - 1, chunk.Y * s - 1, chunk.Z * s - 1), Vector3.One * (s + 2)), _jobMaterial);
        }
        layer.Begin(count);
        foreach (var job in _map.Jobs.InChunk(chunk)) layer.Add(job.Position, JobColor(job));
        layer.End();
    }

    public void ShowPreview(IReadOnlyList<JobSpec> jobs, DesignationTool tool)
    {
        ((ShaderMaterial)_preview.Node.MaterialOverride).SetShaderParameter("color", WithAlpha(ToolColor(tool)));
        _preview.Begin(jobs.Count);
        foreach (var job in jobs) _preview.Add(job.Position, Colors.White);
        _preview.End();
    }

    public void ClearPreview()
    {
        _preview.Begin(0);
        _preview.End();
    }

    const int JobChunkSize = 32;

    ShaderMaterial CreateMaterial(Color color)
    {
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/overlay.gdshader") };
        material.SetShaderParameter("color", WithAlpha(color));
        return material;
    }

    Layer CreateLayer(string name, Aabb bounds, ShaderMaterial material)
    {
        var mesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = _cube,
            CustomAabb = bounds,
        };
        var node = new MultiMeshInstance3D
        {
            Name = name,
            Multimesh = mesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(node);
        return new Layer { Mesh = mesh, Node = node };
    }

    static Color WithAlpha(Color c) => new(c, Alpha);

    /// <summary>The instance color (linear, as the shader multiplies it in): the kind's color, dimmed or red by status.</summary>
    static Color JobColor(Job job)
    {
        var c = job.Status switch
        {
            JobStatus.NeedsWayBack => NeedsWayBackColor,
            JobStatus.Unreachable => Dim(KindColor(job.Kind)),
            _ => KindColor(job.Kind),
        };
        return c.SrgbToLinear();
    }

    static Color Dim(Color c) => new(c.R * UnreachableBrightness, c.G * UnreachableBrightness, c.B * UnreachableBrightness);

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
