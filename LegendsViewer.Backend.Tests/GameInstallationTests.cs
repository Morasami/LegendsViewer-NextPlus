using LegendsViewer.Backend.Settings;
using LegendsViewer.Backend.Controllers;
using LegendsViewer.Backend.Legends.Interfaces;
using LegendsViewer.Backend.Legends.Maps;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace LegendsViewer.Backend.Tests;

[TestClass]
public class GameInstallationTests
{
    private string root = null!;
    private ApplicationSettingsService settings = null!;
    private TestLocations locations = null!;
    private GameInstallationService service = null!;
    private class TestLocations : InstallationLocations
    {
        public string? Override;
        public string[] Roots = [];
        public string[] Standalone = [];
        public override string? EnvironmentOverride => Override;
        public override IEnumerable<string> SteamRoots() => Roots;
        public override IEnumerable<string> StandaloneRoots() => Standalone;
    }
    [TestInitialize]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "NextPlus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        settings = new(Path.Combine(root, "config", "settings.json"));
        locations = new();
        service = new(settings, locations);
    }
    [TestCleanup]
    public void Cleanup() => Directory.Delete(root, true);
    private string Game(string path, bool graphics = true)
    {
        Directory.CreateDirectory(Path.Combine(path, "data"));
        File.WriteAllText(Path.Combine(path, OperatingSystem.IsWindows() ? "Dwarf Fortress.exe" : "dwarfort"), "fixture");
        if (graphics) foreach (var file in GameInstallationService.GraphicsFiles)
        {
            var full = Path.Combine(path, file);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, "synthetic graphic");
        }
        return path;
    }
    [TestMethod]
    public void CustomSteamAndSecondaryLibrariesAreDetectedAndDeduplicated()
    {
        var steam = Path.Combine(root, "Custom Steam \u00e9");
        var library = Path.Combine(root, "Second library \u4e16\u754c");
        var first = Game(Path.Combine(steam, "steamapps", "common", "Dwarf Fortress"));
        var second = Game(Path.Combine(library, "steamapps", "common", "Dwarf Fortress"));
        File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"),
            "\"libraryfolders\" { \"0\" { \"path\" \"" + steam.Replace(@"\", @"\\") +
            "\" } \"1\" { \"path\" \"" + library.Replace(@"\", @"\\") + "\" } }");
        locations.Roots = [steam, steam];
        var result = service.Discover();
        CollectionAssert.AreEquivalent(new[] { first, second }, result.Candidates.Select(c => c.Path).ToArray());
        Assert.IsNull(result.Selected);
        StringAssert.Contains(result.Warning!, "Multiple");
        Assert.IsTrue(result.Candidates.All(c => c.PremiumGraphicsAvailable));
    }
    [TestMethod]
    public void ParsesOldAndModernVdfWithoutDependingOnLineLayout()
    {
        var library = Path.Combine(root, "Steam library");
        var escaped = library.Replace(@"\", @"\\");
        var paths = GameInstallationService.ParseLibraryPaths("\"libraryfolders\" { \"1\" \"" + escaped +
            "\" \"2\" { \"path\" \"" + escaped + "\" \"apps\" { \"975370\" \"123\" } } }").ToArray();
        CollectionAssert.AreEqual(new[] { library, library }, paths);
    }
    [TestMethod]
    public void ManualSelectionWinsAndPersistsAcrossRestart()
    {
        var manual = Game(Path.Combine(root, "manual"));
        locations.Override = Game(Path.Combine(root, "environment"));
        service.Save(manual + Path.DirectorySeparatorChar);
        var reloaded = new GameInstallationService(new(Path.Combine(root, "config", "settings.json")), locations).Discover();
        Assert.AreEqual(manual, reloaded.Selected!.Path);
        Assert.AreEqual("Manual", reloaded.Source);
        Assert.IsTrue(reloaded.Settings.ExplicitSelection);
        Assert.AreEqual(0, Directory.GetFiles(Path.Combine(root, "config"), "*.tmp").Length);
    }
    [TestMethod]
    public void MissingManualSelectionIsNeverSilentlyReplaced()
    {
        var manual = Game(Path.Combine(root, "manual"));
        service.Save(manual);
        Directory.Delete(manual, true);
        locations.Override = Game(Path.Combine(root, "other"));
        var result = service.Discover();
        Assert.AreEqual(manual, result.Selected!.Path);
        Assert.IsFalse(result.Selected.Valid);
        Assert.AreEqual(1, result.Candidates.Count);
        Assert.IsNotNull(result.Warning);
        Assert.IsNull(service.FindFile(GameInstallationService.GraphicsFiles[0]));
        Assert.AreEqual(manual, settings.Read().InstallationPath);
    }
    [TestMethod]
    public void RejectsRelativePathsAndFoldersNamedAfterTheGame()
    {
        Assert.IsFalse(service.Validate("relative").Valid);
        Assert.ThrowsException<ArgumentException>(() => service.Save(root));
        Assert.IsNull(service.Discover().Selected);
        Assert.IsFalse(service.Validate(Path.Combine(root, "missing")).Valid);
    }
    [TestMethod]
    public void MissingGraphicsDoesNotInvalidateClassicInstallation()
    {
        var game = Game(Path.Combine(root, "classic"), false);
        var result = service.Save(game);
        Assert.IsTrue(result.Selected!.Valid);
        Assert.IsFalse(result.Selected.PremiumGraphicsAvailable);
        StringAssert.Contains(result.Selected.Status, "missing");
    }
    [TestMethod]
    public void EnvironmentFallbackAndNonWindowsStandalonePathsWork()
    {
        var game = Game(Path.Combine(root, "standalone"));
        locations.Standalone = [game];
        Assert.AreEqual("Automatic", service.Discover().Source);
        locations.Override = game;
        Assert.AreEqual("Environment", service.Discover().Source);
        locations.Override = Path.Combine(root, "missing");
        Assert.AreEqual(game, service.Discover().Selected!.Path);
    }
    [TestMethod]
    public void SelectedLookupAndIconEndpointUseSameInstallationWithoutWritingAssets()
    {
        var first = Game(Path.Combine(root, "first"));
        var second = Game(Path.Combine(root, "second"));
        locations.Override = first;
        service.Save(second);
        var before = Directory.GetFiles(second, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);
        foreach (var file in GameInstallationService.GraphicsFiles)
            Assert.AreEqual(Path.GetFullPath(Path.Combine(second, file)), service.FindFile(file));
        var controller = new WorldMapController(Mock.Of<IWorld>(), Mock.Of<IWorldMapImageGenerator>(), service);
        Assert.AreEqual(Path.GetFullPath(Path.Combine(second, GameInstallationService.GraphicsDirectory + "images/world_map_details.png")),
            ((PhysicalFileResult)controller.GetGameSiteIcons()).FileName);
        Assert.IsNull(service.FindFile("../first/Dwarf Fortress.exe"));
        foreach (var file in before) CollectionAssert.AreEqual(file.Value, File.ReadAllBytes(file.Key));
        service.Save(first);
        Assert.AreEqual(Path.GetFullPath(Path.Combine(first, GameInstallationService.GraphicsFiles[0])), service.FindFile(GameInstallationService.GraphicsFiles[0]));
    }
    [TestMethod]
    public void MissingOrCorruptSettingsHaveActionableRecovery()
    {
        Assert.AreEqual(new ApplicationSettings(), settings.Read());
        Directory.CreateDirectory(Path.Combine(root, "config"));
        File.WriteAllText(Path.Combine(root, "config", "settings.json"), "{broken");
        Assert.IsNotNull(service.Discover().Warning);
        service.Save(Game(Path.Combine(root, "repair")));
        Assert.IsNull(service.Discover().Warning);
    }
    [TestMethod]
    public void BrowserOriginAndHostValidationRejectsUnrelatedWebsites()
    {
        var request = new DefaultHttpContext().Request;
        request.Host = new("localhost", 15421);
        request.Headers.Origin = "http://localhost:15422";
        Assert.IsTrue(LocalBrowserAccess.IsAllowed(request));
        request.Headers.Origin = "https://unrelated.example";
        Assert.IsFalse(LocalBrowserAccess.IsAllowed(request));
        request.Headers.Remove("Origin");
        request.Host = new("attacker.example", 15421);
        Assert.IsFalse(LocalBrowserAccess.IsAllowed(request));
    }
    [TestMethod]
    public void FolderBrowserPreservesLiteralPathsAndReportsUnavailableDirectories()
    {
        var child = Path.Combine(root, "folder %20 é");
        // Use a literal percent sequence: it must not be URL-decoded a second time.
        Directory.CreateDirectory(child);
        var controller = new GameSettingsController(service);
        var result = (OkObjectResult)controller.Directories(root).Result!;
        var listing = (LegendsViewer.Backend.Contracts.FilesAndSubdirectoriesDto)result.Value!;
        CollectionAssert.Contains(listing.Subdirectories, child);
        Assert.AreEqual(child, ((LegendsViewer.Backend.Contracts.FilesAndSubdirectoriesDto)
            ((OkObjectResult)controller.Directories(child).Result!).Value!).CurrentDirectory);
        Assert.AreEqual(400, ((ObjectResult)controller.Directories(Path.Combine(root, "missing")).Result!).StatusCode);
    }

    [TestMethod]
    public void SavingWithoutCustomHeaderIsRejected()
    {
        var controller = new GameSettingsController(service) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        Assert.AreEqual(403, ((StatusCodeResult)controller.Save(new(root)).Result!).StatusCode);
    }
}
