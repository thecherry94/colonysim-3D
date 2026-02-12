namespace ColonySim;

/// <summary>
/// Global Y-level slice state, accessible from any system.
/// CameraController updates these values; Colonist, BlockInteraction, etc. read them.
/// </summary>
public static class SliceState
{
    /// <summary>Whether Y-level slicing is currently active.</summary>
    public static bool Enabled { get; set; }

    /// <summary>Current Y-level slice height. Everything above this is hidden.</summary>
    public static int YLevel { get; set; } = 999;
}
