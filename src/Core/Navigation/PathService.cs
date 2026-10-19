namespace ColonySim.Core;

/// <summary>
/// Path requests answered over time: <see cref="Process"/> spends a node budget per call on the pending requests in
/// FIFO order, so many colonists re-pathing at once spread over ticks instead of stalling one. Requests that need no
/// search (unreachable ends, a cell to itself) are answered by <see cref="Request"/> itself.
/// <para>
/// A search spread over calls reads the world as it is at each call: when <see cref="Navigation.Version"/> changes
/// meanwhile it finishes on the new data, and <see cref="PathRequest.NavVersion"/> records the version at the answer.
/// Walkers validate each move anyway. Main thread only.
/// </para>
/// </summary>
public sealed class PathService(Navigation nav)
{
    readonly PathFinder _finder = new(nav);
    readonly Queue<PathRequest> _queue = new();

    /// <summary>Requests waiting for an answer (cancelled ones excluded).</summary>
    public int Pending { get; private set; }

    /// <summary>A new request: answered already if it needs no search, otherwise <see cref="PathStatus.Pending"/> until <see cref="Process"/> answers it.</summary>
    public PathRequest Request(Int3 from, Int3 to)
    {
        var r = new PathRequest(from, to);
        if (_finder.AnswerNow(r)) return r;
        _queue.Enqueue(r);
        Pending++;
        return r;
    }

    /// <summary>Advances the pending requests, oldest first, expanding at most <paramref name="nodeBudget"/> nodes in all.</summary>
    public void Process(int nodeBudget)
    {
        while (nodeBudget > 0 && _queue.Count > 0)
        {
            var r = _queue.Peek();
            if (r.Cancelled)
            {
                _queue.Dequeue();
                continue;
            }
            if (_finder.Current != r)
            {
                // The world may have changed since the request was made: re-check what needs no search.
                if (!_finder.AnswerNow(r)) _finder.Begin(r);
                else
                {
                    Answered();
                    continue;
                }
            }
            if (_finder.Advance(ref nodeBudget)) Answered();
        }
    }

    /// <summary>Drops a pending request unanswered (it stays <see cref="PathStatus.Pending"/>); does nothing to an answered one.</summary>
    public void Cancel(PathRequest r)
    {
        if (r.Status != PathStatus.Pending || r.Cancelled) return;
        r.Cancelled = true;
        Pending--;
        if (_finder.Current == r) _finder.Abandon();
    }

    void Answered()
    {
        _queue.Dequeue();
        Pending--;
    }
}
