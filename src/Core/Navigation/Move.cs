namespace ColonySim.Core;

/// <summary>One move to the feet cell <paramref name="To"/>: its path cost and how many ticks it takes.</summary>
public readonly record struct Move(Int3 To, MoveKind Kind, float Cost, int Ticks);
