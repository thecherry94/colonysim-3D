namespace ColonySim.Core;

/// <summary>A named view level the player can jump back to.</summary>
public readonly record struct ViewBookmark(string Name, int Level);
