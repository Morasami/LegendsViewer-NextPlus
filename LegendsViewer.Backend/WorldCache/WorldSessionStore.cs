using LegendsViewer.Backend.Legends;
using LegendsViewer.Backend.Legends.Interfaces;
using LegendsViewer.Backend.Legends.Maps;
using LegendsViewer.Backend.Settings;

namespace LegendsViewer.Backend.WorldCache;

/// <summary>Publishes complete generations; requests lease one world/map pair until response serialization ends.</summary>
public sealed class WorldSessionStore
{
    private readonly object gate = new();
    private Generation active;
    public WorldSessionStore(GameInstallationService graphics)
    {
        var world = new World(); active = new(world, new WorldMapImageGenerator(world, graphics.FindFile));
    }
    public WorldSessionLease Lease()
    {
        lock (gate) { var generation = active; generation.References++; return new(generation.World, generation.Map, () => Release(generation)); }
    }
    private void Release(Generation generation)
    {
        lock (gate) { if (--generation.References == 0) generation.Map.Clear(); }
    }
    public void Publish(World world, IWorldMapImageGenerator map)
    {
        lock (gate) { var old = active; active = new(world, map); Release(old); }
    }
    private sealed class Generation(World world, IWorldMapImageGenerator map)
    {
        public World World { get; } = world;
        public IWorldMapImageGenerator Map { get; } = map;
        public int References = 1;
    }
}

public sealed class WorldSessionLease(World world, IWorldMapImageGenerator map, Action release) : IDisposable
{
    public World World { get; } = world;
    public IWorldMapImageGenerator Map { get; } = map;
    private Action? release = release;
    public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();
}
