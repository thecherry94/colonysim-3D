using ColonySim.Core;
using Godot;

namespace ColonySim.Game;

/// <summary>Save files in <see cref="Dir"/>: one <see cref="MapSerializer"/> file per save, named by seed and time.</summary>
public static class SaveGames
{
    public const string Dir = "user://saves";
    const string Extension = ".csim";

    static string DirPath => ProjectSettings.GlobalizePath(Dir);

    /// <summary>
    /// Writes <c>seed-{seed}-{yyyyMMdd-HHmmss}.csim</c> and returns its file name. The file is written under a temporary
    /// name first, so a failed save never leaves a truncated file in the list.
    /// </summary>
    public static string Save(ColonyMap map)
    {
        Directory.CreateDirectory(DirPath);
        string name = $"seed-{map.Settings.Seed}-{DateTime.Now:yyyyMMdd-HHmmss}{Extension}";
        string path = Path.Combine(DirPath, name), temp = path + ".tmp";
        try
        {
            using (var file = File.Create(temp)) MapSerializer.Save(map, file);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            File.Delete(temp);
        }
        return name;
    }

    /// <summary>Save file names, newest first.</summary>
    public static IReadOnlyList<string> List()
    {
        var dir = new DirectoryInfo(DirPath);
        if (!dir.Exists) return [];
        return dir.EnumerateFiles("*" + Extension)
            .Where(f => f.Name.EndsWith(Extension, StringComparison.Ordinal))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ThenByDescending(f => f.Name, StringComparer.Ordinal)
            .Select(f => f.Name)
            .ToList();
    }

    /// <summary>
    /// Loads a save by file name. Throws <see cref="InvalidDataException"/> for a bad, corrupt or newer file and
    /// <see cref="IOException"/> when it can't be read.
    /// </summary>
    public static ColonyMap Load(string fileName)
    {
        using var file = File.OpenRead(Path.Combine(DirPath, Path.GetFileName(fileName)));
        return MapSerializer.Load(file);
    }
}
