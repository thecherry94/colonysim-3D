namespace ColonySim.Core;

/// <summary>
/// Which cells colonists' bodies are in, for O(1) "is anyone in this cell" questions. A colonist is in its cell and
/// the one above; during a move, at both ends. The <see cref="ColonistMover"/> keeps it current: it re-tracks a
/// colonist after each step, and the simulation tracks every colonist it starts with or spawns.
/// </summary>
public sealed class BodyMap
{
    readonly Dictionary<Int3, int> _count = new();
    readonly Dictionary<int, (Int3 From, Int3? To)> _tracked = new();

    /// <summary>A colonist's body is in the cell.</summary>
    public bool IsOccupied(Int3 cell) => _count.ContainsKey(cell);

    /// <summary>Brings the colonist's entry up to date with its <see cref="Colonist.Cell"/> and <see cref="Colonist.Motion"/>.</summary>
    internal void Track(Colonist c)
    {
        var now = (c.Cell, c.Motion?.To);
        if (_tracked.TryGetValue(c.Id, out var was))
        {
            if (was == now) return;
            Body(was.From, -1);
            if (was.To is { } t) Body(t, -1);
        }
        _tracked[c.Id] = now;
        Body(now.Cell, +1);
        if (now.To is { } to) Body(to, +1);
    }

    void Body(Int3 feet, int delta)
    {
        Add(feet, delta);
        Add(feet + Int3.Up, delta);
    }

    void Add(Int3 cell, int delta)
    {
        int n = _count.GetValueOrDefault(cell) + delta;
        if (n == 0) _count.Remove(cell);
        else _count[cell] = n;
    }
}
