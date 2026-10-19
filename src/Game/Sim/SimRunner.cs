using System.Diagnostics;
using ColonySim.Core;
using Godot;

namespace ColonySim.Game;

/// <summary>
/// Drives the <see cref="Simulation"/> in fixed ticks: <see cref="Speed"/> × <see cref="SimClock.TicksPerSecond"/> per
/// second (0 = paused), at most <see cref="MaxTicksPerFrame"/> in a frame (a slow frame drops the backlog instead of
/// spiralling). Space toggles pause, restoring the last speed; 1, 2 and 3 set the speed.
/// </summary>
public partial class SimRunner : Node
{
    public const int MaxSpeed = 3, MaxTicksPerFrame = 8;
    const double AverageSeconds = 2;

    readonly Queue<(long At, double Ms)> _recent = new();
    double _recentSum, _owed;
    int _speed = 1, _resumeSpeed = 1;

    public Simulation Sim { get; private set; } = null!;

    /// <summary>0 = paused, 1–3.</summary>
    public int Speed
    {
        get => _speed;
        set
        {
            _speed = Math.Clamp(value, 0, MaxSpeed);
            if (_speed > 0) _resumeSpeed = _speed;
        }
    }

    /// <summary>The fraction (0..1) of the way to the next tick, for drawing between ticks.</summary>
    public float Alpha { get; private set; }

    /// <summary>The last tick's duration.</summary>
    public double LastTickMs { get; private set; }

    /// <summary>The tick duration averaged over the last two seconds of ticking.</summary>
    public double AvgTickMs { get; private set; }

    public void Setup(Simulation sim) => Sim = sim;

    public override void _Process(double delta)
    {
        if (Sim == null || _speed == 0) return;
        _owed += delta * _speed * SimClock.TicksPerSecond;
        int ticks = Math.Min((int)_owed, MaxTicksPerFrame);
        _owed -= ticks;
        if (_owed >= 1) _owed = 0; // over the cap: the rest is dropped
        for (int i = 0; i < ticks; i++) TimedTick();
        Alpha = (float)_owed;
    }

    void TimedTick()
    {
        long start = Stopwatch.GetTimestamp();
        Sim.Tick();
        long end = Stopwatch.GetTimestamp();
        LastTickMs = Stopwatch.GetElapsedTime(start, end).TotalMilliseconds;
        _recent.Enqueue((end, LastTickMs));
        _recentSum += LastTickMs;
        while (Stopwatch.GetElapsedTime(_recent.Peek().At, end).TotalSeconds > AverageSeconds)
            _recentSum -= _recent.Dequeue().Ms;
        AvgTickMs = _recentSum / _recent.Count;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed(InputActions.SimPause)) Speed = _speed == 0 ? _resumeSpeed : 0;
        else if (@event.IsActionPressed(InputActions.SimSpeed1)) Speed = 1;
        else if (@event.IsActionPressed(InputActions.SimSpeed2)) Speed = 2;
        else if (@event.IsActionPressed(InputActions.SimSpeed3)) Speed = 3;
        else return;
        GetViewport().SetInputAsHandled();
    }
}
