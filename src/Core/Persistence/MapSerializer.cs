using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace ColonySim.Core;

/// <summary>
/// Binary save format: "CSIM", int32 version, then a Brotli-compressed body (little-endian):
/// settings, block-name palette, surface heights, per-chunk blocks + discovery, start site, jobs
/// (with progress; claims and status are not saved), the job id counter and the colony (clock, home, colonists, bookmarks).
/// Transient state (motion, activity, walks) is not saved: a loaded colonist is idle.
/// </summary>
public static class MapSerializer
{
    public const int FormatVersion = 2;
    const int MaxDimension = 4096;
    static readonly byte[] Magic = "CSIM"u8.ToArray();

    public static void Save(ColonyMap map, Stream stream)
    {
        stream.Write(Magic);
        Span<byte> ver = stackalloc byte[4];
        BitConverter.TryWriteBytes(ver, FormatVersion);
        stream.Write(ver);

        using var brotli = new BrotliStream(stream, CompressionLevel.Fastest, leaveOpen: true);
        using var w = new BinaryWriter(brotli, Encoding.UTF8, leaveOpen: true);
        var world = map.World;

        var s = map.Settings;
        w.Write(s.Seed); w.Write(s.SizeX); w.Write(s.SizeZ);
        var t = s.Terrain;
        w.Write(t.WaterLevel); w.Write(t.BaseHeight); w.Write(t.HillAmplitude); w.Write(t.HillFrequency);
        w.Write(t.MountainCoverage); w.Write(t.MountainMaxHeight); w.Write(t.HasRiver); w.Write(t.RiverWidth);
        w.Write(t.DeepStoneLevel); w.Write(t.MountainRockLine); w.Write(t.CaveRoof);

        // Palette is the whole registry, so a palette index equals the block id at save time.
        var defs = Blocks.Registry.All;
        w.Write(defs.Count);
        foreach (var d in defs) w.Write(d.Name);

        for (int z = 0; z < world.SizeZ; z++)
            for (int x = 0; x < world.SizeX; x++)
                w.Write((short)world.GetSurfaceHeight(x, z));

        for (int cy = 0; cy < world.ChunksY; cy++)
            for (int cz = 0; cz < world.ChunksZ; cz++)
                for (int cx = 0; cx < world.ChunksX; cx++)
                {
                    var c = world.ChunkByCoords(cx, cy, cz);
                    if (c.Blocks is { } blocks)
                    {
                        w.Write(false);
                        w.Flush();
                        brotli.Write(MemoryMarshal.AsBytes(blocks.AsSpan()));
                    }
                    else { w.Write(true); w.Write(c.UniformId); }

                    if (c.Discovery is { } disc)
                    {
                        w.Write(false);
                        w.Flush();
                        brotli.Write(MemoryMarshal.AsBytes(disc.AsSpan()));
                    }
                    else w.Write(true);
                }

        var site = map.StartSite;
        w.Write(site.X); w.Write(site.Z); w.Write(site.Level);

        w.Write(map.Jobs.Count);
        foreach (var j in map.Jobs.All)
        {
            w.Write(j.Id);
            WriteInt3(w, j.Position);
            w.Write((byte)j.Kind); w.Write(j.Block);
            w.Write(j.Progress);
        }
        w.Write(map.Jobs.NextId);

        var colony = map.Colony;
        w.Write(colony.Tick);
        WriteInt3(w, colony.Home);
        w.Write(colony.NextColonistId);
        w.Write(colony.Colonists.Count);
        foreach (var c in colony.Colonists)
        {
            w.Write(c.Id); w.Write(c.Name); w.Write(c.ShirtColor);
            WriteInt3(w, c.Cell); // mid-move, the cell it started from
            w.Write((byte)c.Skills.Length);
            foreach (var sk in c.Skills) { w.Write(sk.Level); w.Write(sk.Xp); }
            w.Write((byte)c.Priorities.Length);
            foreach (int p in c.Priorities) w.Write((byte)p);
        }
        w.Write(colony.Bookmarks.Count);
        foreach (var b in colony.Bookmarks) { w.Write(b.Name); w.Write(b.Level); }
        w.Flush();
    }

    static void WriteInt3(BinaryWriter w, Int3 p) { w.Write(p.X); w.Write(p.Y); w.Write(p.Z); }
    static Int3 ReadInt3(BinaryReader r) => new(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());

    /// <summary>Loads a save. <paramref name="aliases"/> maps block names found in the file to current ones (default <see cref="BlockAliases.Default"/>).</summary>
    public static ColonyMap Load(Stream stream, IReadOnlyDictionary<string, string>? aliases = null)
    {
        Span<byte> head = stackalloc byte[8];
        try { stream.ReadExactly(head); }
        catch (EndOfStreamException e) { throw new InvalidDataException("Save file is too short to be valid.", e); }
        if (!head[..4].SequenceEqual(Magic)) throw new InvalidDataException("Not a ColonySim save file (bad magic).");
        int version = BitConverter.ToInt32(head[4..]);
        if (version > FormatVersion)
            throw new InvalidDataException($"Save file version {version} is newer than the supported version {FormatVersion}.");
        if (version == 1)
            throw new InvalidDataException("This save was made by an older version of ColonySim (format 1) and can't be loaded.");
        if (version < 1) throw new InvalidDataException($"Invalid save file version {version}.");

        try
        {
            using var brotli = new BrotliStream(stream, CompressionMode.Decompress, leaveOpen: true);
            using var r = new BinaryReader(brotli, Encoding.UTF8, leaveOpen: true);
            return ReadBody(r, brotli, aliases ?? BlockAliases.Default);
        }
        catch (InvalidDataException) { throw; }
        catch (Exception e)
        {
            throw new InvalidDataException("Save file is truncated or corrupt: " + e.Message, e);
        }
    }

    static ColonyMap ReadBody(BinaryReader r, Stream raw, IReadOnlyDictionary<string, string> aliases)
    {
        int seed = r.ReadInt32(), sizeX = r.ReadInt32(), sizeZ = r.ReadInt32();
        if (sizeX <= 0 || sizeZ <= 0 || sizeX > MaxDimension || sizeZ > MaxDimension || sizeX % 32 != 0 || sizeZ % 32 != 0)
            throw new InvalidDataException($"Invalid map size {sizeX}x{sizeZ}.");
        var terrain = new TerrainParams
        {
            WaterLevel = r.ReadInt32(), BaseHeight = r.ReadInt32(), HillAmplitude = r.ReadSingle(), HillFrequency = r.ReadSingle(),
            MountainCoverage = r.ReadSingle(), MountainMaxHeight = r.ReadInt32(), HasRiver = r.ReadBoolean(), RiverWidth = r.ReadInt32(),
            DeepStoneLevel = r.ReadInt32(), MountainRockLine = r.ReadInt32(), CaveRoof = r.ReadInt32(),
        };
        var settings = new MapSettings(seed, sizeX, sizeZ, terrain);

        int paletteCount = r.ReadInt32();
        if (paletteCount < 1 || paletteCount > ushort.MaxValue + 1) throw new InvalidDataException($"Invalid palette size {paletteCount}.");
        var palette = new ushort[paletteCount];
        var names = new string[paletteCount];
        bool identity = true;
        for (int i = 0; i < paletteCount; i++)
        {
            string name = names[i] = r.ReadString();
            if (aliases.TryGetValue(name, out var current)) name = current;
            // A name this build doesn't know is tolerated until something uses it.
            if (!Blocks.Registry.TryGetByName(name, out palette[i])) palette[i] = Unknown;
            identity &= palette[i] == i;
        }
        // The block for a palette index; naming the block when the build doesn't have it.
        ushort Block(int idx)
        {
            if (idx >= paletteCount) throw new InvalidDataException($"Block palette index {idx} out of range.");
            if (palette[idx] == Unknown) throw new InvalidDataException($"Save file uses unknown block '{names[idx]}'.");
            return palette[idx];
        }
        if (palette[0] != Blocks.Air) throw new InvalidDataException("Save file palette does not start with air.");

        var world = new VoxelWorld(sizeX, sizeZ);
        for (int z = 0; z < sizeZ; z++)
            for (int x = 0; x < sizeX; x++)
            {
                int h = r.ReadInt16();
                if ((uint)h >= VoxelWorld.Height) throw new InvalidDataException($"Invalid surface height {h}.");
                world.SetSurfaceHeight(x, z, h);
            }

        for (int cy = 0; cy < world.ChunksY; cy++)
            for (int cz = 0; cz < world.ChunksZ; cz++)
                for (int cx = 0; cx < world.ChunksX; cx++)
                {
                    var c = world.ChunkByCoords(cx, cy, cz);
                    if (r.ReadBoolean())
                    {
                        ushort id = r.ReadUInt16();
                        ushort block = Block(id);
                        if (block != Blocks.Air) // uniform non-air chunks are not representable; expand
                        {
                            var arr = new ushort[Chunk.Volume];
                            Array.Fill(arr, block);
                            c.Blocks = arr;
                        }
                    }
                    else
                    {
                        var arr = new ushort[Chunk.Volume];
                        raw.ReadExactly(MemoryMarshal.AsBytes(arr.AsSpan()));
                        for (int i = 0; i < arr.Length; i++)
                        {
                            ushort id = arr[i];
                            if (identity && id < paletteCount) continue;
                            arr[i] = Block(id);
                        }
                        c.Blocks = arr;
                    }

                    if (!r.ReadBoolean())
                    {
                        var disc = new ulong[Chunk.Volume / 64];
                        raw.ReadExactly(MemoryMarshal.AsBytes(disc.AsSpan()));
                        c.Discovery = disc;
                    }
                }

        var site = new StartSite(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());

        var jobs = new JobQueue();
        int jobCount = r.ReadInt32();
        if (jobCount < 0) throw new InvalidDataException($"Invalid job count {jobCount}.");
        for (int i = 0; i < jobCount; i++)
        {
            long id = r.ReadInt64();
            var pos = ReadInt3(r);
            byte kind = r.ReadByte();
            ushort block = Block(r.ReadUInt16());
            float progress = r.ReadSingle();
            if (id < 1 || jobs.TryGetById(id, out _)) throw new InvalidDataException($"Invalid or repeated job id {id}.");
            if (!Enum.IsDefined((JobKind)kind)) throw new InvalidDataException($"Invalid job kind {kind}.");
            if (!world.InBounds(pos.X, pos.Y, pos.Z) || pos.Y < 1) throw new InvalidDataException($"Job position {pos} is outside the map.");
            if (!float.IsFinite(progress) || progress < 0) throw new InvalidDataException($"Invalid job progress {progress}.");
            jobs.Restore(new Job(id, new JobSpec(pos, (JobKind)kind, block)) { Progress = progress });
        }
        long nextJobId = r.ReadInt64();
        if (nextJobId > jobs.NextId) jobs.NextId = nextJobId;

        var colony = ReadColony(r, world);
        var map = new ColonyMap(settings, world, jobs, site, colony);
        PlaceColonists(map);
        return map;
    }

    static Colony ReadColony(BinaryReader r, VoxelWorld world)
    {
        long tick = r.ReadInt64();
        var home = ReadInt3(r);
        if (tick < 0) throw new InvalidDataException($"Invalid colony tick {tick}.");
        if (!world.InBounds(home.X, home.Y, home.Z)) throw new InvalidDataException($"Colony home {home} is outside the map.");
        var colony = new Colony(home) { Tick = tick };
        int nextId = r.ReadInt32();
        int count = r.ReadInt32();
        if (nextId < 1 || count < 0) throw new InvalidDataException("Invalid colonist counts.");
        var ids = new HashSet<int>();
        int maxId = 0;
        int skillTypes = Enum.GetValues<SkillType>().Length;
        for (int i = 0; i < count; i++)
        {
            int id = r.ReadInt32();
            string name = r.ReadString();
            int shirt = r.ReadInt32();
            var cell = ReadInt3(r);
            if (id < 1 || !ids.Add(id)) throw new InvalidDataException($"Invalid or repeated colonist id {id}.");
            if ((uint)shirt > 7) throw new InvalidDataException($"Invalid shirt color {shirt}.");
            if (!world.InBounds(cell.X, cell.Y, cell.Z)) throw new InvalidDataException($"Colonist position {cell} is outside the map.");
            maxId = Math.Max(maxId, id);
            var c = new Colonist(id, name) { ShirtColor = shirt, Cell = cell };

            int savedSkills = r.ReadByte();
            if (savedSkills > skillTypes) throw new InvalidDataException($"Save has {savedSkills} skills; this build has {skillTypes}.");
            for (int k = 0; k < savedSkills; k++)
            {
                int level = r.ReadInt32();
                float xp = r.ReadSingle();
                if (level < 0 || level > Skills.MaxLevel || !float.IsFinite(xp) || xp < 0) throw new InvalidDataException($"Invalid skill {level}/{xp}.");
                c.Skills[k].Level = level;
                c.Skills[k].Xp = xp;
            }

            int savedPriorities = r.ReadByte();
            if (savedPriorities > c.Priorities.Length)
                throw new InvalidDataException($"Save has {savedPriorities} work priorities; this build has {c.Priorities.Length}.");
            Array.Fill(c.Priorities, 3); // work types a save predates start at the default
            for (int k = 0; k < savedPriorities; k++)
            {
                byte p = r.ReadByte();
                if (p > 4) throw new InvalidDataException($"Invalid work priority {p}.");
                c.Priorities[k] = p;
            }
            colony.Colonists.Add(c);
        }
        colony.NextColonistId = Math.Max(nextId, maxId + 1);

        int bookmarks = r.ReadInt32();
        if (bookmarks < 0) throw new InvalidDataException($"Invalid bookmark count {bookmarks}.");
        for (int i = 0; i < bookmarks; i++)
        {
            string name = r.ReadString();
            int level = r.ReadInt32();
            if ((uint)level >= VoxelWorld.Height) throw new InvalidDataException($"Invalid bookmark level {level}.");
            colony.Bookmarks.Add(new ViewBookmark(name, level));
        }
        return colony;
    }

    /// <summary>A colonist whose saved cell isn't standable (the ground changed since the save) is moved to the nearest standable cell, else home.</summary>
    static void PlaceColonists(ColonyMap map)
    {
        if (map.Colony.Colonists.Count == 0) return;
        var rules = new NavRules(map.View); // the map's navigation is built later, by the simulation
        foreach (var c in map.Colony.Colonists)
            if (!rules.IsStandable(c.Cell))
                c.Cell = rules.FindStandableNear(c.Cell, 8, out var near) ? near : map.Colony.Home;
    }

    const ushort Unknown = ushort.MaxValue;
}
