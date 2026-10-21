using System.Text;
using LegendsViewer.Backend.Controllers;
using LegendsViewer.Backend.Legends.Bookmarks;
using LegendsViewer.Backend.Legends.Interfaces;
using LegendsViewer.Backend.Legends.Maps;
using LegendsViewer.Backend.Settings;
using LegendsViewer.Backend.WorldLibrary;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LegendsViewer.Backend.Tests;

[TestClass]
public class WorldLibraryTests
{
    private string root = null!, exports = null!, indexPath = null!;
    private ApplicationSettingsService settings = null!;
    private GameInstallationService installations = null!;
    private IBookmarkService bookmarks = null!;
    private WorldLibraryService library = null!;
    private class EmptyLocations : InstallationLocations
    {
        public override string? EnvironmentOverride => null;
        public override IEnumerable<string> SteamRoots() => [];
        public override IEnumerable<string> StandaloneRoots() => [];
    }
    [TestInitialize]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "library-" + Guid.NewGuid().ToString("N"));
        exports = Path.Combine(root, "exports Ã© ä¸–ç•Œ %20");
        Directory.CreateDirectory(exports);
        settings = new(Path.Combine(root, "settings.json"));
        installations = new(settings, new EmptyLocations());
        bookmarks = new BookmarkService(Path.Combine(root, "bookmarks.json"));
        indexPath = Path.Combine(root, "world-library.json");
        library = Restart();
    }
    private WorldLibraryService Restart() => new(settings, installations, bookmarks, indexPath);
    [TestCleanup] public void Cleanup() => Directory.Delete(root, true);
    private string Export(string prefix = "region2-00184-01-01", string? directory = null, bool complete = true, string world = "World A")
    {
        directory ??= exports;
        Directory.CreateDirectory(directory);
        foreach (var suffix in ExportName.Suffixes.Where(p => complete || p.Key == "main"))
            File.WriteAllText(Path.Combine(directory, prefix + suffix.Value), suffix.Key == "extended"
                ? $"<?xml version=\"1.0\"?><df_world><name>{world}</name><altname>Alternate</altname><landmasses><landmass><id>0</id><name>Continent</name><coord_1>1,1</coord_1></landmass></landmasses><regions/></df_world>" : "fixture");
        return Path.Combine(directory, prefix + "-legends.xml");
    }
    private LibraryState Scan() => library.SetDirectories([exports]);

    [TestMethod] public void CompleteSetHasExactSixCompanionsAndHeaderIdentity()
    {
        Export(); var snapshot = Scan().Snapshots.Single();
        Assert.IsTrue(snapshot.Available);
        Assert.AreEqual(6, snapshot.Files.Count(f => f.Status == "Available"));
        Assert.AreEqual("World A", snapshot.Name);
        Assert.IsNotNull(snapshot.IdentityEvidence);
        Assert.AreEqual("00184-01-01", snapshot.Date);
        Assert.IsNull(snapshot.Thumbnail);
    }
    [TestMethod] public void StandardFiveFilesDoNotRequireLegacyBmp()
    {
        Export(); File.Delete(Path.Combine(exports, "region2-00184-01-01-world_map.bmp"));
        var snapshot = Scan().Snapshots.Single();
        Assert.IsTrue(snapshot.Available);
        Assert.AreEqual(5, snapshot.Files.Count(f => f.Status == "Available"));
        Assert.IsNull(snapshot.Warning);
    }
    [TestMethod] public void PartialAndLegacyExportsRemainLoadable()
    {
        Export(complete: false);
        File.WriteAllText(Path.Combine(exports, "region2-00184-01-01-world_map.bmp"), "bmp");
        var snapshot = Scan().Snapshots.Single();
        Assert.IsTrue(snapshot.Available);
        Assert.AreEqual("Missing", snapshot.Files.Single(f => f.Kind == "extended").Status);
        Assert.AreEqual("Available", snapshot.Files.Single(f => f.Kind == "bmp").Status);
    }
    [TestMethod] public void DatesGroupOnlyWithMatchingRegionDirectoryAndGeography()
    {
        Export("region2-00184-01-01"); Export("region2-00185-01-01"); Export("region3-00185-01-01");
        Export("region2-00186-01-01", world: "Different world");
        var snapshots = Scan().Snapshots;
        Assert.AreEqual(4, snapshots.Count);
        Assert.AreEqual(3, snapshots.Select(s => s.WorldId).Distinct().Count());
        Assert.AreEqual(snapshots[0].WorldId, snapshots[1].WorldId);
    }
    [TestMethod] public void AmbiguousWorldsStaySeparate()
    {
        Export("region2-00184-01-01", complete: false); Export("region2-00185-01-01", complete: false);
        Assert.AreEqual(2, Scan().Snapshots.Select(s => s.WorldId).Distinct().Count());
    }
    [TestMethod] public void IdenticalFilenamesInDifferentDirectoriesHaveDifferentIds()
    {
        Export(); var other = Path.Combine(root, "other"); Export(directory: other);
        var state = library.SetDirectories([exports, other]);
        Assert.AreEqual(2, state.Snapshots.Select(s => s.Id).Distinct().Count());
        Assert.AreEqual(2, state.Snapshots.Select(s => s.WorldId).Distinct().Count());
    }
    [TestMethod] public void InvalidSuffixesAreIgnoredAndUndatedExportsRemainSeparate()
    {
        Assert.IsNull(ExportName.Parse("foo-legends.xml.backup"));
        Assert.IsNull(ExportName.Parse("-legends.xml"));
        Assert.IsNull(ExportName.Parse("foo.xml"));
        Assert.IsNull(ExportName.Parse("region-00123-13-01-legends.xml")!.Date);
        Assert.IsNull(ExportName.Parse("region-00123-01-29-legends.xml")!.Date);
        Export("name-legends.xml-in-middle", complete: false);
        var s = Scan().Snapshots.Single();
        Assert.AreEqual("name-legends.xml-in-middle", s.Prefix);
        Assert.IsNull(s.Date);
    }
    [TestMethod] public void MissingMainIsDisplayedWithActionableWarning()
    {
        var main = Export(); File.Delete(main);
        var snapshot = Scan().Snapshots.Single();
        Assert.IsFalse(snapshot.Available);
        StringAssert.Contains(snapshot.Warning!, "Main legends XML");
    }
    [TestMethod] public void DisappearingDirectoriesRetainHistoryAcrossRestart()
    {
        Export(); var original = Scan().Snapshots.Single(); Directory.Delete(exports, true);
        var state = Restart().Scan();
        Assert.AreEqual(original.Id, state.Snapshots.Single().Id);
        Assert.IsFalse(state.Snapshots.Single().Available);
        Assert.IsNotNull(state.Roots.Single().Warning);
    }
    [TestMethod] public void ExclusiveFileAccessIsReportedWithoutCrashing()
    {
        var main = Export();
        using var locked = new FileStream(main, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.AreEqual("Inaccessible", Scan().Snapshots.Single().Files.Single(f => f.Kind == "main").Status);
    }
    [TestMethod] public void AdditionalFoldersPersistAndNeverOverwriteInstallation()
    {
        settings.Save(new() { InstallationPath = Path.Combine(root, "game"), ExplicitSelection = true });
        Scan();
        var reloadedSettings = new ApplicationSettingsService(Path.Combine(root, "settings.json")).Read();
        CollectionAssert.AreEqual(new[] { exports }, reloadedSettings.ExportDirectories);
        Assert.AreEqual(Path.Combine(root, "game"), reloadedSettings.InstallationPath);
        Assert.IsTrue(reloadedSettings.ExplicitSelection);
        library.SetDirectories([]);
        Assert.AreEqual(0, settings.Read().ExportDirectories.Length);
    }
    [TestMethod] public void ScanDoesNotTraverseNestedSaveOrModFolders()
    {
        Export(directory: Path.Combine(exports, "save", "deep"));
        Export(directory: Path.Combine(exports, "mods"));
        Assert.AreEqual(0, Scan().Snapshots.Count);
    }
    [TestMethod] public void IndexPersistsAndRecoversLastValidBackup()
    {
        Export(); Scan(); library.Scan();
        var original = Restart().Get().Snapshots.Single();
        File.WriteAllText(indexPath, "{broken");
        var recovered = Restart();
        Assert.AreEqual(original.Id, recovered.Get().Snapshots.Single().Id);
        Assert.IsNotNull(recovered.Get().Warning);
        recovered.Scan();
        Assert.AreEqual(original.Id, Restart().Get().Snapshots.Single().Id);
    }
    [TestMethod] public void UnsupportedIndexCanBeRebuiltWithoutTouchingBookmarks()
    {
        File.WriteAllText(indexPath, "{\"Version\":99}");
        var service = Restart();
        Assert.IsNotNull(service.Get().Warning);
        Export(); service.SetDirectories([exports]);
        Assert.AreEqual(1, Restart().Get().Snapshots.Count);
        Assert.IsFalse(File.Exists(Path.Combine(root, "bookmarks.json")));
    }
    [TestMethod] public void CancelledScanDoesNotPublishPartialIndex()
    {
        Export(); Scan(); var before = File.ReadAllBytes(indexPath);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.ThrowsException<OperationCanceledException>(() => library.Scan(cancel.Token));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(indexPath));
    }
    [TestMethod] public void MetadataChangeRefreshesIdentityAndUnavailableHistoryIsRetained()
    {
        var path = Export(); var first = Scan().Snapshots.Single();
        Export(world: "Changed world identity");
        var changed = library.Scan().Snapshots.Single();
        Assert.AreEqual(first.Id, changed.Id);
        Assert.AreNotEqual(first.WorldId, changed.WorldId);
        File.Delete(path);
        Assert.IsFalse(library.Scan().Snapshots.Single().Available);
    }
    [TestMethod] public void LibraryWritesRequireCustomHeaderAndRejectDriveRoots()
    {
        var controller = new WorldLibraryController(library) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        Assert.AreEqual(403, ((StatusCodeResult)controller.Refresh(default).Result!).StatusCode);
        Assert.AreEqual(403, ((StatusCodeResult)controller.Directories(new([exports])).Result!).StatusCode);
        Assert.ThrowsException<ArgumentException>(() => library.SetDirectories([Path.GetPathRoot(root)!]));
        Assert.ThrowsException<ArgumentException>(() => library.SetDirectories(["relative"]));
    }
    [TestMethod] public async Task ExistingLoaderUsesExactSelectedPathAndCompanionsAndRecordsManualImport()
    {
        var main = Export("odd-legends.xml-name-00184-01-01");
        Export("odd-legends.xml-name-00185-01-01");
        var world = new Mock<IWorld>(); world.SetupGet(w => w.Log).Returns(new StringBuilder());
        world.SetupGet(w => w.Name).Returns("Loaded World");
        var maps = new Mock<IWorldMapImageGenerator>();
        var controller = new BookmarkController(NullLogger<BookmarkController>.Instance, world.Object, maps.Object, bookmarks, library);
        Assert.IsInstanceOfType((await controller.ParseWorldXml(main)).Result, typeof(OkObjectResult));
        world.Verify(w => w.ParseAsync(main, Path.Combine(exports, "odd-legends.xml-name-00184-01-01-legends_plus.xml"),
            Path.Combine(exports, "odd-legends.xml-name-00184-01-01-world_history.txt"),
            Path.Combine(exports, "odd-legends.xml-name-00184-01-01-world_sites_and_pops.txt"),
            Path.Combine(exports, "odd-legends.xml-name-00184-01-01-world_map.bmp")), Times.Once);
        maps.Verify(m => m.LoadExportedWorldMapAsync(main), Times.Once);
        Assert.AreEqual("Validated", Restart().Get().Snapshots.Single().Status);
        Assert.AreEqual(2, library.Scan().Snapshots.Count);
    }
    [TestMethod] public async Task FailedLoadIsNotRecordedAsValidatedAndGateIsReleased()
    {
        var main = Export();
        var world = new Mock<IWorld>(); world.Setup(w => w.ParseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ThrowsAsync(new InvalidDataException("Bad XML"));
        var controller = new BookmarkController(NullLogger<BookmarkController>.Instance, world.Object, Mock.Of<IWorldMapImageGenerator>(), bookmarks, library);
        Assert.AreEqual(500, ((ObjectResult)(await controller.ParseWorldXml(main)).Result!).StatusCode);
        Assert.AreEqual("Failed", Restart().Get().Snapshots.Single().Status);
        Assert.AreEqual(0, bookmarks.GetAll().Count);
        Assert.AreEqual(500, ((ObjectResult)(await controller.ParseWorldXml(main)).Result!).StatusCode);
    }
    [TestMethod] public async Task ConcurrentLoadsAreRejectedWithoutInterleavingParserState()
    {
        var main = Export();
        var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
        var world = new Mock<IWorld>(); world.SetupGet(w => w.Log).Returns(new StringBuilder());
        world.Setup(w => w.ParseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(async () => { entered.SetResult(); await release.Task; });
        var controller = new BookmarkController(NullLogger<BookmarkController>.Instance, world.Object, Mock.Of<IWorldMapImageGenerator>(), bookmarks);
        var first = controller.ParseWorldXml(main);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsInstanceOfType((await controller.ParseWorldXml(main)).Result, typeof(ConflictObjectResult));
        }
        finally { release.SetResult(); await first; }
        world.Verify(w => w.Clear(), Times.Once);
    }

    [TestMethod] public void SameNamedBookmarksFromDifferentDirectoriesSurviveRestart()
    {
        Bookmark Make(string directory) => new() { FilePath = Path.Combine(directory, "region-{TIMESTAMP}-legends.xml"),
            WorldName = "Same name", WorldRegionName = "region", WorldTimestamps = ["00184-01-01"], LoadedTimestamp = "00184-01-01" };
        var first = bookmarks.AddBookmark(Make(exports));
        var second = bookmarks.AddBookmark(Make(Path.Combine(root, "other")));
        Assert.AreNotEqual(first.Id, second.Id);
        var reloaded = new BookmarkService(Path.Combine(root, "bookmarks.json"));
        Assert.AreEqual(2, reloaded.GetAll().Count);
        Assert.AreEqual(second.Id, reloaded.AddBookmark(Make(Path.Combine(root, "other"))).Id);
        Assert.AreEqual(2, reloaded.GetAll().Count);
        Assert.IsTrue(reloaded.DeleteBookmarkTimestamp(first.FilePath.Replace("{TIMESTAMP}", "00184-01-01")).Success);
        Assert.AreEqual(second.Id, reloaded.AddBookmark(Make(Path.Combine(root, "other"))).Id);
        Assert.AreEqual(1, reloaded.GetAll().Count);
    }

    [TestMethod] public void ManualBrowserPreservesLiteralUnicodeAndPercentPaths()
    {
        var main = Export();
        var controller = new FileSystemController();
        var result = (LegendsViewer.Backend.Contracts.FilesAndSubdirectoriesDto)((OkObjectResult)controller.Browse(exports).Result!).Value!;
        Assert.AreEqual(exports, result.CurrentDirectory);
        CollectionAssert.Contains(result.Files, Path.GetFileName(main));
        Assert.AreEqual(400, ((ObjectResult)controller.Browse(Path.Combine(root, "missing")).Result!).StatusCode);
    }

    [TestMethod] public void XmlEntitiesAndTruncatedHeadersNeverProvideIdentityEvidence()
    {
        Export();
        File.WriteAllText(Path.Combine(exports, "region2-00184-01-01-legends_plus.xml"),
            "<!DOCTYPE df_world [<!ENTITY x SYSTEM 'file:///not-allowed'>]><df_world><name>&x;</name></df_world>");
        var snapshot = Scan().Snapshots.Single();
        Assert.IsNull(snapshot.IdentityEvidence);
        Assert.IsTrue(snapshot.Available);
        File.WriteAllText(Path.Combine(exports, "region2-00184-01-01-legends_plus.xml"),
            "<df_world><name>A</name><altname>B</altname><landmasses><landmass><name>" + new string('x', 100000));
        Assert.IsNull(library.Scan().Snapshots.Single().IdentityEvidence);
    }

    [TestMethod] public void InvalidIndexEntriesRecoverAsDiscoveryMetadataInsteadOfCrashing()
    {
        File.WriteAllText(indexPath, "{\"Version\":1,\"Snapshots\":[null]}");
        var restarted = Restart();
        Assert.IsNotNull(restarted.Get().Warning);
        Assert.AreEqual(0, restarted.Get().Snapshots.Count);
    }

    [DataTestMethod]
    [DataRow("  [*] Test Leader (b. 0), Reign began: 102), current ruler")]
    [DataRow("  [*] Test Leader (b. 0, d. 103, Reign began: 102), former ruler")]
    public void SnapshotHistoryAcceptsBirthDateClosingBeforeReignField(string leaderLine)
    {
        var world = new LegendsViewer.Backend.Legends.World();
        var civ = new LegendsViewer.Backend.Legends.WorldObjects.Entity([], world) { Name = "Test Civ" };
        var leader = new LegendsViewer.Backend.Legends.WorldObjects.HistoricalFigure { Name = "Test Leader" };
        world.Entities.Add(civ); world.HistoricalFigures.Add(leader);
        var history = Path.Combine(root, "history.txt");
        File.WriteAllText(history, "Test World\nAlternate\nTest Civ, dwarves\n King List\n" + leaderLine + "\n");
        using var parser = new LegendsViewer.Backend.Legends.Parser.HistoryParser(world, history);
        parser.Parse();
        Assert.AreEqual(102, leader.Positions!.Single().StartYear);
        Assert.AreSame(civ, leader.Positions!.Single().Entity);
    }
}
