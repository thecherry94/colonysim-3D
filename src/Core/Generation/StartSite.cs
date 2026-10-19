namespace ColonySim.Core;

/// <summary>Where the colonists land: map column (X, Z) and the standing (feet) y of the flattened ground there.</summary>
public readonly record struct StartSite(int X, int Z, int Level);
