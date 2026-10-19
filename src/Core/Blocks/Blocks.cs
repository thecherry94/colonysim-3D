namespace ColonySim.Core;

public static class Blocks
{
    public static readonly BlockRegistry Registry;

    public static readonly ushort Air, Bedrock, Grass, Dirt, Sand, Stone, DeepStone,
        CoalOre, CopperOre, IronOre, GoldOre, Water, OakLog, OakLeaves, PineLog, PineLeaves,
        Cobblestone, WoodPlanks, StoneBricks;

    static readonly ushort[,] StairIds = new ushort[2, 4];
    static readonly ushort[,] RampIds = new ushort[2, 4];
    static readonly ushort[,,] SlopeIds = new ushort[4, 3, 4]; // [Grass,Dirt,Sand,Stone][Ramp,CornerOuter,CornerInner][facing]
    static readonly ushort[] LadderIds = new ushort[4];
    static readonly ushort[,] CarvedIds = new ushort[3, 4]; // [dirt,stone,deep_stone][facing]
    static readonly ushort[] SlopeBases;
    static readonly BlockShape[] SlopeShapes = { BlockShape.Ramp, BlockShape.CornerOuter, BlockShape.CornerInner };

    static Blocks()
    {
        var r = new BlockRegistry();
        Air = r.Add("air", BlockShape.None, false, false);
        Bedrock = r.Add("bedrock", BlockShape.Cube, true, false, "bedrock", natural: true);
        Grass = r.Add("grass", BlockShape.Cube, true, true, "grass_top", "grass_side", "dirt", natural: true, hardness: 1.0f);
        Dirt = Cube(r, "dirt", 1.0f, true);
        Sand = Cube(r, "sand", 1.0f, true);
        Stone = Cube(r, "stone", 2.5f, true);
        DeepStone = Cube(r, "deep_stone", 3.5f, true);
        CoalOre = Cube(r, "coal_ore", 3.0f, true);
        CopperOre = Cube(r, "copper_ore", 3.0f, true);
        IronOre = Cube(r, "iron_ore", 3.0f, true);
        GoldOre = Cube(r, "gold_ore", 3.0f, true);
        Water = r.Add("water", BlockShape.Liquid, false, false, "water");
        OakLog = r.Add("oak_log", BlockShape.Cube, true, true, "oak_log_top", "oak_log", "oak_log_top", hardness: 2.0f);
        OakLeaves = Cube(r, "oak_leaves", 0.5f);
        PineLog = r.Add("pine_log", BlockShape.Cube, true, true, "pine_log_top", "pine_log", "pine_log_top", hardness: 2.0f);
        PineLeaves = Cube(r, "pine_leaves", 0.5f);
        Cobblestone = Cube(r, "cobblestone", 2.5f, true);
        WoodPlanks = r.Add("wood_planks", BlockShape.Cube, true, true, "planks", hardness: 1.5f);
        StoneBricks = Cube(r, "stone_bricks", 3.0f);

        foreach (var m in Enum.GetValues<BuildMaterial>())
            foreach (var f in Enum.GetValues<Facing>())
            {
                string mat = m == BuildMaterial.Wood ? "wood" : "stone";
                string tex = m == BuildMaterial.Wood ? "planks" : "stone_bricks";
                StairIds[(int)m, (int)f] = r.Add($"{mat}_stairs_{f.ToString().ToLowerInvariant()}",
                    BlockShape.Stairs, true, true, tex, tex, tex, f,
                    hardness: m == BuildMaterial.Wood ? 1.5f : 3.0f, baseBlock: BuildBlock(m));
            }

        SlopeBases = new[] { Grass, Dirt, Sand, Stone };
        string[] facings = { "north", "east", "south", "west" };

        for (int bi = 0; bi < 4; bi++)
        {
            ushort bb = SlopeBases[bi];
            var bd = r.Get(bb);
            string bn = bd.Name;
            // "stone_ramp_*" is the built stone-brick ramp, so natural stone slopes are named "natural_stone_*".
            string slopeName = bb == Stone ? "natural_stone" : bn;
            string[] tex = bb == Grass ? new[] { "grass_top", "grass_side", "dirt" } : new[] { bn, bn, bn };
            for (int si = 0; si < 3; si++)
                foreach (var f in Enum.GetValues<Facing>())
                {
                    string sn = SlopeShapes[si] == BlockShape.Ramp ? "ramp" : SlopeShapes[si] == BlockShape.CornerOuter ? "corner_outer" : "corner_inner";
                    SlopeIds[bi, si, (int)f] = r.Add($"{slopeName}_{sn}_{facings[(int)f]}", SlopeShapes[si], true, true,
                        tex[0], tex[1], tex[2], f, natural: true, hardness: bd.Hardness, baseBlock: bb);
                }
        }

        foreach (var m in Enum.GetValues<BuildMaterial>())
            foreach (var f in Enum.GetValues<Facing>())
            {
                string mat = m == BuildMaterial.Wood ? "wood" : "stone";
                string tex = m == BuildMaterial.Wood ? "planks" : "stone_bricks";
                RampIds[(int)m, (int)f] = r.Add($"{mat}_ramp_{facings[(int)f]}", BlockShape.Ramp, true, true, tex, tex, tex, f,
                    hardness: r.Get(BuildBlock(m)).Hardness, baseBlock: BuildBlock(m));
            }

        foreach (var f in Enum.GetValues<Facing>())
            LadderIds[(int)f] = r.Add($"ladder_{facings[(int)f]}", BlockShape.Ladder, false, true, "planks", facing: f,
                hardness: 1.0f, baseBlock: WoodPlanks);

        ushort[] carvedBase = { Dirt, Stone, DeepStone };
        string[] carvedName = { "dirt", "stone", "deep_stone" };
        for (int ci = 0; ci < 3; ci++)
            foreach (var f in Enum.GetValues<Facing>())
                CarvedIds[ci, (int)f] = r.Add($"carved_{carvedName[ci]}_stairs_{facings[(int)f]}", BlockShape.Stairs, true, true,
                    carvedName[ci], carvedName[ci], carvedName[ci], f, hardness: r.Get(carvedBase[ci]).Hardness, baseBlock: carvedBase[ci]);

        Registry = r;
    }

    static ushort Cube(BlockRegistry r, string name, float hardness = 0f, bool natural = false) =>
        r.Add(name, BlockShape.Cube, true, true, name, natural: natural, hardness: hardness);

    public static ushort BuildBlock(BuildMaterial m) => m == BuildMaterial.Wood ? WoodPlanks : StoneBricks;

    public static ushort Stairs(BuildMaterial m, Facing f) => StairIds[(int)m, (int)f];

    /// <summary>Natural slope variant; <paramref name="baseBlock"/> must be Grass, Dirt, Sand or Stone and <paramref name="shape"/> a slope shape.</summary>
    public static ushort NaturalSlope(ushort baseBlock, BlockShape shape, Facing f)
    {
        int bi = Array.IndexOf(SlopeBases, baseBlock), si = Array.IndexOf(SlopeShapes, shape);
        if (bi < 0) throw new ArgumentException($"No natural slopes for block {baseBlock}", nameof(baseBlock));
        if (si < 0) throw new ArgumentException($"Not a slope shape: {shape}", nameof(shape));
        return SlopeIds[bi, si, (int)f];
    }

    /// <summary>The slope base block for a surface block: Grass, Dirt and Sand map to themselves, everything else to Stone.</summary>
    public static ushort SlopeBaseFor(ushort topBlock) =>
        topBlock == Grass || topBlock == Dirt || topBlock == Sand ? topBlock : Stone;

    public static ushort BuiltRamp(BuildMaterial m, Facing f) => RampIds[(int)m, (int)f];

    /// <summary><paramref name="f"/> is the side of the cell the ladder is mounted against.</summary>
    public static ushort Ladder(Facing f) => LadderIds[(int)f];

    /// <summary>Stairs carved out of the dug block: Grass/Dirt/Sand give dirt, DeepStone deep stone, anything else stone.</summary>
    public static ushort CarvedStairs(ushort dugBlock, Facing f) =>
        CarvedIds[dugBlock == Grass || dugBlock == Dirt || dugBlock == Sand ? 0 : dugBlock == DeepStone ? 2 : 1, (int)f];
}
