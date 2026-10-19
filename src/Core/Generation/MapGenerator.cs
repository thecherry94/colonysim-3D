namespace ColonySim.Core;

public readonly record struct GenerationProgress(string Stage, float Fraction);

/// <summary>Full map generation: height field, ground and water, caves, ores, trees, slopes, the initial discovery flood, then the colonists' arrival.</summary>
public static class MapGenerator
{
    public static ColonyMap Generate(MapSettings s, IProgress<GenerationProgress>? progress = null, CancellationToken ct = default)
    {
        progress?.Report(new("Shaping terrain", 0f));
        var heights = HeightField.Generate(s).Heights;
        ct.ThrowIfCancellationRequested();

        progress?.Report(new("Choosing a start site", 0.1f));
        var (siteX, siteZ) = StartSiteFinder.Find(heights, s);
        var site = new StartSite(siteX, siteZ, StartSiteFinder.Flatten(heights, s, siteX, siteZ) + 1);

        var world = new VoxelWorld(s.SizeX, s.SizeZ);
        var layers = new TerrainLayers(s);
        int columns = world.ChunksX * world.ChunksZ;
        var parallel = new ParallelOptions { CancellationToken = ct };

        progress?.Report(new("Filling terrain", 0.2f));
        Parallel.For(0, columns, parallel, i => ColumnFiller.FillChunkColumn(world, layers, s, heights, i % world.ChunksX, i / world.ChunksX));

        // A separate pass: carving reads neighboring columns' surface heights, which must all be filled first.
        progress?.Report(new("Carving caves", 0.4f));
        var carver = new CaveCarver(s);
        Parallel.For(0, columns, parallel, i => carver.CarveChunkColumn(world, i % world.ChunksX, i / world.ChunksX));

        progress?.Report(new("Placing ores", 0.6f));
        OrePlacer.Place(world, s);
        ct.ThrowIfCancellationRequested();

        progress?.Report(new("Growing trees", 0.65f));
        SurfaceDecorator.Decorate(world, s, site);
        ct.ThrowIfCancellationRequested();

        progress?.Report(new("Smoothing slopes", 0.68f));
        SlopePlacer.Place(world, s);
        ct.ThrowIfCancellationRequested();

        progress?.Report(new("Exploring", 0.7f));
        Discovery.InitialFlood(world);

        progress?.Report(new("Arriving", 0.9f));
        var map = new ColonyMap(s, world, site: site);
        ColonistFactory.SpawnStarting(map);

        progress?.Report(new("Done", 1f));
        return map;
    }
}
