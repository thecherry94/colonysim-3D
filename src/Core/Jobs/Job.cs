namespace ColonySim.Core;

/// <summary>A pending order at a cell. <paramref name="Block"/> is Air for digs.</summary>
public readonly record struct Job(Int3 Position, JobKind Kind, ushort Block);
