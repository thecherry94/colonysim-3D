namespace ColonySim.Core;

/// <summary>Makes new colonists, and lands the starting ones at the start site.</summary>
public static class ColonistFactory
{
    /// <summary>How far from home the starting colonists may land.</summary>
    public const int SpawnRadius = 6;

    /// <summary>
    /// A new colonist, added to the colony (the caller places it): the next id, a name no colonist of the colony has,
    /// 1–2 strengths at level 6–12 and the other skills at 0–4, every work priority 3.
    /// </summary>
    public static Colonist Create(Colony colony, Random rng)
    {
        int id = colony.NextColonistId++;
        var c = new Colonist(id, UnusedName(colony, rng)) { ShirtColor = id % 8 };
        var skills = Enum.GetValues<SkillType>();
        rng.Shuffle(skills);
        int strengths = rng.Next(1, 3);
        for (int i = 0; i < skills.Length; i++) c.Skills[(int)skills[i]].Level = i < strengths ? rng.Next(6, 13) : rng.Next(0, 5);
        Array.Fill(c.Priorities, 3);
        colony.Colonists.Add(c);
        return c;
    }

    /// <summary>
    /// Creates <paramref name="count"/> colonists on distinct standable cells, the nearest ones to home within
    /// <see cref="SpawnRadius"/> (the first is <see cref="NavRules.FindStandableNear"/>'s; the rest spiral outward).
    /// Seeded from the map, so a seed always lands the same colonists.
    /// </summary>
    public static void SpawnStarting(ColonyMap map, int count = 3)
    {
        var rng = new Random((int)Hash.Of(map.Settings.Seed, 41, 0, 0));
        var rules = new NavRules(map.View); // the map's navigation is built later, by the simulation
        Int3 home = map.Colony.Home;
        var cells = new List<Int3>(count);
        foreach (var offset in Spiral.Value)
        {
            Int3 cell = home + offset;
            if (rules.IsStandable(cell)) cells.Add(cell);
            if (cells.Count == count) break;
        }
        if (cells.Count < count) throw new InvalidOperationException($"No room for {count} colonists within {SpawnRadius} of {home}.");
        foreach (var cell in cells) Create(map.Colony, rng).Cell = cell;
    }

    /// <summary>
    /// Offsets within the <see cref="SpawnRadius"/> cube, nearest first, with <see cref="NavRules.FindStandableNear"/>'s
    /// tie-break (higher y, then lower x, then lower z).
    /// </summary>
    static readonly Lazy<Int3[]> Spiral = new(() =>
    {
        const int r = SpawnRadius;
        var offsets = new List<Int3>();
        for (int y = r; y >= -r; y--)
            for (int x = -r; x <= r; x++)
                for (int z = -r; z <= r; z++) offsets.Add(new Int3(x, y, z));
        // A stable sort keeps the scan order (the tie-break) among equal distances.
        return offsets.OrderBy(o => o.X * o.X + o.Y * o.Y + o.Z * o.Z).ToArray();
    });

    static string UnusedName(Colony colony, Random rng)
    {
        var used = colony.Colonists.Select(c => c.Name).ToHashSet();
        var free = ColonistNames.All.Where(n => !used.Contains(n)).ToList();
        if (free.Count > 0) return free[rng.Next(free.Count)];
        // Every name taken: number one.
        string name = ColonistNames.All[rng.Next(ColonistNames.All.Count)];
        for (int k = 2; ; k++)
            if (!used.Contains($"{name} {k}")) return $"{name} {k}";
    }
}
