namespace ColonySim.Core;

/// <summary>Simulation time: fixed ticks, 20 per second at 1×.</summary>
public static class SimClock
{
    public const int TicksPerSecond = 20, TicksPerDay = 24000;

    /// <summary>The day a tick falls on, counting from 1.</summary>
    public static int Day(long tick) => (int)(tick / TicksPerDay) + 1;
}
