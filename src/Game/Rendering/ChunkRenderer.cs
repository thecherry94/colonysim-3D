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
/// dirty) jump ahead of it. The chunks dirtied in one frame form an edit batch, applied all at once as soon as every
/// one of its chunks has its result, so the up to 8 chunks one block change touches swap their meshes in the same
/// frame and never show a seam. Batches complete independently: a steady stream of edits never holds up an earlier
/// batch, and a chunk dirtied again by a later batch moves to that batch. A batch too big to apply in one frame
/// (the fog toggle's full remesh, a large cave reveal) goes through the time-budgeted path instead. The renderer runs
/// after the other nodes each frame, so edits made during a frame are dispatched in that same frame.
/// </para>
/// </summary>
public partial class ChunkRenderer : Node3D
{
    const double ApplyBudgetMs = 6.0;
    /// <summary>After this many frames waiting for the rest of its batch, ready edit results are applied anyway.</summary>
    const int MaxEditHoldFrames = 6;
    /// <summary>Edit batches with more chunks than this are applied within the frame budget, not all at once.</summary>
    const int MaxAtomicEditChunks = 64;
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
        public EditBatch? Batch;      // the batch of its latest request, until that request's result is applied or queued
        public MeshInstance3D? Opaque, Liquid;
        public ArrayMesh? OpaqueMesh, LiquidMesh;
    }

    readonly record struct Result(int Slot, int Version, GArray? Opaque, GArray? Liquid, Exception? Error);

    /// <summary>The chunks dirtied in one frame.</summary>
    sealed class EditBatch
    {
        public readonly HashSet<int> Waiting = new();          // members without a current result yet
        public readonly Dictionary<int, Result> Staged = new(); // current results held until the batch is complete
        public bool Budgeted;                                   // too big for one frame: results go to the budget lane
        public int HeldFrames;                                  // frames spent holding staged results

        public bool IsEmpty => Waiting.Count == 0 && Staged.Count == 0;
    }

    readonly Queue<int> _loadQueue = new(), _editQueue = new();
    readonly ConcurrentQueue<Result> _done = new();
    readonly Queue<Result> _loadReady = new();          // load results waiting for the frame budget
    readonly Queue<Result> _editReady = new();          // results of budgeted edit batches, ahead of the loads
    readonly List<EditBatch> _batches = new();          // oldest first
    readonly List<Int3> _appliedEdits = new();
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
    /// Raised once per frame in which edited chunks swapped in their new meshes, with those chunks.
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
            var batch = new EditBatch { Budgeted = _dirty.Count > MaxAtomicEditChunks };
            _batches.Add(batch);
            foreach (var c in _dirty) Request(Index(c.X, c.Y, c.Z), batch);
            _dirty.Clear();
        }

        Dispatch();
        ApplyResults();
    }

    int Index(int cx, int cy, int cz) => (cy * _map.World.ChunksZ + cz) * _map.World.ChunksX + cx;

    /// <summary>Requests a new mesh: an edit if <paramref name="batch"/> is given, else part of the initial load.</summary>
    void Request(int index, EditBatch? batch = null)
    {
        var s = _slots[index];
        if (!s.Tracked) { s.Tracked = true; TotalChunks++; }
        if (s.AppliedVersion == s.RequestedVersion) PendingChunks++;
        s.RequestedVersion++;
        if (batch != null)
        {
            // Re-dirtied: whatever an earlier batch holds for it is stale now, and it completes with the new batch.
            if (s.Batch is { } old)
            {
                old.Waiting.Remove(index);
                if (old.Staged.Remove(index, out var stale)) Dispose(stale);
            }
            s.Batch = batch;
            batch.Waiting.Add(index);
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
        // Sort finished work: edits are staged in their batch (or queued for the budget if the batch is budgeted),
        // loads wait for the budget. Stale results are dropped.
        while (_done.TryDequeue(out var r))
        {
            var s = _slots[r.Slot];
            if (r.Version != s.RequestedVersion) Dispose(r);
            else if (s.Batch is { } batch)
            {
                batch.Waiting.Remove(r.Slot);
                if (batch.Budgeted) { s.Batch = null; _editReady.Enqueue(r); }
                else batch.Staged[r.Slot] = r;
            }
            else _loadReady.Enqueue(r);
        }

        // Staged results are always current (a new request discards them), so a batch is complete once none of its
        // chunks is still waiting. One that has waited too long for its last chunks applies what it has.
        _appliedEdits.Clear();
        foreach (var batch in _batches)
        {
            if (batch.Staged.Count == 0) continue;
            if (batch.Waiting.Count > 0 && ++batch.HeldFrames <= MaxEditHoldFrames) continue;
            foreach (var r in batch.Staged.Values) ApplyEdit(r);
            batch.Staged.Clear();
        }
        _batches.RemoveAll(static b => b.IsEmpty);

        // Budgeted edits go before the initial load.
        long start = Stopwatch.GetTimestamp();
        bool InBudget() => Stopwatch.GetElapsedTime(start).TotalMilliseconds < ApplyBudgetMs;
        while (InBudget() && _editReady.TryDequeue(out var r))
        {
            if (r.Version != _slots[r.Slot].RequestedVersion) Dispose(r);
            else ApplyEdit(r);
        }
        while (InBudget() && _loadReady.TryDequeue(out var r))
        {
            if (r.Version != _slots[r.Slot].RequestedVersion) Dispose(r);
            else Apply(r);
        }

        if (_appliedEdits.Count > 0) EditsApplied?.Invoke(_appliedEdits);
    }

    void ApplyEdit(Result r)
    {
        var s = _slots[r.Slot];
        s.Batch = null;
        Apply(r);
        _appliedEdits.Add(s.Coord);
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
