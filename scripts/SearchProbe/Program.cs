using System.Diagnostics;
using System.Text;
using System.Text.Json;
using LegendsViewer.Backend.Search;
using LegendsViewer.Backend.WorldCache;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: SearchProbe <main-legends.xml> <cache-directory> [query ...]");
    return 2;
}

string source = Path.GetFullPath(args[0]);
string cacheDirectory = Path.GetFullPath(args[1]);
string[] queries = args.Length > 2
    ? args[2..]
    : ["Urist", "artifact created", "war", "a", "zzzznotfound"];

WorldCacheService cache = new(cacheDirectory);
Stopwatch restoreTimer = Stopwatch.StartNew();
CacheRestore restored = cache.Restore(source)
    ?? throw new InvalidOperationException("No valid parsed-world cache is available for " + source);
restoreTimer.Stop();
var world = restored.World;

GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();
long managedBefore = GC.GetTotalMemory(true);
long workingSetBefore = Environment.WorkingSet;

Stopwatch buildTimer = Stopwatch.StartNew();
var index = HistoricalSearchIndex.Build(world);
buildTimer.Stop();

GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();
long managedAfter = GC.GetTotalMemory(true);
long workingSetAfter = Environment.WorkingSet;

var measurements = new List<object>();
foreach (string query in queries)
{
    var timings = new List<double>();
    HistoricalSearchResultPageInfo last = default;
    for (int repetition = 0; repetition < 7; repetition++)
    {
        Stopwatch searchTimer = Stopwatch.StartNew();
        var response = index.Search(query, null, null, null, 1, 25);
        searchTimer.Stop();
        if (repetition > 0)
        {
            timings.Add(searchTimer.Elapsed.TotalMilliseconds);
        }
        last = new(response.TotalCount, response.Items.Count, response.Truncated);
    }
    timings.Sort();
    measurements.Add(new
    {
        query,
        totalCount = last.TotalCount,
        returned = last.Returned,
        truncated = last.Truncated,
        medianMs = Math.Round(timings[timings.Count / 2], 3),
        minMs = Math.Round(timings[0], 3),
        maxMs = Math.Round(timings[^1], 3),
    });
}

Console.WriteLine(JsonSerializer.Serialize(new
{
    source,
    cacheHit = true,
    offline = restored.Offline,
    restoreMs = Math.Round(restoreTimer.Elapsed.TotalMilliseconds, 1),
    indexBuildMs = Math.Round(buildTimer.Elapsed.TotalMilliseconds, 1),
    reportedIndexBuildMs = index.BuildMilliseconds,
    documents = index.DocumentCount,
    estimatedIndexBytes = index.EstimatedBytes,
    managedHeapDeltaBytes = managedAfter - managedBefore,
    workingSetBefore,
    workingSetAfter,
    workingSetDelta = workingSetAfter - workingSetBefore,
    peakWorkingSet = Process.GetCurrentProcess().PeakWorkingSet64,
    figures = world.HistoricalFigures.Count,
    events = world.Events.Count,
    previewFailures = index.EventPreviewFailures,
    queries = measurements,
}, new JsonSerializerOptions { WriteIndented = true }));

return 0;

internal readonly record struct HistoricalSearchResultPageInfo(int TotalCount, int Returned, bool Truncated);
