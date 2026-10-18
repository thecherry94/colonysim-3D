namespace ColonySim.Core;

public static class Blocks
{
    public static readonly BlockRegistry Registry;

    public static readonly ushort Air, Bedrock, Grass, Dirt, Sand, Stone, DeepStone,
        CoalOre, CopperOre, IronOre, GoldOre, Water, OakLog, OakLeaves, PineLog, PineLeaves,
        Cobblestone, WoodPlanks, StoneBricks;

    static readonly ushort[,] StairIds = new ushort[2, 4];

    static Blocks()
    {
        var r = new BlockRegistry();
        Air = r.Add("air", BlockShape.None, false, false);
        Bedrock = r.Add("bedrock", BlockShape.Cube, true, false, "bedrock");
        Grass = r.Add("grass", BlockShape.Cube, true, true, "grass_top", "grass_side", "dirt");
        Dirt = Cube(r, "dirt");
        Sand = Cube(r, "sand");
        Stone = Cube(r, "stone");
        DeepStone = Cube(r, "deep_stone");
        CoalOre = Cube(r, "coal_ore");
        CopperOre = Cube(r, "copper_ore");
        IronOre = Cube(r, "iron_ore");
        GoldOre = Cube(r, "gold_ore");
        Water = r.Add("water", BlockShape.Liquid, false, false, "water");
        OakLog = r.Add("oak_log", BlockShape.Cube, true, true, "oak_log_top", "oak_log", "oak_log_top");
        OakLeaves = Cube(r, "oak_leaves");
        PineLog = r.Add("pine_log", BlockShape.Cube, true, true, "pine_log_top", "pine_log", "pine_log_top");
        PineLeaves = Cube(r, "pine_leaves");
        Cobblestone = Cube(r, "cobblestone");
        WoodPlanks = r.Add("wood_planks", BlockShape.Cube, true, true, "planks");
        StoneBricks = Cube(r, "stone_bricks");

        foreach (var m in Enum.GetValues<BuildMaterial>())
            foreach (var f in Enum.GetValues<Facing>())
            {
                string mat = m == BuildMaterial.Wood ? "wood" : "stone";
                string tex = m == BuildMaterial.Wood ? "planks" : "stone_bricks";
                StairIds[(int)m, (int)f] = r.Add($"{mat}_stairs_{f.ToString().ToLowerInvariant()}",
                    BlockShape.Stairs, true, true, tex, tex, tex, f);
            }

        Registry = r;
    }

    static ushort Cube(BlockRegistry r, string name) => r.Add(name, BlockShape.Cube, true, true, name);

    public static ushort BuildBlock(BuildMaterial m) => m == BuildMaterial.Wood ? WoodPlanks : StoneBricks;

    public static ushort Stairs(BuildMaterial m, Facing f) => StairIds[(int)m, (int)f];
}
