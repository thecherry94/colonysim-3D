namespace ColonySim.Core;

/// <summary>
/// Block renames: save-file block name to current registry name, so a save keeps loading after a block is renamed.
/// <see cref="Default"/> is empty until the first rename.
/// </summary>
public static class BlockAliases
{
    public static IReadOnlyDictionary<string, string> Default { get; } = new Dictionary<string, string>();
}
