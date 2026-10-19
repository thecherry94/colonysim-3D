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
        Assert.Equal(m.Jobs.All.Select(j => j.Spec).OrderBy(j => j.Position.ToString()), r.Jobs.All.Select(j => j.Spec).OrderBy(j => j.Position.ToString()));
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
        m.Jobs.Add(new JobSpec(new Int3(x, y, z), JobKind.Dig, Blocks.Air));
        var ms = new MemoryStream(); MapSerializer.Save(m, ms); ms.Position = 0;
        Assert.Contains("job", Assert.Throws<InvalidDataException>(() => MapSerializer.Load(ms)).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] public void Load_JobsAtMapLimits_AreKept()
    {
        var m = TestWorlds.FlatMap();
        m.Jobs.Add(new JobSpec(new Int3(0, 1, 0), JobKind.Dig, Blocks.Air));
        m.Jobs.Add(new JobSpec(new Int3(63, 255, 63), JobKind.BuildWall, Blocks.StoneBricks));
        Assert.Equal(2, RoundTrip(m).Jobs.Count);
    }

    // ---- format v2: site, jobs with progress, colony ----

    static byte[] Body(MemoryStream file)
    {
        var raw = new MemoryStream(); file.Position = 8;
        using (var br = new System.IO.Compression.BrotliStream(file, System.IO.Compression.CompressionMode.Decompress, true)) br.CopyTo(raw);
        return raw.ToArray();
    }

    static MemoryStream Rebuild(byte[] body, int version = 2)
    {
        var file = new MemoryStream();
        file.Write("CSIM"u8); file.Write(BitConverter.GetBytes(version));
        using (var br = new System.IO.Compression.BrotliStream(file, System.IO.Compression.CompressionLevel.Fastest, true)) br.Write(body);
        file.Position = 0;
        return file;
    }

    /// <summary>Saves the map with the block's name replaced by a same-length one (the first occurrence is the palette's).</summary>
    static MemoryStream SaveRenaming(ColonyMap m, ushort block, string replacement)
    {
        var ms = new MemoryStream(); MapSerializer.Save(m, ms);
        var body = Body(ms);
        var name = System.Text.Encoding.UTF8.GetBytes(Blocks.Registry.Get(block).Name);
        var bytes = System.Text.Encoding.UTF8.GetBytes(replacement); Assert.Equal(name.Length, bytes.Length);
        int at = body.AsSpan().IndexOf(name); Assert.True(at >= 0);
        bytes.CopyTo(body, at);
        return Rebuild(body);
    }

    static ColonyMap Populated()
    {
        var m = TestWorlds.FlatMap();
        var colony = m.Colony;
        colony.Tick = 12345;
        var rng = new Random(3);
        for (int i = 0; i < 3; i++)
        {
            var c = ColonistFactory.Create(colony, rng);
            c.Cell = new Int3(30 + i, 151, 30);
            c.Skills[(int)SkillType.Mining].Xp = 12.5f + i;
            c.Priorities[i % c.Priorities.Length] = i; // 0 (off) through 2
        }
        colony.Bookmarks.Add(new ViewBookmark("Cellar", 120));
        colony.Bookmarks.Add(new ViewBookmark("Roof", 170));
        var j = m.Jobs.Add(new JobSpec(new Int3(5, 150, 5), JobKind.Dig, Blocks.Air));
        m.Jobs.AddProgress(j, 17.25f);
        m.Jobs.Add(new JobSpec(new Int3(8, 151, 8), JobKind.BuildWall, Blocks.WoodPlanks));
        m.Jobs.Remove(m.Jobs.Add(new JobSpec(new Int3(9, 151, 9), JobKind.Dig, Blocks.Air)).Position); // burns an id: NextId > count + 1
        return m;
    }

    static void AssertSameColony(ColonyMap a, ColonyMap b)
    {
        Assert.Equal(a.StartSite, b.StartSite);
        Assert.Equal(a.Colony.Tick, b.Colony.Tick);
        Assert.Equal(a.Colony.Home, b.Colony.Home);
        Assert.Equal(a.Colony.NextColonistId, b.Colony.NextColonistId);
        Assert.Equal(a.Colony.Bookmarks, b.Colony.Bookmarks);
        Assert.Equal(a.Colony.Colonists.Count, b.Colony.Colonists.Count);
        for (int i = 0; i < a.Colony.Colonists.Count; i++)
        {
            var x = a.Colony.Colonists[i]; var y = b.Colony.Colonists[i];
            Assert.Equal((x.Id, x.Name, x.ShirtColor, x.Cell), (y.Id, y.Name, y.ShirtColor, y.Cell));
            Assert.Equal(x.Skills.Select(s => (s.Level, s.Xp)), y.Skills.Select(s => (s.Level, s.Xp)));
            Assert.Equal(x.Priorities, y.Priorities);
        }
        Assert.Equal(a.Jobs.NextId, b.Jobs.NextId);
        Assert.Equal(a.Jobs.All.OrderBy(j => j.Id).Select(j => (j.Id, j.Position, j.Kind, j.Block, j.Progress)),
                     b.Jobs.All.OrderBy(j => j.Id).Select(j => (j.Id, j.Position, j.Kind, j.Block, j.Progress)));
    }

    [Fact] public void RoundTrip_ColonyJobsSiteBookmarks_Exact()
    {
        var m = Populated();
        var r = RoundTrip(m);
        AssertSameColony(m, r);
        Assert.Equal(new StartSite(32, 32, 151), r.StartSite);
        Assert.Equal(2, r.Jobs.Count);
        Assert.Equal(3, r.Colony.Colonists.Count);
        // Restored jobs are indexed like added ones: both are ready.
        Assert.Equal(m.Jobs.ReadyCount, r.Jobs.ReadyCount);
        Assert.Equal(1, r.Jobs.ReadyOfKind(JobKind.Dig));
        Assert.Equal(m.Jobs.NextId, r.Jobs.Add(new JobSpec(new Int3(20, 150, 20), JobKind.Dig, Blocks.Air)).Id);
    }

    [Fact] public void RoundTrip_MidWork_DropsClaimsKeepsProgress()
    {
        var m = TestWorlds.FlatMap();
        var sim = new Simulation(m);
        var miner = sim.Spawn(new Int3(30, 151, 30));
        var walker = sim.Spawn(new Int3(35, 151, 30));
        walker.Priorities[0] = 0; walker.Priorities[1] = 0;
        var dig = m.Jobs.Add(new JobSpec(new Int3(30, 150, 33), JobKind.Dig, Blocks.Air));
        sim.GoTo(walker, new Int3(50, 151, 50));
        for (int t = 0; t < 2000 && !(dig.Progress > 5 && walker.Motion != null); t++) sim.Tick();
        Assert.True(dig.Progress > 5 && walker.Motion != null && dig.ClaimedBy == miner.Id);

        var r = RoundTrip(m);
        var rd = r.Jobs.All.Single();
        Assert.Equal(dig.Progress, rd.Progress);
        Assert.Equal(dig.Id, rd.Id);
        Assert.Equal(0, rd.ClaimedBy);
        Assert.Equal(JobStatus.Open, rd.Status);
        foreach (var c in r.Colony.Colonists)
        {
            Assert.True(r.Nav.Rules.IsStandable(c.Cell), c.Name);
            Assert.Equal(ColonistActivity.Idle, c.Activity);
            Assert.Null(c.Motion);
        }
        Assert.Equal(m.Colony.Colonists.Select(c => c.Cell), r.Colony.Colonists.Select(c => c.Cell));
    }

    [Fact] public void Load_ColonistOnNonStandableCell_MovedNearby()
    {
        var m = Populated();
        m.Colony.Colonists[0].Cell = new Int3(30, 148, 30); // inside the rock, 3 below the surface
        var r = RoundTrip(m);
        var cell = r.Colony.Colonists[0].Cell;
        Assert.True(r.Nav.Rules.IsStandable(cell));
        Assert.True(Math.Abs(cell.X - 30) <= 8 && Math.Abs(cell.Y - 148) <= 8 && Math.Abs(cell.Z - 30) <= 8);
    }

    [Fact] public void Load_ColonistWithNowhereNear_GoesHome()
    {
        var m = Populated();
        m.Colony.Colonists[0].Cell = new Int3(30, 100, 30); // deep rock: nothing standable within 8
        Assert.Equal(m.Colony.Home, RoundTrip(m).Colony.Colonists[0].Cell);
    }

    [Fact] public void Load_Version1_ThrowsReadableMessage()
    {
        var ms = new MemoryStream(); MapSerializer.Save(TestWorlds.FlatMap(), ms); var bytes = ms.ToArray();
        BitConverter.GetBytes(1).CopyTo(bytes, 4);
        var ex = Assert.Throws<InvalidDataException>(() => MapSerializer.Load(new MemoryStream(bytes)));
        Assert.Equal("This save was made by an older version of ColonySim (format 1) and can't be loaded.", ex.Message);
    }

    [Fact] public void Load_UnknownUnusedPaletteName_Tolerated()
    {
        var m = TestWorlds.FlatMap();
        ushort unused = (ushort)(Blocks.Registry.All.Count - 1);
        Assert.DoesNotContain(m.World.GetBlock(1, 1, 1), new[] { unused });
        var file = SaveRenaming(m, unused, "#" + Blocks.Registry.Get(unused).Name[1..]);
        var r = MapSerializer.Load(file);
        Assert.Equal(TestWorlds.WorldHash(m.World), TestWorlds.WorldHash(r.World));
    }

    [Fact] public void Load_UnknownUsedName_ThrowsNamingIt()
    {
        var file = SaveRenaming(TestWorlds.FlatMap(), Blocks.Stone, "#" + Blocks.Registry.Get(Blocks.Stone).Name[1..]);
        var ex = Assert.Throws<InvalidDataException>(() => MapSerializer.Load(file));
        Assert.Contains("#" + Blocks.Registry.Get(Blocks.Stone).Name[1..], ex.Message);
    }

    [Fact] public void Load_UnknownNameUsedByJob_ThrowsNamingIt()
    {
        var m = TestWorlds.FlatMap();
        m.Jobs.Add(new JobSpec(new Int3(8, 151, 8), JobKind.BuildWall, Blocks.WoodPlanks));
        string fake = "#" + Blocks.Registry.Get(Blocks.WoodPlanks).Name[1..];
        var ex = Assert.Throws<InvalidDataException>(() => MapSerializer.Load(SaveRenaming(m, Blocks.WoodPlanks, fake)));
        Assert.Contains(fake, ex.Message);
    }

    [Fact] public void Load_Alias_MapsRenamedBlock()
    {
        var m = TestWorlds.FlatMap();
        string old = "#" + Blocks.Registry.Get(Blocks.Stone).Name[1..];
        var file = SaveRenaming(m, Blocks.Stone, old);
        var r = MapSerializer.Load(file, new Dictionary<string, string> { [old] = Blocks.Registry.Get(Blocks.Stone).Name });
        Assert.Equal(TestWorlds.WorldHash(m.World), TestWorlds.WorldHash(r.World));
    }

    [Fact] public void Load_MorePrioritiesThanWorkTypes_Throws()
    {
        var m = Populated();
        var ms = new MemoryStream(); MapSerializer.Save(m, ms);
        var body = Body(ms);
        // The last colonist's priorities are followed by the bookmark section: count byte, N bytes, then 4 + (1+6) + 4 + (1+4) + 4 bytes.
        int tail = 4 + 4 + 1 + "Cellar".Length + 4 + 1 + "Roof".Length + 4 - 4;
        int countAt = body.Length - tail - WorkTypes.All.Count - 1;
        Assert.Equal(WorkTypes.All.Count, body[countAt]);
        body[countAt] = (byte)(WorkTypes.All.Count + 1);
        Assert.Throws<InvalidDataException>(() => MapSerializer.Load(Rebuild(body)));
    }
}
