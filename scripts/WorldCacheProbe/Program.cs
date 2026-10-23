using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using LegendsViewer.Backend.Legends;
using LegendsViewer.Backend.Legends.Maps;
using LegendsViewer.Backend.WorldCache;
using WorldCacheProbe;
using LegendsViewer.Backend.Settings;
using Microsoft.Extensions.Logging.Abstractions;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var mode = args[0]; var source = Path.GetFullPath(args[1]); var cache = Path.GetFullPath(args[2]);
if (mode is "baseline" or "cold" or "warm" or "rebuild")
{
    var full = Stopwatch.StartNew();
    var graphics = new GameInstallationService(new ApplicationSettingsService(), new InstallationLocations());
    var store = new WorldSessionStore(graphics);
    var caching = new WorldCacheService(cache);
    double parserMs = 0; var parserCalls = 0;
    PreparedWorld prepared;
    if (mode == "baseline")
    {
        var baselineWorld = new World(); var parseTimer = Stopwatch.StartNew();
        string? FileFor(string suffix) { var p = source[..^"-legends.xml".Length] + suffix; return File.Exists(p) ? p : null; }
        await baselineWorld.ParseAsync(source, FileFor("-legends_plus.xml"), FileFor("-world_history.txt"), FileFor("-world_sites_and_pops.txt"), null);
        parserMs = parseTimer.Elapsed.TotalMilliseconds; parserCalls++;
        var baselineMap = new WorldMapImageGenerator(baselineWorld, graphics.FindFile);
        await baselineMap.LoadExportedWorldMapAsync(source); _ = baselineMap.GenerateMapByteArray(WorldMapImageGenerator.DefaultTileSizeMin);
        prepared = new(baselineWorld, baselineMap, false, false, full.Elapsed.TotalMilliseconds, null);
    }
    else
    {
        var loading = new WorldLoadService(caching, graphics, store, NullLogger<WorldLoadService>.Instance);
        var parser = loading.Parse;
        loading.Parse = async (w, m, p, h, s) =>
        {
            parserCalls++;
            if (mode == "warm") throw new Exception("PARSER EXECUTED ON WARM LOAD");
            var parseTimer = Stopwatch.StartNew(); await parser(w, m, p, h, s); parserMs = parseTimer.Elapsed.TotalMilliseconds;
        };
        prepared = await loading.PrepareAsync(source, mode == "rebuild");
        if (mode == "warm" && !prepared.CacheHit) throw new Exception("Expected cache hit");
        loading.Publish(prepared);
    }
    var elapsed = full.Elapsed.TotalMilliseconds;
    Console.WriteLine(JsonSerializer.Serialize(new { mode, source, fullMs = elapsed, parserMs, parserCalls, prepared.CacheHit,
        prepared.Warning, cacheBytes = File.Exists(caching.CachePath(source)) ? new FileInfo(caching.CachePath(source)).Length : 0,
        figures = prepared.World.HistoricalFigures.Count, events = prepared.World.Events.Count,
        managedBytes = GC.GetTotalMemory(false), workingSet = Environment.WorkingSet, peakWorkingSet = Process.GetCurrentProcess().PeakWorkingSet64 }));
    prepared.Map.Clear(); return;
}
if (mode == "diagnose")
{
    World Read() { using var f = File.OpenRead(cache); using var g = new GZipStream(f, CompressionMode.Decompress); return new WorldGraphCodec().Read(g); }
    var a = Read();
    using (var log = File.CreateText(cache + ".behavior1")) Console.WriteLine("First: " + WorldEvidence.Behavior(a, log.WriteLine));
    using (var log = File.CreateText(cache + ".behavior2")) Console.WriteLine("Second: " + WorldEvidence.Behavior(a, log.WriteLine));
    return;
}
string? Companion(string suffix) { var p = source[..^"-legends.xml".Length] + suffix; return File.Exists(p) ? p : null; }
var timer = Stopwatch.StartNew();
var world = new World();
await world.ParseAsync(source, Companion("-legends_plus.xml"), Companion("-world_history.txt"), Companion("-world_sites_and_pops.txt"), null);
var parseMs = timer.Elapsed.TotalMilliseconds;
Console.Error.WriteLine($"Parsed in {parseMs:F0} ms; {world.HistoricalFigures.Count} figures, {world.Events.Count} events");
timer.Restart();
using (var file = File.Create(cache)) using (var gzip = new GZipStream(file, CompressionLevel.Fastest)) new WorldGraphCodec().Write(gzip, world);
var writeMs = timer.Elapsed.TotalMilliseconds;
Console.Error.WriteLine($"Graph written in {writeMs:F0} ms");
timer.Restart();
World restored;
using (var file = File.OpenRead(cache)) using (var gzip = new GZipStream(file, CompressionMode.Decompress)) restored = new WorldGraphCodec().Read(gzip);
var readMs = timer.Elapsed.TotalMilliseconds;
Console.Error.WriteLine($"Graph restored in {readMs:F0} ms");
var compared = WorldEvidence.CompareGraph(world, restored);
string expected;
using (var log = File.CreateText(cache + ".fresh")) expected = WorldEvidence.Behavior(world, log.WriteLine);
Console.Error.WriteLine("Fresh behavior captured");
string actual;
using (var log = File.CreateText(cache + ".restored")) actual = WorldEvidence.Behavior(restored, log.WriteLine);
if (expected != actual)
{
    Console.Error.WriteLine($"MISMATCH: expected={expected}, actual={actual}; parseMs={parseMs:F0}, writeMs={writeMs:F0}, readMs={readMs:F0}, compared={compared}");
    var first = File.ReadLines(cache + ".fresh").Zip(File.ReadLines(cache + ".restored"))
        .Select((pair, index) => new { line = index + 1, fresh = pair.First, restored = pair.Second }).First(p => p.fresh != p.restored);
    File.WriteAllText(cache + ".mismatch.json", JsonSerializer.Serialize(first, new JsonSerializerOptions { WriteIndented = true }));
    throw new Exception("Behavior mismatch; see " + cache + ".mismatch.json");
}
var map = new WorldMapImageGenerator(restored);
await map.LoadExportedWorldMapAsync(source);
var mapBytes = map.GenerateMapByteArray();
Console.WriteLine(JsonSerializer.Serialize(new { mode, source, parseMs, writeMs, readMs, compared, behavior = expected,
    cacheBytes = new FileInfo(cache).Length, figures = world.HistoricalFigures.Count, events = world.Events.Count,
    wars = world.Wars.Count, battles = world.Battles.Count, mapBytes = mapBytes?.Length,
    managedBytes = GC.GetTotalMemory(false), workingSet = Environment.WorkingSet,
    peakWorkingSet = Process.GetCurrentProcess().PeakWorkingSet64 }));
map.Clear();
