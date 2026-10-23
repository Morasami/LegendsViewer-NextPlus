using System.Runtime.CompilerServices;
using System.Text;
using LegendsViewer.Backend.Contracts;
using LegendsViewer.Backend.Controllers;
using LegendsViewer.Backend.Legends;
using LegendsViewer.Backend.Legends.EventCollections;
using LegendsViewer.Backend.Legends.Events;
using LegendsViewer.Backend.Legends.Maps;
using LegendsViewer.Backend.Legends.Parser;
using LegendsViewer.Backend.Legends.WorldObjects;
using LegendsViewer.Backend.Search;
using LegendsViewer.Backend.Settings;
using LegendsViewer.Backend.WorldCache;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace LegendsViewer.Backend.Tests;

[TestClass]
public class SearchTests
{
    private sealed class NoLocations : InstallationLocations
    {
        public override string? EnvironmentOverride => null;
        public override IEnumerable<string> SteamRoots() => [];
        public override IEnumerable<string> StandaloneRoots() => [];
    }

    [ClassInitialize]
    public static void Initialize(TestContext _)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static string Fixture(string suffix) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "Xah_Atho-00005-01-01" + suffix);

    private static async Task<World> ParseWorldAsync(bool extended = true)
    {
        var world = new World();
        await world.ParseAsync(Fixture("-legends.xml"), extended ? Fixture("-legends_plus.xml") : null, null, null, null);
        return world;
    }

    private static HistoricalSearchResponse Search(HistoricalSearchIndex index, string query,
        HistoricalSearchCategory[]? categories = null, int? fromYear = null, int? toYear = null,
        int page = 1, int pageSize = 25)
    {
        var filter = categories == null ? null : new HashSet<HistoricalSearchCategory>(categories);
        return index.Search(query, filter, fromYear, toYear, page, pageSize);
    }

    private static (WorldSessionStore Sessions, GameInstallationService Graphics, WorldSessionLease Lease) CreateSessions()
    {
        var root = Path.Combine(Path.GetTempPath(), "nextplus-search-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var graphics = new GameInstallationService(new ApplicationSettingsService(Path.Combine(root, "settings.json")),
            new NoLocations());
        var sessions = new WorldSessionStore(graphics);
        return (sessions, graphics, sessions.Lease());
    }

    [TestMethod]
    public async Task ExactPrefixPartialAndCaseInsensitiveMatching()
    {
        var world = await ParseWorldAsync();
        var figure = world.HistoricalFigures[3];
        figure.Name = "Zzsearchalpa Thodor";
        var index = HistoricalSearchIndex.Build(world);

        var exact = Search(index, "Zzsearchalpa Thodor", [HistoricalSearchCategory.Figure]);
        Assert.AreEqual("exact name", exact.Items[0].MatchKind);
        Assert.AreEqual($"figure:{figure.Id}", exact.Items[0].Key);

        var prefix = Search(index, "ZZSEARCH", [HistoricalSearchCategory.Figure]);
        CollectionAssert.Contains(prefix.Items.Select(item => item.Key).ToList(), $"figure:{figure.Id}");

        var partial = Search(index, "searchalpa", [HistoricalSearchCategory.Figure]);
        Assert.IsTrue(partial.Items.Any(item => item.Key == $"figure:{figure.Id}"),
            $"Partial search total={partial.TotalCount}; keys={string.Join(",", partial.Items.Select(item => item.Key))}; figure={figure.Id}; name={figure.Name}");
        var partialResult = partial.Items.Single(item => item.Key == $"figure:{figure.Id}");
        Assert.AreEqual("partial name", partialResult.MatchKind);

        var upper = Search(index, "ZZSEARCHALPA THODOR", [HistoricalSearchCategory.Figure]);
        Assert.AreEqual("exact name", upper.Items[0].MatchKind);
    }

    [TestMethod]
    public async Task DuplicateNamesKeepDistinctIdsAndLinks()
    {
        var world = await ParseWorldAsync();
        var first = world.HistoricalFigures[0];
        var second = world.HistoricalFigures[1];
        first.Name = "Mirror Twin";
        second.Name = "Mirror Twin";
        var index = HistoricalSearchIndex.Build(world);

        var response = Search(index, "Mirror Twin", [HistoricalSearchCategory.Figure]);
        var matches = response.Items.Where(item => item.Name == "Mirror Twin").ToList();
        Assert.AreEqual(2, matches.Count);
        Assert.AreEqual(2, matches.Select(item => item.Key).Distinct().Count());
        CollectionAssert.AreEquivalent(new[] { $"/hf/{first.Id}", $"/hf/{second.Id}" }, matches.Select(item => item.Url).ToList());
        Assert.IsTrue(matches.All(item => item.Navigable));
    }

    [TestMethod]
    public async Task UnicodeNamesMatchWithAndWithoutDiacritics()
    {
        var world = await ParseWorldAsync();
        var figure = world.HistoricalFigures[2];
        figure.Name = "Éowyn 世界";
        var index = HistoricalSearchIndex.Build(world);

        var withoutDiacritic = Search(index, "eowyn", [HistoricalSearchCategory.Figure]);
        CollectionAssert.Contains(withoutDiacritic.Items.Select(item => item.Key).ToList(), $"figure:{figure.Id}");

        var unicode = Search(index, "世界", [HistoricalSearchCategory.Figure]);
        CollectionAssert.Contains(unicode.Items.Select(item => item.Key).ToList(), $"figure:{figure.Id}");
    }

    [TestMethod]
    public async Task CategoryYearFiltersAndUnavailableRecordsAreExplicit()
    {
        var world = await ParseWorldAsync(extended: false);
        var figure = world.HistoricalFigures[0];
        figure.Name = "Calendar Filter Figure";
        figure.BirthYear = 1234;
        var index = HistoricalSearchIndex.Build(world);

        var inRange = Search(index, "calendar filter", [HistoricalSearchCategory.Figure], 1234, 1234);
        Assert.AreEqual(1, inRange.TotalCount);
        Assert.AreEqual($"figure:{figure.Id}", inRange.Items[0].Key);

        var outsideRange = Search(index, "calendar filter", [HistoricalSearchCategory.Figure], 1235, 1300);
        Assert.AreEqual(0, outsideRange.TotalCount);

        var wrongCategory = Search(index, "calendar filter", [HistoricalSearchCategory.Event], 1200, 1300);
        Assert.AreEqual(0, wrongCategory.TotalCount);

        var eventResponse = Search(index, "artifact created", [HistoricalSearchCategory.Event], 1, 1);
        Assert.IsTrue(eventResponse.TotalCount >= 1);
        Assert.IsTrue(eventResponse.Items.All(item => item.Category == HistoricalSearchCategory.Event));
        Assert.IsTrue(eventResponse.Items.All(item => item.Year == 1));
        CollectionAssert.Contains(eventResponse.UnavailableCategories, "War");
        Assert.IsTrue(eventResponse.AvailableCategoryCounts.ContainsKey("Figure"));
    }

    [TestMethod]
    public async Task RankingAndPaginationAreDeterministic()
    {
        var world = await ParseWorldAsync();
        var exact = world.HistoricalFigures[0];
        var prefix = world.HistoricalFigures[1];
        var wordMatch = world.HistoricalFigures[2];
        exact.Name = "Pagination Alpha";
        prefix.Name = "Pagination Alpha Junior";
        wordMatch.Name = "Alpha of Pagination";
        var index = HistoricalSearchIndex.Build(world);

        var firstPage = Search(index, "pagination alpha", [HistoricalSearchCategory.Figure], pageSize: 1);
        var secondPage = Search(index, "pagination alpha", [HistoricalSearchCategory.Figure], page: 2, pageSize: 1);
        Assert.AreEqual(3, firstPage.TotalCount);
        Assert.AreEqual($"figure:{exact.Id}", firstPage.Items[0].Key);
        Assert.AreEqual("exact name", firstPage.Items[0].MatchKind);
        Assert.AreEqual($"figure:{prefix.Id}", secondPage.Items[0].Key);
        Assert.AreEqual("name prefix", secondPage.Items[0].MatchKind);
        Assert.AreNotEqual(firstPage.Items[0].Key, secondPage.Items[0].Key);

        var all = Search(index, "pagination alpha", [HistoricalSearchCategory.Figure], pageSize: 10);
        CollectionAssert.AreEqual(
            new[] { $"figure:{exact.Id}", $"figure:{prefix.Id}", $"figure:{wordMatch.Id}" },
            all.Items.Select(item => item.Key).ToList());
    }

    [TestMethod]
    public async Task ResultsExposeWorkingDetailLinksAndStableIdentifiers()
    {
        var world = await ParseWorldAsync();
        var index = HistoricalSearchIndex.Build(world);

        var site = world.Sites.First(item => item.Name.Contains("Blazehelpful", StringComparison.OrdinalIgnoreCase));
        var siteResponse = Search(index, site.Name, [HistoricalSearchCategory.Site]);
        Assert.AreEqual($"/site/{site.Id}", siteResponse.Items[0].Url);
        Assert.AreEqual($"site:{site.Id}", siteResponse.Items[0].Key);

        var region = world.Regions.First(item => item.Name.Contains("Grand Hill", StringComparison.OrdinalIgnoreCase));
        var regionResponse = Search(index, region.Name, [HistoricalSearchCategory.Place]);
        Assert.AreEqual($"/region/{region.Id}", regionResponse.Items[0].Url);

        var artifact = world.Artifacts[0];
        var artifactResponse = Search(index, artifact.Name, [HistoricalSearchCategory.Artifact]);
        Assert.AreEqual($"/artifact/{artifact.Id}", artifactResponse.Items[0].Url);

        var creature = world.CreatureInfos.First(item => item != LegendsViewer.Backend.Legends.Various.CreatureInfo.Unknown &&
                                                         item.NameSingular.Length > 3);
        var creatureResponse = Search(index, creature.NameSingular, [HistoricalSearchCategory.Creature], pageSize: 100);
        var creatureResult = creatureResponse.Items.First(item => item.Category == HistoricalSearchCategory.Creature);
        Assert.IsFalse(creatureResult.Navigable);
        Assert.IsNull(creatureResult.Url);
        StringAssert.StartsWith(creatureResult.Key, "creature:");

        var collection = world.EventCollections.First(item => !string.IsNullOrWhiteSpace(item.Name));
        var collectionResponse = Search(index, collection.Name, pageSize: 100);
        var collectionResult = collectionResponse.Items.First(item => item.Id == collection.Id &&
            item.Category is HistoricalSearchCategory.War or HistoricalSearchCategory.Battle or HistoricalSearchCategory.EventCollection);
        Assert.IsNotNull(collectionResult.Url);
        Assert.IsTrue(collectionResult.Navigable);
    }

    [TestMethod]
    public async Task ParallelEventIndexingPreservesEventIdentityAndCounts()
    {
        var world = await ParseWorldAsync(extended: false);
        world.Events.Clear();
        for (int eventIndex = 0; eventIndex < 5_000; eventIndex++)
        {
            world.Events.Add(new WorldEvent([
                new Property { Name = "id", Value = (100_000 + eventIndex).ToString() },
                new Property { Name = "year", Value = (eventIndex % 400).ToString() },
                new Property { Name = "type", Value = "hf died" }
            ], world));
        }
        var index = HistoricalSearchIndex.Build(world);

        var response = Search(index, "historical figure death", [HistoricalSearchCategory.Event], pageSize: 100);
        Assert.AreEqual(5_000, response.TotalCount);
        Assert.AreEqual(100, response.Items.Count);
        Assert.AreEqual(100, response.Items.Select(item => item.Key).Distinct().Count());
        Assert.IsTrue(response.Items.All(item => item.Category == HistoricalSearchCategory.Event));
        Assert.IsTrue(response.Items.All(item => item.Key.StartsWith("event:10", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void EmptyAndMalformedQueriesNeverThrowAndReturnNoFalseMatches()
    {
        var world = new World();
        var index = HistoricalSearchIndex.Build(world);
        var empty = Search(index, "   ");
        Assert.AreEqual(0, empty.TotalCount);
        Assert.AreEqual(0, empty.Items.Count);
        Assert.IsFalse(TextNormalizer.HasSearchableCharacter("!!! ---"));
        Assert.AreEqual(string.Empty, TextNormalizer.Normalize("!!! ---"));
        Assert.AreEqual("urist mclegend", TextNormalizer.Normalize("  Urist-McLegend!! "));
    }

    [TestMethod]
    public async Task SearchApiValidatesInputsAndReportsNoWorld()
    {
        var (sessions, _, lease) = CreateSessions();
        using (lease)
        {
            var controller = Controller(lease);
            var noWorld = (HistoricalSearchResponse)((OkObjectResult)controller.Search("test").Result!).Value!;
            Assert.IsFalse(noWorld.WorldLoaded);
            StringAssert.Contains(noWorld.Message!, "No world is loaded");

            Assert.AreEqual(400, ((ObjectResult)controller.Search("test", page: 0).Result!).StatusCode);
            Assert.AreEqual(400, ((ObjectResult)controller.Search("test", pageSize: 101).Result!).StatusCode);
            Assert.AreEqual(400, ((ObjectResult)controller.Search("test", fromYear: 20, toYear: 10).Result!).StatusCode);
            Assert.AreEqual(400, ((ObjectResult)controller.Search("test", categories: "NotACategory").Result!).StatusCode);
            Assert.AreEqual(400, ((ObjectResult)controller.Search("!!!").Result!).StatusCode);
            Assert.AreEqual(400, ((ObjectResult)controller.Search(new string('a', HistoricalSearchIndex.MaximumQueryLength + 1)).Result!).StatusCode);
        }
    }

    [TestMethod]
    public async Task SearchApiReturnsResultsAndMetadataForPublishedWorld()
    {
        var (sessions, _, lease) = CreateSessions();
        using (lease)
        {
            var world = await ParseWorldAsync();
            var map = new Mock<IWorldMapImageGenerator>();
            sessions.Publish(world, map.Object);
            using var active = sessions.Lease();
            var controller = Controller(active);
            var result = (HistoricalSearchResponse)((OkObjectResult)controller.Search("artifact created", categories: "Event").Result!).Value!;
            Assert.IsTrue(result.WorldLoaded);
            Assert.AreEqual(active.GenerationId, result.Generation);
            Assert.IsTrue(result.TotalCount >= 1);
            Assert.AreEqual("Event", result.Items[0].Category.ToString());

            var metadata = (HistoricalSearchMetadataDto)((OkObjectResult)controller.Metadata().Result!).Value!;
            Assert.IsTrue(metadata.WorldLoaded);
            Assert.IsTrue(metadata.IndexedRecordCount > 0);
            Assert.IsTrue(metadata.CategoryCounts.ContainsKey("Event"));
        }
    }

    [TestMethod]
    public async Task WarmCacheRestoresSearchWithoutInvokingXmlParser()
    {
        var root = Path.Combine(Path.GetTempPath(), "nextplus-search-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var main = Path.Combine(root, "region-00005-01-01-legends.xml");
            File.Copy(Fixture("-legends.xml"), main);
            File.Copy(Fixture("-legends_plus.xml"), Path.Combine(root, "region-00005-01-01-legends_plus.xml"));
            var graphics = new GameInstallationService(new ApplicationSettingsService(Path.Combine(root, "settings.json")),
                new NoLocations());
            var sessions = new WorldSessionStore(graphics);
            var cache = new WorldCacheService(Path.Combine(root, "cache"));
            var firstLoader = new WorldLoadService(cache, graphics, sessions,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<WorldLoadService>.Instance);
            var first = await firstLoader.PrepareAsync(main);
            firstLoader.Publish(first);
            using (var lease = sessions.Lease())
            {
                Assert.IsTrue(lease.SearchIndex.Search("artifact created", null, null, null, 1, 10).TotalCount >= 1);
            }
            first.Map.Clear();

            var parserCalls = 0;
            var secondLoader = new WorldLoadService(new WorldCacheService(Path.Combine(root, "cache")), graphics, sessions,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<WorldLoadService>.Instance)
            {
                Parse = (_, _, _, _, _) =>
                {
                    parserCalls++;
                    throw new InvalidOperationException("XML PARSER MUST NOT RUN FOR SEARCH");
                }
            };
            var warm = await secondLoader.PrepareAsync(main);
            Assert.IsTrue(warm.CacheHit);
            secondLoader.Publish(warm);
            using (var lease = sessions.Lease())
            {
                var response = lease.SearchIndex.Search("artifact created", null, null, null, 1, 10);
                Assert.IsTrue(response.TotalCount >= 1);
                Assert.AreEqual(0, parserCalls);
            }
            warm.Map.Clear();
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task WorldSwitchKeepsOldLeaseAndNewGenerationIsolated()
    {
        var (sessions, _, initial) = CreateSessions();
        using (initial)
        {
            var worldA = await ParseWorldAsync();
            worldA.HistoricalFigures[0].Name = "Concurrent World Alpha";
            var worldB = await ParseWorldAsync();
            worldB.HistoricalFigures[0].Name = "Concurrent World Beta";
            var map = new Mock<IWorldMapImageGenerator>();

            sessions.Publish(worldA, map.Object);
            using var leaseA = sessions.Lease();
            long generationA = leaseA.GenerationId;
            Assert.AreEqual("Concurrent World Alpha", leaseA.SearchIndex.Search("concurrent world", null, null, null, 1, 10).Items[0].Name);

            sessions.Publish(worldB, map.Object);
            using var leaseB = sessions.Lease();
            Assert.AreNotEqual(generationA, leaseB.GenerationId);
            Assert.AreEqual("Concurrent World Beta", leaseB.SearchIndex.Search("concurrent world", null, null, null, 1, 10).Items[0].Name);
            Assert.AreEqual("Concurrent World Alpha", leaseA.SearchIndex.Search("concurrent world", null, null, null, 1, 10).Items[0].Name);
            Assert.AreEqual(generationA, leaseA.GenerationId);
        }
    }

    [TestMethod]
    public async Task ConcurrentSearchesDuringPublicationNeverMixWorlds()
    {
        var (sessions, _, initial) = CreateSessions();
        using (initial)
        {
            var worldA = await ParseWorldAsync();
            worldA.HistoricalFigures[0].Name = "Shared Publication Alpha";
            var worldB = await ParseWorldAsync();
            worldB.HistoricalFigures[0].Name = "Shared Publication Beta";
            var map = new Mock<IWorldMapImageGenerator>();
            sessions.Publish(worldA, map.Object);
            long generationA;
            using (var probe = sessions.Lease()) generationA = probe.GenerationId;

            var tasks = Enumerable.Range(0, 32).Select(async _ =>
            {
                using var lease = sessions.Lease();
                await Task.Yield();
                long generation = lease.GenerationId;
                var response = lease.SearchIndex.Search("shared publication", null, null, null, 1, 5);
                return (generation, response.Items);
            }).ToList();
            var publish = Task.Run(() => sessions.Publish(worldB, map.Object));
            await Task.WhenAll(tasks);
            await publish;
            using var after = sessions.Lease();
            long generationB = after.GenerationId;
            Assert.AreNotEqual(generationA, generationB);

            foreach (var (generation, items) in tasks.Select(task => task.Result))
            {
                Assert.IsTrue(generation == generationA || generation == generationB);
                string otherWorldMarker = generation == generationA ? "Shared Publication Beta" : "Shared Publication Alpha";
                Assert.IsFalse(items.Any(item =>
                        (item.Name ?? string.Empty).Contains(otherWorldMarker, StringComparison.Ordinal) ||
                        (item.Description ?? string.Empty).Contains(otherWorldMarker, StringComparison.Ordinal)),
                    "A search leaked another world generation: " +
                    string.Join(" | ", items.Select(item => $"{item.Category}:{item.Name}")));
            }
        }
    }

    [TestMethod]
    public async Task ReplacedIndexCanBeCollectedAfterLeasesRelease()
    {
        var (sessions, _, initial) = CreateSessions();
        using (initial)
        {
            var worldA = await ParseWorldAsync();
            var worldB = await ParseWorldAsync();
            var map = new Mock<IWorldMapImageGenerator>();
            sessions.Publish(worldA, map.Object);
            WeakReference weak = CreateWeakIndexReference(sessions);
            sessions.Publish(worldB, map.Object);

            for (int attempt = 0; attempt < 5 && weak.IsAlive; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            Assert.IsFalse(weak.IsAlive, "The replaced generation's search index should be collectible");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateWeakIndexReference(WorldSessionStore sessions)
    {
        using var lease = sessions.Lease();
        return new WeakReference(lease.SearchIndex);
    }

    private static SearchController Controller(WorldSessionLease lease) => new(lease)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };
}
