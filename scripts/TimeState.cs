namespace ColonySim;

/// <summary>
/// Global time-of-day state, accessible from any system without references.
/// DayNightCycle updates these values each frame; UI, colonist AI, etc. read them.
/// Follows the same pattern as SliceState.
/// </summary>
public static class TimeState
{
    /// <summary>Normalized time of day: 0.0=midnight, 0.25=sunrise, 0.5=noon, 0.75=sunset.</summary>
    public static float TimeOfDay { get; set; }

    /// <summary>Whether it is currently nighttime (sun below horizon).</summary>
    public static bool IsNight { get; set; }

    /// <summary>Formatted time string for debug display (e.g., "14:30").</summary>
    public static string FormattedTime { get; set; } = "06:00";
}
