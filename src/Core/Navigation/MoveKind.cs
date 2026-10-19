namespace ColonySim.Core;

/// <summary>How a colonist gets from one feet cell to the next. <see cref="Fall"/> is produced only by the simulation, never by <see cref="NavRules"/>.</summary>
public enum MoveKind : byte { Walk, Diagonal, Step, Ladder, Hop, Climb, Fall }
