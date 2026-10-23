using LegendsViewer.Backend.Legends;
using LegendsViewer.Backend.Legends.Interfaces;
using LegendsViewer.Backend.Legends.Maps;
using LegendsViewer.Backend.Search;
using LegendsViewer.Backend.Settings;

namespace LegendsViewer.Backend.WorldCache;

/// <summary>Publishes complete generations; requests lease one world/map/index until response serialization ends.</summary>
public sealed class WorldSessionStore
{
    private readonly object gate = new();
    private readonly object publishGate = new();
    private readonly ILogger<WorldSessionStore>? logger;
    private Generation active;
    private long generationId;

    public WorldSessionStore(GameInstallationService graphics, ILogger<WorldSessionStore>? logger = null)
    {
        this.logger = logger;
        var world = new World();
        active = CreateGeneration(world, new WorldMapImageGenerator(world, graphics.FindFile));
    }

    public WorldSessionLease Lease()
    {
        lock (gate)
        {
            var generation = active;
            generation.References++;
            return new(generation.World, generation.Map, generation.SearchIndex, generation.Id,
                () => Release(generation));
        }
    }

    private void Release(Generation generation)
    {
        lock (gate) { if (--generation.References == 0) generation.Map.Clear(); }
    }

    public void Publish(World world, IWorldMapImageGenerator map)
    {
        // Index construction is serialized, but it happens outside the publication lock so
        // concurrent search requests continue to lease the previous complete generation.
        lock (publishGate)
        {
            HistoricalSearchIndex index = HistoricalSearchIndex.Build(world);
            long publishedId;
            lock (gate)
            {
                var old = active;
                active = CreateGeneration(world, map, index);
                publishedId = active.Id;
                Release(old);
            }
            logger?.LogInformation(
                "Published search index generation {Generation}: {Documents} records, {BuildMs} ms, ~{EstimatedMiB} MiB, {PreviewFailures} event preview failure(s)",
                publishedId, index.DocumentCount, index.BuildMilliseconds, index.EstimatedBytes / (1024d * 1024d), index.EventPreviewFailures);
        }
    }

    private Generation CreateGeneration(World world, IWorldMapImageGenerator map, HistoricalSearchIndex? index = null)
    {
        index ??= HistoricalSearchIndex.Build(world);
        return new Generation(++generationId, world, map, index);
    }

    private sealed class Generation(long id, World world, IWorldMapImageGenerator map, HistoricalSearchIndex searchIndex)
    {
        public long Id { get; } = id;
        public World World { get; } = world;
        public IWorldMapImageGenerator Map { get; } = map;
        public HistoricalSearchIndex SearchIndex { get; } = searchIndex;
        public int References = 1;
    }
}

public sealed class WorldSessionLease(World world, IWorldMapImageGenerator map, HistoricalSearchIndex searchIndex,
    long generationId, Action release) : IDisposable
{
    public World World { get; } = world;
    public IWorldMapImageGenerator Map { get; } = map;
    public HistoricalSearchIndex SearchIndex { get; } = searchIndex;
    public long GenerationId { get; } = generationId;
    private Action? release = release;
    public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();
}
