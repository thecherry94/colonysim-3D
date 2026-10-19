using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace ColonySim.Core;

/// <summary>
/// Binary save format: "CSIM", int32 version, then a Brotli-compressed body (little-endian):
/// settings, block-name palette, surface heights, per-chunk blocks + discovery, jobs.
/// </summary>
public static class MapSerializer
{
    public const int FormatVersion = 1;
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

        w.Write(map.Jobs.Count);
        foreach (var j in map.Jobs.All)
        {
            w.Write(j.Position.X); w.Write(j.Position.Y); w.Write(j.Position.Z);
            w.Write((byte)j.Kind); w.Write(j.Block);
        }
        w.Flush();
    }

    public static ColonyMap Load(Stream stream)
    {
        Span<byte> head = stackalloc byte[8];
        try { stream.ReadExactly(head); }
        catch (EndOfStreamException e) { throw new InvalidDataException("Save file is too short to be valid.", e); }
        if (!head[..4].SequenceEqual(Magic)) throw new InvalidDataException("Not a ColonySim save file (bad magic).");
        int version = BitConverter.ToInt32(head[4..]);
        if (version > FormatVersion)
            throw new InvalidDataException($"Save file version {version} is newer than the supported version {FormatVersion}.");
        if (version < 1) throw new InvalidDataException($"Invalid save file version {version}.");

        try
        {
            using var brotli = new BrotliStream(stream, CompressionMode.Decompress, leaveOpen: true);
            using var r = new BinaryReader(brotli, Encoding.UTF8, leaveOpen: true);
            return ReadBody(r, brotli);
        }
        catch (InvalidDataException) { throw; }
        catch (Exception e)
        {
            throw new InvalidDataException("Save file is truncated or corrupt: " + e.Message, e);
        }
    }

    static ColonyMap ReadBody(BinaryReader r, Stream raw)
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
        bool identity = true;
        for (int i = 0; i < paletteCount; i++)
        {
            string name = r.ReadString();
            if (!Blocks.Registry.TryGetByName(name, out palette[i]))
                throw new InvalidDataException($"Save file uses unknown block '{name}'.");
            identity &= palette[i] == i;
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
                        if (id >= paletteCount) throw new InvalidDataException($"Block palette index {id} out of range.");
                        if (palette[id] != Blocks.Air) // uniform non-air chunks are not representable; expand
                        {
                            var arr = new ushort[Chunk.Volume];
                            Array.Fill(arr, palette[id]);
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
                            if (id >= paletteCount) throw new InvalidDataException($"Block palette index {id} out of range.");
                            if (!identity) arr[i] = palette[id];
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

        var jobs = new JobQueue();
        int jobCount = r.ReadInt32();
        if (jobCount < 0) throw new InvalidDataException($"Invalid job count {jobCount}.");
        for (int i = 0; i < jobCount; i++)
        {
            var pos = new Int3(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
            byte kind = r.ReadByte();
            ushort idx = r.ReadUInt16();
            if (!Enum.IsDefined((JobKind)kind)) throw new InvalidDataException($"Invalid job kind {kind}.");
            if (idx >= paletteCount) throw new InvalidDataException($"Block palette index {idx} out of range.");
            if (!world.InBounds(pos.X, pos.Y, pos.Z) || pos.Y < 1) throw new InvalidDataException($"Job position {pos} is outside the map.");
            jobs.Add(new JobSpec(pos, (JobKind)kind, palette[idx]));
        }
        return new ColonyMap(settings, world, jobs);
    }
}
