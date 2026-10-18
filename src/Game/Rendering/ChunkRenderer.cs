using System.Collections.Concurrent;
using System.Diagnostics;
using ColonySim.Core;
using Godot;
using GArray = Godot.Collections.Array;

namespace ColonySim.Game;

/// <summary>
/// Renders the block world chunk by chunk. Chunks are meshed on the thread pool (bounded concurrency). A result is
/// applied only if no newer remesh was requested in the meantime. Meshing reads the world without locks: a write
/// that races with a mesh re-dirties the chunk, so the stale result is discarded and replaced.
/// <para>
/// Two lanes: the initial load is applied within a per-frame time budget, while edits (chunks the world reports
/// dirty) jump ahead of it and are applied all at once, so the up to 8 chunks one block change touches swap their
/// meshes in the same frame and never show a seam. The renderer runs after the other nodes each frame, so edits made
/// during a frame are dispatched in that same frame.
/// </para>
/// </summary>
public partial class ChunkRenderer : Node3D
{
    const double ApplyBudgetMs = 6.0;
    /// <summary>After this many frames waiting for the rest of its batch, ready edit results are applied anyway.</summary>
    const int MaxEditHoldFrames = 6;
    const int S = VoxelWorld.ChunkSize;

    static ShaderMaterial? _blockMaterial, _liquidMaterial;
    static Texture2DArray? _blockTextures;

    /// <summary>Opaque block material; shared with the cap renderer.</summary>
    public static ShaderMaterial BlockMaterial => _blockMaterial ??= CreateMaterial("res://assets/shaders/block.gdshader");

    /// <summary>Semi-transparent liquid material; shared with the cap renderer.</summary>
    public static ShaderMaterial LiquidMaterial => _liquidMaterial ??= CreateMaterial("res://assets/shaders/liquid.gdshader");

    enum Lane { None, Load, Edit }

    sealed class Slot(Int3 coord)
    {
        public readonly Int3 Coord = coord;
        public bool Tracked;          // ever requested; counts toward TotalChunks
        public Lane Queued;           // which dispatch queue holds it (a stale entry in the other queue is skipped)
        public int RequestedVersion;
        public int AppliedVersion;
        public MeshInstance3D? Opaque, Liquid;
        public ArrayMesh? OpaqueMesh, LiquidMesh;
    }

    readonly record struct Result(int Slot, int Version, GArray? Opaque, GArray? Liquid, Exception? Error);

    readonly Queue<int> _loadQueue = new(), _editQueue = new();
    readonly ConcurrentQueue<Result> _done = new();
    readonly Queue<Result> _loadReady = new();          // load results waiting for the frame budget
    readonly HashSet<int> _editPending = new();         // slots whose latest request is an unapplied edit
    readonly Dictionary<int, Result> _staged = new();   // current edit results held until the batch is complete
    readonly List<Int3> _appliedEdits = new();
    readonly HashSet<Int3> _dirty = new();
    int _stagedFrames;
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
    /// Raised when a batch of edited chunks has swapped in its new meshes, with those chunks.
    /// The collection is reused: it is only valid during the call.
    /// </summary>
    public event Action<IReadOnlyCollection<Int3>>? EditsApplied;

    public override void _Ready() => ProcessPriority = 100;

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
                    if (!w.IsChunkUniform(new Int3(cx, cy, cz), out _)) Request(Index(cx, cy, cz), edit: false);
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
        foreach (var c in _dirty) Request(Index(c.X, c.Y, c.Z), edit: true);
        _dirty.Clear();

        Dispatch();
        ApplyResults();
    }

    int Index(int cx, int cy, int cz) => (cy * _map.World.ChunksZ + cz) * _map.World.ChunksX + cx;

    void Request(int index, bool edit)
    {
        var s = _slots[index];
        if (!s.Tracked) { s.Tracked = true; TotalChunks++; }
        if (s.AppliedVersion == s.RequestedVersion) PendingChunks++;
        s.RequestedVersion++;
        if (edit)
        {
            _editPending.Add(index);
            if (_staged.Remove(index, out var stale)) Dispose(stale);
            if (s.Queued != Lane.Edit) { s.Queued = Lane.Edit; _editQueue.Enqueue(index); }
        }
        else if (s.Queued == Lane.None) { s.Queued = Lane.Load; _loadQueue.Enqueue(index); }
    }

    void Dispatch()
    {
        while (Volatile.Read(ref _running) < _maxConcurrent)
        {
            int index;
            if (_editQueue.TryDequeue(out index)) { if (_slots[index].Queued != Lane.Edit) continue; }
            else if (_loadQueue.TryDequeue(out index)) { if (_slots[index].Queued != Lane.Load) continue; }
            else break;
            var s = _slots[index];
            s.Queued = Lane.None;
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
        // Sort finished work: edits are staged for their batch, loads wait for the budget. Stale results are dropped.
        while (_done.TryDequeue(out var r))
        {
            if (r.Version != _slots[r.Slot].RequestedVersion) Dispose(r);
            else if (_editPending.Contains(r.Slot))
            {
                if (_staged.Count == 0) _stagedFrames = 0;
                _staged[r.Slot] = r;
            }
            else _loadReady.Enqueue(r);
        }

        // Every staged result is current, so the batch is complete once each pending edit has one.
        if (_staged.Count > 0 && (_staged.Count == _editPending.Count || ++_stagedFrames > MaxEditHoldFrames))
        {
            _appliedEdits.Clear();
            foreach (var r in _staged.Values)
            {
                Apply(r);
                _editPending.Remove(r.Slot);
                _appliedEdits.Add(_slots[r.Slot].Coord);
            }
            _staged.Clear();
            EditsApplied?.Invoke(_appliedEdits);
        }

        long start = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(start).TotalMilliseconds < ApplyBudgetMs && _loadReady.TryDequeue(out var r))
        {
            if (r.Version != _slots[r.Slot].RequestedVersion) Dispose(r);
            else Apply(r);
        }
    }

    void Apply(Result r)
    {
        var s = _slots[r.Slot];
        if (r.Error != null) GD.PushError($"ChunkRenderer: meshing chunk {s.Coord} failed: {r.Error}");

        Apply(ref s.Opaque, ref s.OpaqueMesh, r.Opaque, BlockMaterial, "Opaque", s.Coord, castShadow: true);
        Apply(ref s.Liquid, ref s.LiquidMesh, r.Liquid, LiquidMaterial, "Liquid", s.Coord, castShadow: false);
        UpdateVisibility(s);

        if (s.AppliedVersion == 0) MeshedChunks++;
        s.AppliedVersion = s.RequestedVersion;
        PendingChunks--;
    }

    static void Dispose(Result r)
    {
        r.Opaque?.Dispose();
        r.Liquid?.Dispose();
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
