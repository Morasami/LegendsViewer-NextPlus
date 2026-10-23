using System.Diagnostics;
using LegendsViewer.Backend.Legends;
using LegendsViewer.Backend.Legends.Maps;
using LegendsViewer.Backend.Settings;
using LegendsViewer.Backend.WorldLibrary;

namespace LegendsViewer.Backend.WorldCache;

public record PreparedWorld(World World, WorldMapImageGenerator Map, bool CacheHit, bool Offline, double Milliseconds, string? Warning);

public sealed class WorldLoadService(WorldCacheService cache, GameInstallationService graphics, WorldSessionStore sessions,
    ILogger<WorldLoadService> logger)
{
    public static readonly SemaphoreSlim LoadGate = new(1, 1);
    public Func<World, string, string?, string?, string?, Task> Parse { get; set; } = (world, main, plus, history, populations)
        => world.ParseAsync(main, plus, history, populations, null);

    public async Task<PreparedWorld> PrepareAsync(string main, bool rebuild = false)
    {
        var timer = Stopwatch.StartNew(); var hit = false; var offline = false; World? world = null; string? warning = null;
        try
        {
            if (!rebuild)
            {
                try
                {
                    var restored = cache.Restore(main);
                    if (restored != null) { world = restored.World; offline = restored.Offline; hit = true; }
                }
                catch (Exception ex) when (WorldCacheService.Recoverable(ex))
                { warning = "Cache could not be restored; parsed the source instead. " + ex.Message; logger.LogWarning(ex, "Cache restore failed"); }
            }
            if (world == null)
            {
                if (!File.Exists(main)) throw new FileNotFoundException("Source unavailable and no valid matching cache can be opened.", main);
                cache.Phase(main, "Parsing", rebuild ? "Rebuilding from source exports" : "Parsing source exports");
                var before = cache.Sources(main, true);
                world = new World();
                string? Companion(string kind) { var p = Path.Combine(Path.GetDirectoryName(main)!, ExportName.Parse(Path.GetFileName(main))!.Prefix + ExportName.Suffixes[kind]); return File.Exists(p) ? p : null; }
                await Parse(world, main, Companion("extended"), Companion("history"), Companion("populations"));
                WorldGraphValidation.Validate(world);
                try { cache.Publish(main, world, before); }
                catch (Exception ex) when (WorldCacheService.Recoverable(ex) && ex is not SourceChangedException)
                { warning = "World loaded, but its cache could not be saved. " + ex.Message; logger.LogWarning(ex, "Cache creation failed"); }
            }
            cache.Phase(main, "Map", "Initializing map and installed graphics");
            var map = new WorldMapImageGenerator(world, graphics.FindFile);
            try
            {
                await map.LoadExportedWorldMapAsync(main);
                _ = map.GenerateMapByteArray(WorldMapImageGenerator.DefaultTileSizeMin);
                if (offline) warning = "Opened cached history read-only. Original source is unavailable; its current contents cannot be verified.";
                world.Log.AppendLine($"Parsed-world cache: {(hit ? "HIT (XML parser not invoked)" : "MISS (XML parser executed)")}; full load {timer.Elapsed.TotalMilliseconds:F0} ms.");
                logger.LogInformation("World load {Source}: CacheHit={CacheHit}, ParserExecuted={ParserExecuted}, FullLoadMs={Ms}", main, hit, !hit, timer.Elapsed.TotalMilliseconds);
                return new(world, map, hit, offline, timer.Elapsed.TotalMilliseconds, warning);
            }
            catch { map.Clear(); throw; }
        }
        finally { cache.EndPhase(main); }
    }
    public void Publish(PreparedWorld prepared) => sessions.Publish(prepared.World, prepared.Map);
}
