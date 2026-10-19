using System.Diagnostics;

namespace ColonySim.Core;

/// <summary>A move from a chunk's standable cell into another chunk, kept from the chunk's build until its links are made.</summary>
internal readonly record struct CrossMove(ushort FromLocal, Int3 From, Int3 To, float Cost, float ReverseCost);

/// <summary>
/// The navigation sections of one 32³ chunk: the connected sets of its standable cells under the moves that stay inside
/// it. Only standable cells are stored, sparsely: a sorted key per cell and the local id of its section.
/// <see cref="Sections"/> maps local ids to the global ids <see cref="Navigation"/> hands out.
/// </summary>
internal sealed class NavChunk
{
    const ushort Unlabeled = ushort.MaxValue;

    /// <summary>A chunk without standable cells (shared: never mutated).</summary>
    public static readonly NavChunk Empty = new([], [], [], []);

    /// <summary>Cell keys (<see cref="KeyOf"/>), ascending.</summary>
    public readonly ushort[] Keys;

    /// <summary>The local section of each key.</summary>
    public readonly ushort[] Local;

    /// <summary>One standable cell per local section: the member nearest the section's mean position.</summary>
    public readonly Int3[] Anchors;

    /// <summary>Global section id per local section.</summary>
    public int[] Sections = [];

    /// <summary>The moves out of the chunk found by the build, until <see cref="DropCross"/>.</summary>
    public CrossMove[] Cross { get; private set; }

    NavChunk(ushort[] keys, ushort[] local, Int3[] anchors, CrossMove[] cross)
    {
        Keys = keys;
        Local = local;
        Anchors = anchors;
        Cross = cross;
    }

    public int SectionCount => Anchors.Length;

    /// <summary>Frees the build's moves out once the chunk's links are made.</summary>
    public void DropCross()
    {
        if (Cross.Length > 0) Cross = [];
    }

    /// <summary>A cell's key within its chunk: x, then z, then y, so one column's cells are adjacent.</summary>
    public static int KeyOf(Int3 c) => ((c.X & 31) << 10) | ((c.Z & 31) << 5) | (c.Y & 31);

    /// <summary>The local section of a cell of this chunk, or -1 when it isn't standable.</summary>
    public int LocalOf(Int3 c)
    {
        int i = Keys.AsSpan().BinarySearch((ushort)KeyOf(c));
        return i < 0 ? -1 : Local[i];
    }

    /// <summary>Same cells in the same sections with the same local ids (builds are deterministic).</summary>
    public bool SameSections(NavChunk other) =>
        Keys.AsSpan().SequenceEqual(other.Keys) && Local.AsSpan().SequenceEqual(other.Local);

    /// <summary>Scratch buffers for <see cref="Build"/>; one per thread.</summary>
    public sealed class Builder
    {
        internal readonly ushort[] IndexOfKey = new ushort[VoxelWorld.ChunkSize * VoxelWorld.ChunkSize * VoxelWorld.ChunkSize];
        internal readonly int[] Queue = new int[VoxelWorld.ChunkSize * VoxelWorld.ChunkSize * VoxelWorld.ChunkSize];
        internal readonly List<ushort> Keys = new();
        internal readonly List<Int3> Anchors = new();
        internal readonly List<CrossMove> Cross = new();
        internal readonly Move[] Moves = new Move[NavRules.MaxMoves];
    }

    /// <summary>Finds the chunk's standable cells and floods its sections. Only reads through <paramref name="rules"/>.</summary>
    public static NavChunk Build<TBlocks>(NavRules<TBlocks> rules, Int3 chunk, Builder b) where TBlocks : struct, IKnownBlocks
    {
        int x0 = chunk.X * VoxelWorld.ChunkSize, y0 = chunk.Y * VoxelWorld.ChunkSize, z0 = chunk.Z * VoxelWorld.ChunkSize;
        var keys = b.Keys;
        keys.Clear();
        Span<bool> column = stackalloc bool[VoxelWorld.ChunkSize];
        for (int lx = 0; lx < VoxelWorld.ChunkSize; lx++)
            for (int lz = 0; lz < VoxelWorld.ChunkSize; lz++)
            {
                rules.StandableColumn(x0 + lx, z0 + lz, y0, column);
                for (int ly = 0; ly < VoxelWorld.ChunkSize; ly++)
                    if (column[ly]) keys.Add((ushort)((lx << 10) | (lz << 5) | ly));
            }
        int n = keys.Count;
        if (n == 0) return Empty;

        for (int i = 0; i < n; i++) b.IndexOfKey[keys[i]] = (ushort)(i + 1);
        var local = new ushort[n];
        Array.Fill(local, Unlabeled);
        b.Anchors.Clear();
        b.Cross.Clear();
        int head = 0, tail = 0;
        for (int seed = 0; seed < n; seed++)
        {
            if (local[seed] != Unlabeled) continue;
            ushort s = (ushort)b.Anchors.Count;
            local[seed] = s;
            int start = tail;
            b.Queue[tail++] = seed;
            long sx = 0, sy = 0, sz = 0;
            while (head < tail)
            {
                Int3 c = CellOf(keys[b.Queue[head++]], x0, y0, z0);
                sx += c.X; sy += c.Y; sz += c.Z;
                int m = rules.GetMoves(c, b.Moves);
                for (int k = 0; k < m; k++)
                {
                    Int3 to = b.Moves[k].To;
                    if (to.X >> 5 != chunk.X || to.Y >> 5 != chunk.Y || to.Z >> 5 != chunk.Z)
                    {
                        b.Cross.Add(new CrossMove(s, c, to, b.Moves[k].Cost, rules.ReverseCost(c, b.Moves[k])));
                        continue;
                    }
                    int j = b.IndexOfKey[KeyOf(to)] - 1;
                    Debug.Assert(j >= 0, $"move target {to} is not standable");
                    if (local[j] != Unlabeled) continue;
                    local[j] = s;
                    b.Queue[tail++] = j;
                }
            }
            b.Anchors.Add(NearestMember(keys, b.Queue.AsSpan(start, tail - start), x0, y0, z0, sx, sy, sz));
        }
        for (int i = 0; i < n; i++) b.IndexOfKey[keys[i]] = 0;
        return new NavChunk(keys.ToArray(), local, b.Anchors.ToArray(), b.Cross.ToArray());
    }

    static Int3 CellOf(ushort key, int x0, int y0, int z0) => new(x0 + (key >> 10), y0 + (key & 31), z0 + ((key >> 5) & 31));

    /// <summary>The member closest to the members' mean (sums <paramref name="sx"/>..<paramref name="sz"/>), so anchors sit mid-section.</summary>
    static Int3 NearestMember(List<ushort> keys, ReadOnlySpan<int> members, int x0, int y0, int z0, long sx, long sy, long sz)
    {
        double mx = (double)sx / members.Length, my = (double)sy / members.Length, mz = (double)sz / members.Length;
        Int3 best = default;
        double bestD = double.MaxValue;
        foreach (int i in members)
        {
            Int3 c = CellOf(keys[i], x0, y0, z0);
            double dx = c.X - mx, dy = c.Y - my, dz = c.Z - mz, d = dx * dx + dy * dy + dz * dz;
            if (d < bestD) { bestD = d; best = c; }
        }
        return best;
    }
}
