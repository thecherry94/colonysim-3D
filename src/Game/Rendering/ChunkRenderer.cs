using System.Collections.Concurrent;
using System.Diagnostics;
using ColonySim.Core;
using Godot;
using GArray = Godot.Collections.Array;

namespace ColonySim.Game;

/// <summary>
/// Renders the block world chunk by chunk. Chunks are meshed on the thread pool (bounded concurrency) and the
/// results are applied on the main thread within a per-frame time budget. A result is applied only if no newer
/// remesh was requested in the meantime. Meshing reads the world without locks: a write that races with a
/// mesh re-dirties the chunk, so the stale result is discarded and replaced.
/// </summary>
public partial class ChunkRenderer : Node3D
{
    const double ApplyBudgetMs = 6.0;
    const int S = VoxelWorld.ChunkSize;

    static ShaderMaterial? _blockMaterial, _liquidMaterial;
    static Texture2DArray? _blockTextures;

    /// <summary>Opaque block material; shared with the cap renderer.</summary>
    public static ShaderMaterial BlockMaterial => _blockMaterial ??= CreateMaterial("res://assets/shaders/block.gdshader");

    /// <summary>Semi-transparent liquid material; shared with the cap renderer.</summary>
    public static ShaderMaterial LiquidMaterial => _liquidMaterial ??= CreateMaterial("res://assets/shaders/liquid.gdshader");

    sealed class Slot(Int3 coord)
    {
        public readonly Int3 Coord = coord;
        public bool Tracked;          // ever requested; counts toward TotalChunks
        public bool Waiting;          // in the dispatch queue
        public int RequestedVersion;
        public int AppliedVersion;
        public MeshInstance3D? Opaque, Liquid;
        public ArrayMesh? OpaqueMesh, LiquidMesh;
    }

    readonly record struct Result(int Slot, int Version, GArray? Opaque, GArray? Liquid, Exception? Error);

    readonly Queue<int> _waiting = new();
    readonly ConcurrentQueue<Result> _done = new();
    readonly HashSet<Int3> _dirty = new();
    readonly int _maxConcurrent = Math.Max(1, System.Environment.ProcessorCount - 2);

    ColonyMap _map = null!;
    Slot[] _slots = [];
    int _running;
    int _sliceLevel = int.MaxValue;

    public int TotalChunks { get; private set; }
    public int MeshedChunks { get; private set; }
    /// <summary>Chunks whose latest requested mesh has not been applied yet.</summary>
    public int PendingChunks { get; private set; }

    /// <summary>
    /// Raised each frame with that frame's dirty chunks, before they are scheduled for meshing.
    /// The collection is reused: it is only valid during the call.
    /// </summary>
    public event Action<IReadOnlyCollection<Int3>>? ChunksDirtied;

    /// <summary>Queues every chunk that isn't untouched uniform air.</summary>
    public void Initialize(ColonyMap map)
    {
        _map = map;
        var w = map.World;
        _slots = new Slot[w.ChunksX * w.ChunksY * w.ChunksZ];
        for (int cy = 0; cy < w.ChunksY; cy++)
            for (int cz = 0; cz < w.ChunksZ; cz++)
                for (int cx = 0; cx < w.ChunksX; cx++)
                    _slots[Index(cx, cy, cz)] = new Slot(new Int3(cx, cy, cz));

        // Everything is meshed below anyway, so whatever generation left dirty is already covered.
        w.ConsumeDirtyChunks(_dirty);
        _dirty.Clear();

        // Top-down, so the visible surface appears first.
        for (int cy = w.ChunksY - 1; cy >= 0; cy--)
            for (int cz = 0; cz < w.ChunksZ; cz++)
                for (int cx = 0; cx < w.ChunksX; cx++)
                    if (!w.IsChunkUniform(new Int3(cx, cy, cz), out _)) Request(Index(cx, cy, cz));
    }

    /// <summary>Hides chunks lying entirely above <paramref name="level"/>; the shader cuts the rest.</summary>
    public void SetSliceLevel(int level)
    {
        _sliceLevel = level;
        foreach (var s in _slots) UpdateVisibility(s);
    }

    public override void _Process(double delta)
    {
        if (_slots.Length == 0) return;

        _map.World.ConsumeDirtyChunks(_dirty);
        if (_dirty.Count > 0)
        {
            ChunksDirtied?.Invoke(_dirty);
            foreach (var c in _dirty) Request(Index(c.X, c.Y, c.Z));
            _dirty.Clear();
        }

        Dispatch();
        ApplyResults();
    }

    int Index(int cx, int cy, int cz) => (cy * _map.World.ChunksZ + cz) * _map.World.ChunksX + cx;

    void Request(int index)
    {
        var s = _slots[index];
        if (!s.Tracked) { s.Tracked = true; TotalChunks++; }
        if (s.AppliedVersion == s.RequestedVersion) PendingChunks++;
        s.RequestedVersion++;
        if (!s.Waiting) { s.Waiting = true; _waiting.Enqueue(index); }
    }

    void Dispatch()
    {
        while (_waiting.Count > 0 && Volatile.Read(ref _running) < _maxConcurrent)
        {
            int index = _waiting.Dequeue();
            var s = _slots[index];
            s.Waiting = false;
            Interlocked.Increment(ref _running);
            ThreadPool.QueueUserWorkItem(static job => job.Self.MeshChunk(job.Index, job.Coord, job.Version),
                (Self: this, Index: index, s.Coord, Version: s.RequestedVersion), preferLocal: false);
        }
    }

    /// <summary>Worker thread: mesh the chunk and build the Godot surface arrays.</summary>
    void MeshChunk(int index, Int3 coord, int version)
    {
        try
        {
            var pair = ChunkMesher.Build(_map.View, coord);
            _done.Enqueue(new Result(index, version, MeshConvert.ToArrays(pair.Opaque), MeshConvert.ToArrays(pair.Liquid), null));
        }
        catch (Exception e)
        {
            _done.Enqueue(new Result(index, version, null, null, e));
        }
        finally
        {
            Interlocked.Decrement(ref _running);
        }
    }

    void ApplyResults()
    {
        long start = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(start).TotalMilliseconds < ApplyBudgetMs && _done.TryDequeue(out var r))
        {
            var s = _slots[r.Slot];
            if (r.Version != s.RequestedVersion)
            {
                r.Opaque?.Dispose(); r.Liquid?.Dispose();
                continue;
            }
            if (r.Error != null) GD.PushError($"ChunkRenderer: meshing chunk {s.Coord} failed: {r.Error}");

            Apply(ref s.Opaque, ref s.OpaqueMesh, r.Opaque, BlockMaterial, "Opaque", s.Coord, castShadow: true);
            Apply(ref s.Liquid, ref s.LiquidMesh, r.Liquid, LiquidMaterial, "Liquid", s.Coord, castShadow: false);
            UpdateVisibility(s);

            if (s.AppliedVersion == 0) MeshedChunks++;
            s.AppliedVersion = s.RequestedVersion;
            PendingChunks--;
        }
    }

    void Apply(ref MeshInstance3D? instance, ref ArrayMesh? current, GArray? arrays, Material material,
        string kind, Int3 coord, bool castShadow)
    {
        ArrayMesh? mesh = null;
        if (arrays != null)
        {
            mesh = MeshConvert.FromArrays(arrays);
            arrays.Dispose();
            if (instance == null)
            {
                // Vertices are in world coordinates, so the instance stays at the origin.
                instance = new MeshInstance3D
                {
                    Name = $"{kind}_{coord.X}_{coord.Y}_{coord.Z}",
                    MaterialOverride = material,
                    CastShadow = castShadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
                };
                AddChild(instance);
            }
        }
        if (instance != null) instance.Mesh = mesh;
        current?.Dispose();
        current = mesh;
    }

    void UpdateVisibility(Slot s)
    {
        bool visible = s.Coord.Y * S <= _sliceLevel;
        if (s.Opaque != null) s.Opaque.Visible = visible;
        if (s.Liquid != null) s.Liquid.Visible = visible;
    }

    static ShaderMaterial CreateMaterial(string shaderPath)
    {
        _blockTextures ??= BlockTextures.Build(Blocks.Registry);
        var m = new ShaderMaterial { Shader = GD.Load<Shader>(shaderPath) };
        m.SetShaderParameter("blocks", _blockTextures);
        return m;
    }
}
