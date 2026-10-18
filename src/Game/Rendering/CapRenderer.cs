using ColonySim.Core;
using Godot;
using GArray = Godot.Collections.Array;

namespace ColonySim.Game;

/// <summary>
/// Draws the slice caps: one mesh per chunk column at the current cut. Columns are meshed (and their Godot
/// arrays built) in parallel; the meshes are uploaded on the main thread in the same call, so a level change
/// never shows stale caps. Each column keeps its <see cref="ArrayMesh"/> and only swaps its surface.
/// </summary>
public partial class CapRenderer : Node3D
{
    const float DarkAmount = 0.68f;

    sealed class Column
    {
        public MeshInstance3D? Opaque, Liquid;
    }

    readonly record struct Built(GArray? Opaque, GArray? Liquid);

    static ShaderMaterial? _darkMaterial, _rawMaterial;

    ColonyMap _map = null!;
    Column[] _columns = [];
    bool _dark = true;

    /// <summary>Dark caps make cut rock read as solid mass; raw caps show the cut blocks' own top texture.</summary>
    public bool Dark
    {
        get => _dark;
        set
        {
            _dark = value;
            foreach (var c in _columns)
                if (c.Opaque != null) c.Opaque.MaterialOverride = OpaqueMaterial;
        }
    }

    ShaderMaterial OpaqueMaterial => _dark
        ? _darkMaterial ??= CreateMaterial(DarkAmount)
        : _rawMaterial ??= CreateMaterial(0);

    public void Setup(ColonyMap map)
    {
        _map = map;
        _columns = new Column[map.World.ChunksX * map.World.ChunksZ];
        for (int i = 0; i < _columns.Length; i++) _columns[i] = new Column();
    }

    /// <summary>Rebuilds every chunk column's caps for <paramref name="cutY"/>.</summary>
    public void Rebuild(int cutY) => Build(Enumerable.Range(0, _columns.Length).ToArray(), cutY);

    public void RebuildColumns(IEnumerable<(int cx, int cz)> columns, int cutY) =>
        Build(columns.Select(c => c.cz * _map.World.ChunksX + c.cx).Distinct().ToArray(), cutY);

    public void Clear()
    {
        foreach (var c in _columns)
        {
            SetSurface(c.Opaque, null);
            SetSurface(c.Liquid, null);
        }
    }

    void Build(int[] indices, int cutY)
    {
        int chunksX = _map.World.ChunksX;
        var built = new Built[indices.Length];
        var view = _map.View;
        Parallel.For(0, indices.Length, i =>
        {
            int index = indices[i];
            var pair = CapMesher.Build(view, index % chunksX, index / chunksX, cutY);
            built[i] = new Built(MeshConvert.ToArrays(pair.Opaque), MeshConvert.ToArrays(pair.Liquid));
        });

        for (int i = 0; i < indices.Length; i++)
        {
            var c = _columns[indices[i]];
            var (opaque, liquid) = built[i];
            if (opaque != null) c.Opaque ??= CreateInstance("CapOpaque", indices[i], OpaqueMaterial, castShadow: true);
            if (liquid != null) c.Liquid ??= CreateInstance("CapLiquid", indices[i], ChunkRenderer.LiquidMaterial, castShadow: false);
            SetSurface(c.Opaque, opaque);
            SetSurface(c.Liquid, liquid);
        }
    }

    /// <summary>Replaces the instance's single surface (or empties and hides it) and disposes the arrays.</summary>
    static void SetSurface(MeshInstance3D? instance, GArray? arrays)
    {
        if (instance == null || (arrays == null && !instance.Visible)) return; // already empty
        var mesh = (ArrayMesh)instance.Mesh;
        mesh.ClearSurfaces();
        instance.Visible = arrays != null;
        if (arrays == null) return;
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        arrays.Dispose();
    }

    MeshInstance3D CreateInstance(string kind, int index, Material material, bool castShadow)
    {
        // Vertices are in world coordinates, so the instance stays at the origin.
        var instance = new MeshInstance3D
        {
            Name = $"{kind}_{index % _map.World.ChunksX}_{index / _map.World.ChunksX}",
            Mesh = new ArrayMesh(),
            MaterialOverride = material,
            CastShadow = castShadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(instance);
        return instance;
    }

    static ShaderMaterial CreateMaterial(float darken)
    {
        var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/block_cap.gdshader") };
        m.SetShaderParameter("blocks", ChunkRenderer.BlockMaterial.GetShaderParameter("blocks"));
        m.SetShaderParameter("darken", darken);
        return m;
    }
}
