namespace ColonySim.Core;

/// <summary>Where a path request stands.</summary>
public enum PathStatus { Pending, Found, Unreachable, TooLong }

/// <summary>
/// A path asked of a <see cref="PathFinder"/> or <see cref="PathService"/>, and its answer once it has one. A request
/// cancelled while pending stays <see cref="PathStatus.Pending"/>.
/// </summary>
public sealed class PathRequest
{
    internal PathRequest(Int3 from, Int3 to)
    {
        From = from;
        To = to;
    }

    public Int3 From { get; }
    public Int3 To { get; }
    public PathStatus Status { get; internal set; }

    /// <summary>When found: the moves in order, the first from <see cref="From"/>, the last to <see cref="To"/>. Empty from a cell to itself.</summary>
    public IReadOnlyList<Move> Moves { get; internal set; } = [];

    /// <summary>When found: the sum of the moves' costs.</summary>
    public float Cost { get; internal set; }

    /// <summary>Cells and sections the search expanded.</summary>
    public int NodesExpanded { get; internal set; }

    /// <summary>The <see cref="Navigation.Version"/> when the answer was given.</summary>
    public int NavVersion { get; internal set; }

    /// <summary>Set by <see cref="PathService.Cancel"/>; the service drops it unanswered.</summary>
    internal bool Cancelled { get; set; }
}
