namespace ColonySim.Core;

public sealed record TerrainParams
{
    public int WaterLevel { get; init; } = 130;
    public int BaseHeight { get; init; } = 138;
    public float HillAmplitude { get; init; } = 10f;
    public float HillFrequency { get; init; } = 0.008f;
    public float MountainCoverage { get; init; } = 0.25f;
    public int MountainMaxHeight { get; init; } = 220;
    public bool HasRiver { get; init; } = true;
    public int RiverWidth { get; init; } = 6;
    public int DeepStoneLevel { get; init; } = 64;
    public int MountainRockLine { get; init; } = 170;
    public int CaveRoof { get; init; } = 6;
}
