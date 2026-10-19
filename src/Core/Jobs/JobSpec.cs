namespace ColonySim.Core;

/// <summary>What a designation asks for at a cell. <paramref name="Block"/> is Air for digs, else the block to place.</summary>
public readonly record struct JobSpec(Int3 Position, JobKind Kind, ushort Block);
