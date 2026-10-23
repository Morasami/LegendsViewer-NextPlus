using System.Text;
using LegendsViewer.Backend.Controllers;
using LegendsViewer.Backend.Legends;
using LegendsViewer.Backend.Legends.Bookmarks;
using LegendsViewer.Backend.Legends.Maps;
using LegendsViewer.Backend.Settings;
using LegendsViewer.Backend.WorldCache;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using LegendsViewer.Backend.Legends.Interfaces;
using Moq;
using WorldCacheProbe;

namespace LegendsViewer.Backend.Tests;

[TestClass]
public class WorldCacheTests
{
    private string root = null!, main = null!, cacheDirectory = null!;
    private WorldCacheService cache = null!;
    private GameInstallationService graphics = null!;
    private WorldSessionStore sessions = null!;
    private WorldLoadService loader = null!;
    private class NoLocations : InstallationLocations
    {
        public override string? EnvironmentOverride => null;
        public override IEnumerable<string> SteamRoots() => [];
        public override IEnumerable<string> StandaloneRoots() => [];
    }
    [TestInitialize]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "nextplus-cache-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        main = Path.Combine(root, "region-00005-01-01-legends.xml");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "TestData", "Xah_Atho-00005-01-01-legends.xml"), main);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "TestData", "Xah_Atho-00005-01-01-legends_plus.xml"), main.Replace("-legends.xml", "-legends_plus.xml"));
        cacheDirectory = Path.Combine(root, "cache"); cache = new(cacheDirectory);
        graphics = new(new ApplicationSettingsService(Path.Combine(root, "settings.json")), new NoLocations());
        sessions = new(graphics); loader = Loader(cache);
    }
    private WorldLoadService Loader(WorldCacheService service) => new(service, graphics, sessions, NullLogger<WorldLoadService>.Instance);
    [TestCleanup] public void Cleanup() => Directory.Delete(root, true);

    [TestMethod]
    public async Task ScopedDependencyInjectionDoesNotDisposeActiveHistoricalData()
    {
        var prepared = await loader.PrepareAsync(main); loader.Publish(prepared);
        var services = new ServiceCollection(); services.AddSingleton(sessions);
        services.AddScoped<WorldSessionLease>(s => s.GetRequiredService<WorldSessionStore>().Lease());
        services.AddScoped<IWorld>(s => new WorldView(s.GetRequiredService<WorldSessionLease>().World));
        using var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope()) Assert.AreEqual(97, scope.ServiceProvider.GetRequiredService<IWorld>().HistoricalFigures.Count);
        using var after = sessions.Lease(); Assert.AreEqual(97, after.World.HistoricalFigures.Count);
        Assert.IsTrue(after.Map.GenerateMapByteArray()!.Length > 0);
    }

    [TestMethod]
    public async Task FirstImportCreatesCacheAndRestartRestoresWithoutParserWithEquivalentBehavior()
    {
        var fresh = await loader.PrepareAsync(main);
        Assert.IsFalse(fresh.CacheHit); Assert.IsTrue(File.Exists(cache.CachePath(main)));
        var restarted = Loader(new(cacheDirectory));
        restarted.Parse = (_, _, _, _, _) => throw new Exception("XML PARSER MUST NOT RUN");
        var warm = await restarted.PrepareAsync(main);
        Assert.IsTrue(warm.CacheHit); Assert.IsFalse(warm.Offline);
        Assert.AreEqual(WorldEvidence.Behavior(fresh.World), WorldEvidence.Behavior(warm.World));
        Assert.IsTrue(warm.Map.GenerateMapByteArray()!.Length > 0, "Missing game graphics must still permit a map");
        fresh.Map.Clear(); warm.Map.Clear();
    }

    [TestMethod]
    public async Task SourceChangeDuringImportRejectsPublication()
    {
        var parse = loader.Parse;
        loader.Parse = async (w, m, p, h, s) => { await parse(w, m, p, h, s); File.AppendAllText(m, "\n"); };
        await Assert.ThrowsExceptionAsync<SourceChangedException>(() => loader.PrepareAsync(main));
        Assert.IsFalse(File.Exists(cache.CachePath(main)));
        using var active = sessions.Lease(); Assert.AreEqual(0, active.World.HistoricalFigures.Count);
    }

    [TestMethod]
    public async Task ChangedXmlIsParsedAndRebuiltOnNextLoad()
    {
        (await loader.PrepareAsync(main)).Map.Clear();
        File.AppendAllText(main, "\n");
        var calls = 0; var parse = loader.Parse;
        loader.Parse = (w, m, p, h, s) => { calls++; return parse(w, m, p, h, s); };
        var rebuilt = await loader.PrepareAsync(main);
        Assert.IsFalse(rebuilt.CacheHit); Assert.AreEqual(1, calls); Assert.AreEqual("Ready", cache.Info(main).State); rebuilt.Map.Clear();
    }

    [TestMethod]
    public async Task SameLengthAndTimestampContentChangeNeverHitsCache()
    {
        (await loader.PrepareAsync(main)).Map.Clear();
        var timestamp = File.GetLastWriteTimeUtc(main); var bytes = File.ReadAllBytes(main);
        var index = Array.IndexOf(bytes, (byte)'a', 100); Assert.IsTrue(index > 0); bytes[index] = (byte)'b';
        File.WriteAllBytes(main, bytes); File.SetLastWriteTimeUtc(main, timestamp);
        Assert.IsNull(new WorldCacheService(cacheDirectory).Restore(main));
    }

    [DataTestMethod]
    [DataRow("-world_history.txt")]
    [DataRow("-world_sites_and_pops.txt")]
    public async Task AddedHistoricalCompanionsInvalidate(string suffix)
    {
        (await loader.PrepareAsync(main)).Map.Clear();
        File.WriteAllText(main.Replace("-legends.xml", suffix), "new companion");
        Assert.AreEqual("SourceChanged", cache.Info(main).State);
        Assert.IsNull(new WorldCacheService(cacheDirectory).Restore(main));
    }

    [TestMethod]
    public async Task RemovedHistoricalCompanionInvalidatesWhileMainExists()
    {
        (await loader.PrepareAsync(main)).Map.Clear();
        File.Delete(main.Replace("-legends.xml", "-legends_plus.xml"));
        Assert.AreEqual("SourceChanged", cache.Info(main).State);
        Assert.IsNull(new WorldCacheService(cacheDirectory).Restore(main));
    }

    [TestMethod]
    public async Task ChangedRenderingCompanionsDoNotReparseHistory()
    {
        (await loader.PrepareAsync(main)).Map.Clear();
        File.WriteAllText(main.Replace("-legends.xml", "-world_map.csv"), "visual input");
        File.WriteAllText(main.Replace("-legends.xml", "-world_map.bmp"), "not an image");
        loader.Parse = (_, _, _, _, _) => throw new Exception("Parser invoked for visual change");
        var warm = await loader.PrepareAsync(main); Assert.IsTrue(warm.CacheHit); warm.Map.Clear();
    }

    [DataTestMethod]
    [DataRow("corrupt")]
    [DataRow("truncated")]
    [DataRow("outdated")]
    public async Task BrokenCachesFallBackAndAreReplacedSafely(string failure)
    {
        (await loader.PrepareAsync(main)).Map.Clear(); var path = cache.CachePath(main);
        if (failure == "corrupt") { var bytes = File.ReadAllBytes(path); bytes[^5] ^= 0xff; File.WriteAllBytes(path, bytes); }
        if (failure == "truncated") using (var file = File.OpenWrite(path)) file.SetLength(20);
        if (failure == "outdated")
        {
            using var file = File.Open(path, FileMode.Open, FileAccess.ReadWrite);
            var bytes = new byte[65536]; file.ReadExactly(bytes);
            var text = Encoding.UTF8.GetString(bytes); var i = text.IndexOf("\"Schema\":1", StringComparison.Ordinal) + "\"Schema\":".Length;
            bytes[i] = (byte)'0'; file.Position = 0; file.Write(bytes);
        }
        var calls = 0; var parse = loader.Parse;
        loader.Parse = (w, m, p, h, s) => { calls++; return parse(w, m, p, h, s); };
        var rebuilt = await loader.PrepareAsync(main);
        Assert.IsFalse(rebuilt.CacheHit); Assert.AreEqual(1, calls); Assert.AreEqual("Ready", cache.Info(main).State);
        Assert.IsNotNull(new WorldCacheService(cacheDirectory).Restore(main)); rebuilt.Map.Clear();
        Assert.AreEqual(0, Directory.GetFiles(cacheDirectory, "*.tmp").Length);
    }

    [TestMethod]
    public async Task SnapshotsAndDirectoriesNeverShareCachedHistory()
    {
        (await loader.PrepareAsync(main)).Map.Clear();
        var other = main.Replace("00005", "00006"); File.Copy(main, other);
        Assert.AreNotEqual(cache.CachePath(main), cache.CachePath(other));
        File.Copy(cache.CachePath(main), cache.CachePath(other));
        Assert.ThrowsException<InvalidDataException>(() => new WorldCacheService(cacheDirectory).Restore(other));
        Assert.AreEqual("Ready", cache.Info(main).State);
        var folder = Path.Combine(root, "other"); Directory.CreateDirectory(folder); var elsewhere = Path.Combine(folder, Path.GetFileName(main));
        Assert.AreNotEqual(cache.CachePath(main), cache.CachePath(elsewhere));
    }

    [TestMethod]
    public async Task OfflineCacheOpensWithWarningButChangedRemainingSourceIsRejected()
    {
        (await loader.PrepareAsync(main)).Map.Clear(); File.Delete(main);
        var offline = await loader.PrepareAsync(main);
        Assert.IsTrue(offline.CacheHit); Assert.IsTrue(offline.Offline); StringAssert.Contains(offline.Warning!, "read-only"); offline.Map.Clear();
        File.AppendAllText(main.Replace("-legends.xml", "-legends_plus.xml"), "changed");
        await Assert.ThrowsExceptionAsync<FileNotFoundException>(() => loader.PrepareAsync(main));
    }

    [TestMethod]
    public async Task FailedControllerRebuildPreservesLoadedBookmarkAndWorld()
    {
        using var initial = sessions.Lease();
        var bookmarks = new BookmarkService(Path.Combine(root, "bookmarks.json"));
        var controller = new BookmarkController(NullLogger<BookmarkController>.Instance, initial.World, initial.Map, bookmarks, null, loader)
            { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        Assert.IsInstanceOfType((await controller.ParseWorldXml(main)).Result, typeof(OkObjectResult));
        var bookmarkBytes = File.ReadAllBytes(Path.Combine(root, "bookmarks.json"));
        using var loaded = sessions.Lease();
        loader.Parse = (_, _, _, _, _) => throw new InvalidDataException("Rebuild failed");
        Assert.AreEqual(500, ((ObjectResult)(await controller.ParseWorldXml(main, true)).Result!).StatusCode);
        using var after = sessions.Lease(); Assert.AreSame(loaded.World, after.World);
        CollectionAssert.AreEqual(bookmarkBytes, File.ReadAllBytes(Path.Combine(root, "bookmarks.json")));
        Assert.AreEqual(BookmarkState.Loaded, bookmarks.GetAll().Single().State);
    }

    [TestMethod]
    public async Task FailedParsingDoesNotPublishCacheOrChangeActiveWorld()
    {
        var active = await loader.PrepareAsync(main); loader.Publish(active); cache.Delete(main);
        loader.Parse = (_, _, _, _, _) => throw new InvalidDataException("Failed parse");
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => loader.PrepareAsync(main));
        Assert.IsFalse(File.Exists(cache.CachePath(main)));
        using var lease = sessions.Lease(); Assert.AreSame(active.World, lease.World);
    }

    [TestMethod]
    public async Task DeletionAndForcedRebuildPreserveOtherSnapshotAndMetadata()
    {
        (await loader.PrepareAsync(main)).Map.Clear();
        var metadata = Path.Combine(root, "bookmarks.json"); File.WriteAllText(metadata, "user data");
        var other = Path.Combine(cacheDirectory, new string('a', 64) + ".lvwc"); File.WriteAllText(other, "another snapshot");
        cache.Delete(main); Assert.IsFalse(File.Exists(cache.CachePath(main))); Assert.IsTrue(File.Exists(other));
        (await loader.PrepareAsync(main)).Map.Clear(); var calls = 0; var parse = loader.Parse;
        loader.Parse = (w, m, p, h, s) => { calls++; return parse(w, m, p, h, s); };
        var rebuilt = await loader.PrepareAsync(main, true); Assert.IsFalse(rebuilt.CacheHit); Assert.AreEqual(1, calls);
        Assert.AreEqual("user data", File.ReadAllText(metadata)); Assert.IsTrue(File.Exists(other)); rebuilt.Map.Clear();
    }

    [TestMethod]
    public async Task PublishingPinsOldRequestsAndReleasesObsoleteMapAfterLastLease()
    {
        var old = new World(); var oldMap = new Mock<IWorldMapImageGenerator>();
        sessions.Publish(old, oldMap.Object);
        var lease = sessions.Lease();
        var next = await loader.PrepareAsync(main); loader.Publish(next);
        Assert.AreSame(old, lease.World); oldMap.Verify(m => m.Clear(), Times.Never);
        using (var current = sessions.Lease()) Assert.AreSame(next.World, current.World);
        lease.Dispose(); oldMap.Verify(m => m.Clear(), Times.Once);
    }

    [TestMethod]
    public async Task RealLoadingGateRejectsOverlapsAndKeepsPreviousWorldVisibleDuringParse()
    {
        var entered = new TaskCompletionSource(); var release = new TaskCompletionSource(); var parse = loader.Parse;
        loader.Parse = async (w, m, p, h, s) => { entered.SetResult(); await release.Task; await parse(w, m, p, h, s); };
        using var before = sessions.Lease();
        var controller = new BookmarkController(NullLogger<BookmarkController>.Instance, before.World, before.Map,
            new BookmarkService(Path.Combine(root, "bookmarks.json")), null, loader)
            { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        var pending = controller.ParseWorldXml(main);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var during = sessions.Lease(); Assert.AreSame(before.World, during.World);
            Assert.IsInstanceOfType((await controller.ParseWorldXml(main)).Result, typeof(ConflictObjectResult));
        }
        finally { release.SetResult(); }
        Assert.IsInstanceOfType((await pending).Result, typeof(OkObjectResult));
        using var after = sessions.Lease(); Assert.AreNotSame(before.World, after.World);
    }
}
