namespace ColonySim.Core.Tests;

public class MapSerializerTests
{
    static ColonyMap RoundTrip(ColonyMap m) { var ms = new MemoryStream(); MapSerializer.Save(m, ms); ms.Position = 0; return MapSerializer.Load(ms); }

    [Fact] public void RoundTripsEverything()
    {
        var m = MapGenerator.Generate(new MapSettings(5, 64, 64, new TerrainParams()));
        m.World.SetBlock(10, 140, 10, Blocks.StoneBricks); Discovery.RevealFrom(m.World, 10, 141, 10);
        Designator.Apply(m, DesignationTool.Dig, BuildMaterial.Wood, new DragBox(new(1, 120, 1), new(4, 118, 4)));
        var r = RoundTrip(m);
        Assert.Equal(m.Settings, r.Settings); Assert.Equal(TestWorlds.WorldHash(m.World), TestWorlds.WorldHash(r.World));
        for (int x = 0; x < 64; x += 3) for (int z = 0; z < 64; z += 3) { Assert.Equal(m.World.GetSurfaceHeight(x, z), r.World.GetSurfaceHeight(x, z)); for (int y = 0; y < 256; y += 5) Assert.Equal(m.World.IsDiscovered(x, y, z), r.World.IsDiscovered(x, y, z)); }
        Assert.Equal(m.Jobs.All.OrderBy(j => j.Position.ToString()), r.Jobs.All.OrderBy(j => j.Position.ToString()));
    }

    [Fact] public void RoundTrip_NonDefaultTerrainParams_AreKept()
    {
        var t = new TerrainParams { WaterLevel = 120, BaseHeight = 141, HillAmplitude = 7.5f, HillFrequency = 0.013f, MountainCoverage = 0.4f, MountainMaxHeight = 200, HasRiver = false, RiverWidth = 3, DeepStoneLevel = 50, MountainRockLine = 160, CaveRoof = 9 };
        var m = new ColonyMap(new MapSettings(-77, 64, 32, t), new VoxelWorld(64, 32));
        Assert.Equal(m.Settings, RoundTrip(m).Settings);
    }

    [Fact] public void Load_DoesNotLeaveDirtyChunks()
    {
        var r = RoundTrip(TestWorlds.FlatMap());
        var dirty = new List<Int3>(); r.World.ConsumeDirtyChunks(dirty);
        Assert.Empty(dirty);
    }

    [Fact] public void Load_UnknownBlockName_ThrowsInvalidData()
    {
        var ms = new MemoryStream(); MapSerializer.Save(TestWorlds.FlatMap(), ms);
        var raw = new MemoryStream();
        ms.Position = 8;
        using (var br = new System.IO.Compression.BrotliStream(ms, System.IO.Compression.CompressionMode.Decompress)) br.CopyTo(raw);
        var body = raw.ToArray();
        var name = System.Text.Encoding.UTF8.GetBytes(Blocks.Registry.All[1].Name);
        var bogus = name.ToArray(); bogus[0] = (byte)'#';
        int at = body.AsSpan().IndexOf(name); Assert.True(at >= 0);
        bogus.CopyTo(body, at);
        var file = new MemoryStream();
        file.Write("CSIM"u8); file.Write(BitConverter.GetBytes(MapSerializer.FormatVersion));
        using (var br = new System.IO.Compression.BrotliStream(file, System.IO.Compression.CompressionLevel.Fastest, true)) br.Write(body);
        file.Position = 0;
        var ex = Assert.Throws<InvalidDataException>(() => MapSerializer.Load(file));
        Assert.Contains("#", ex.Message);
    }

    [Fact] public void Load_BadMagic_ThrowsInvalidData() => Assert.Throws<InvalidDataException>(() => MapSerializer.Load(new MemoryStream("NOPE1234"u8.ToArray())));

    [Fact] public void Load_Truncated_ThrowsInvalidData()
    {
        var ms = new MemoryStream(); MapSerializer.Save(TestWorlds.FlatMap(), ms);
        Assert.Throws<InvalidDataException>(() => MapSerializer.Load(new MemoryStream(ms.ToArray()[..(int)(ms.Length / 2)])));
    }

    [Fact] public void Load_Garbage_ThrowsInvalidData()
    {
        var bytes = new byte[200]; "CSIM"u8.CopyTo(bytes); BitConverter.GetBytes(MapSerializer.FormatVersion).CopyTo(bytes, 4);
        new Random(1).NextBytes(bytes.AsSpan(8));
        Assert.Throws<InvalidDataException>(() => MapSerializer.Load(new MemoryStream(bytes)));
    }

    [Fact] public void Load_NewerVersion_ThrowsInvalidData()
    {
        var ms = new MemoryStream(); MapSerializer.Save(TestWorlds.FlatMap(), ms); var bytes = ms.ToArray();
        BitConverter.GetBytes(MapSerializer.FormatVersion + 1).CopyTo(bytes, 4);
        Assert.Contains("version", Assert.Throws<InvalidDataException>(() => MapSerializer.Load(new MemoryStream(bytes))).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(-1, 140, 5)]
    [InlineData(64, 140, 5)]
    [InlineData(5, 140, -1)]
    [InlineData(5, 140, 64)]
    [InlineData(5, 0, 5)]
    [InlineData(5, 256, 5)]
    public void Load_JobOutsideMap_ThrowsInvalidData(int x, int y, int z)
    {
        var m = TestWorlds.FlatMap();
        m.Jobs.Add(new Job(new Int3(x, y, z), JobKind.Dig, Blocks.Air));
        var ms = new MemoryStream(); MapSerializer.Save(m, ms); ms.Position = 0;
        Assert.Contains("job", Assert.Throws<InvalidDataException>(() => MapSerializer.Load(ms)).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] public void Load_JobsAtMapLimits_AreKept()
    {
        var m = TestWorlds.FlatMap();
        m.Jobs.Add(new Job(new Int3(0, 1, 0), JobKind.Dig, Blocks.Air));
        m.Jobs.Add(new Job(new Int3(63, 255, 63), JobKind.BuildWall, Blocks.StoneBricks));
        Assert.Equal(2, RoundTrip(m).Jobs.Count);
    }
}
