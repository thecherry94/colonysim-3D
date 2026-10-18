using ColonySim.Core;
using Godot;

namespace ColonySim.Game;

/// <summary>
/// Debug "instant work": while enabled, completes queued jobs at 10 ticks per second, 4 × <see cref="SpeedMultiplier"/>
/// per tick, in <see cref="InstantWorker"/>'s order (digs top-down, then builds bottom-up).
/// </summary>
public partial class InstantWorkRunner : Node
{
    const double TickSeconds = 0.1;
    const int JobsPerTick = 4;
    public static readonly int[] Speeds = [1, 5, 25];

    ColonyMap _map = null!;
    double _sinceTick;
    int _speed = 1;

    public bool Enabled { get; set; }

    /// <summary>1, 5 or 25; other values snap to the nearest of those.</summary>
    public int SpeedMultiplier
    {
        get => _speed;
        set => _speed = Speeds.MinBy(s => Math.Abs(s - value));
    }

    public void Setup(ColonyMap map) => _map = map;

    public override void _Process(double delta)
    {
        if (_map == null || !Enabled) { _sinceTick = 0; return; }
        _sinceTick += delta;
        if (_sinceTick < TickSeconds) return;
        // At most one tick per frame: a slow frame doesn't trigger a burst of catch-up work.
        _sinceTick = Math.Min(_sinceTick - TickSeconds, TickSeconds);
        InstantWorker.Step(_map, JobsPerTick * _speed);
    }
}
