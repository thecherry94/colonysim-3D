namespace ColonySim.Core;

/// <summary>
/// Connectivity as it would be after one edit (<see cref="Navigation.Preview"/>): the edited cell's chunks rebuilt with
/// the edit, everything else as navigation has it. Its component ids are its own: compare them only with each other,
/// never with <see cref="Navigation.ComponentOf"/>. Valid until the next <see cref="Navigation.Preview"/> or
/// <see cref="Navigation.Update"/>, after which it throws.
/// </summary>
public sealed class ConnectivityPreview
{
    readonly Navigation _nav;
    readonly int _serial;
    readonly int[] _chunkIndices;
    readonly NavChunk[] _chunks;
    readonly int[] _bases;
    readonly int[] _parent;

    internal ConnectivityPreview(Navigation nav, int serial, int[] chunkIndices, NavChunk[] chunks, int[] bases, int[] parent)
    {
        _nav = nav;
        _serial = serial;
        _chunkIndices = chunkIndices;
        _chunks = chunks;
        _bases = bases;
        _parent = parent;
    }

    /// <summary>The cell's component after the edit, or -1 when it wouldn't be standable.</summary>
    public int ComponentOf(Int3 c)
    {
        if (!_nav.IsCurrent(_serial)) throw new InvalidOperationException("The preview is stale: navigation has updated or previewed since.");
        if (!_nav.World.InBounds(c.X, c.Y, c.Z)) return -1;
        int section, k = Array.IndexOf(_chunkIndices, _nav.ChunkIndexOf(c));
        if (k >= 0)
        {
            int local = _chunks[k].LocalOf(c);
            section = local < 0 ? -1 : _bases[k] + local;
        }
        else section = _nav.SectionOf(c);
        return section < 0 ? -1 : UnionFind.Find(_parent, section);
    }
}
