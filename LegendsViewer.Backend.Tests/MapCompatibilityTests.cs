using LegendsViewer.Backend.Legends;
using LegendsViewer.Backend.Legends.Maps;
using SkiaSharp;
using LegendsViewer.Backend.Settings;

namespace LegendsViewer.Backend.Tests;

[TestClass]
public class MapCompatibilityTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NextPlusMaps_" + Guid.NewGuid().ToString("N"));
    private readonly World _world = new() { Width = 2, Height = 2 };
    private string Xml => Path.Combine(_directory, "region-00001-01-01-legends.xml");
    private string Csv => Xml.Replace("-legends.xml", "-world_map.csv");
    private string Bmp => Xml.Replace("-legends.xml", "-world_map.bmp");

    public MapCompatibilityTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Xml, "<df_world/>");
        File.WriteAllText(Path.Combine(_directory, "graphics_world_map.txt"),
            "[TILE_GRAPHICS:WORLD_MAP_TILES:0:0:GRASSLAND_TEMP]\n");
        foreach (var name in new[] { "tiles", "forests", "mountains", "details" })
        {
            using var bitmap = new SKBitmap(16, 16);
            bitmap.Erase(SKColors.Lime);
            using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(_directory, $"world_map_{name}.png"), data.ToArray());
        }
        WriteCsv();
    }

    private void WriteCsv() => File.WriteAllText(Csv,
        "DFHACK_WORLD_MAP,3,2,2\nx,y,biome_type,evilness,savagery,elevation,flags,volcanism,peak,river_dirs\n" +
        "0,0,18,50,50,100,0,0,0,0\n1,0,18,50,50,100,0,0,0,0\n" +
        "0,1,18,50,50,100,0,0,0,0\n1,1,18,50,50,100,0,0,0,0\n");

    private WorldMapImageGenerator Generator(bool graphics = true) => new(_world,
        path => graphics ? Path.Combine(_directory, Path.GetFileName(path)) : null);

    // An actual 2x2, 24-bit BMP, created without game assets or an image encoder.
    private void WriteBmp()
    {
        using var writer = new BinaryWriter(File.Create(Bmp));
        writer.Write((ushort)0x4d42); writer.Write(70); writer.Write(0); writer.Write(54);
        writer.Write(40); writer.Write(2); writer.Write(2); writer.Write((ushort)1);
        writer.Write((ushort)24); writer.Write(0); writer.Write(16);
        writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < 2; x++) writer.Write(new byte[] { 0, 0, 255 });
            writer.Write((ushort)0);
        }
    }

    private static SKColor Pixel(WorldMapImageGenerator generator)
    {
        using var bitmap = SKBitmap.Decode(generator.GenerateMapByteArray(10)!);
        Assert.AreEqual(20, bitmap.Width);
        Assert.AreEqual(20, bitmap.Height);
        return bitmap.GetPixel(5, 5);
    }

    [TestMethod]
    public async Task PremiumCsvRendersWithSyntheticGraphicsAndTakesPrecedenceOverBmp()
    {
        WriteBmp();
        var generator = Generator();
        try { await generator.LoadExportedWorldMapAsync(Xml); Assert.AreEqual(SKColors.Lime, Pixel(generator)); }
        finally { generator.Clear(); }
    }

    [TestMethod]
    public async Task MissingCsvFallsBackToBmp()
    {
        File.Delete(Csv); WriteBmp();
        var generator = Generator();
        try { await generator.LoadExportedWorldMapAsync(Xml); Assert.AreEqual(SKColors.Red, Pixel(generator)); }
        finally { generator.Clear(); }
    }

    [TestMethod]
    public async Task MissingGraphicsFallsBackToBmp()
    {
        WriteBmp(); var generator = Generator(false);
        try { await generator.LoadExportedWorldMapAsync(Xml); Assert.AreEqual(SKColors.Red, Pixel(generator)); }
        finally { generator.Clear(); }
    }

    [TestMethod]
    public async Task MalformedCsvFallsBackAndRecordsReason()
    {
        File.WriteAllText(Csv, "bad header"); WriteBmp(); var generator = Generator();
        try
        {
            await generator.LoadExportedWorldMapAsync(Xml);
            Assert.AreEqual(SKColors.Red, Pixel(generator));
            StringAssert.Contains(_world.Log.ToString(), "Premium map unavailable");
        }
        finally { generator.Clear(); }
    }

    [TestMethod]
    public async Task DirectBmpInputIsStillSupported()
    {
        WriteBmp(); var generator = Generator();
        try { await generator.LoadExportedWorldMapAsync(Bmp); Assert.AreEqual(SKColors.Red, Pixel(generator)); }
        finally { generator.Clear(); }
    }

    [TestMethod]
    public async Task MissingOptionalInputsGenerateRegionsAndClearPreviousImage()
    {
        var generator = Generator();
        try
        {
            await generator.LoadExportedWorldMapAsync(Xml);
            Assert.AreEqual(SKColors.Lime, Pixel(generator));
            File.Delete(Csv);
            await generator.LoadExportedWorldMapAsync(Xml);
            Assert.AreNotEqual(SKColors.Lime, Pixel(generator));
        }
        finally { generator.Clear(); }
    }

    [TestMethod]
    public async Task PersistedInstallationSuppliesMapGraphicsAndCanChangeWithoutRestart()
    {
        var settings = new ApplicationSettingsService(Path.Combine(_directory, "settings.json"));
        var installations = new GameInstallationService(settings, new InstallationLocations());
        string Install(string name, SKColor color)
        {
            var root = Path.Combine(_directory, name);
            Directory.CreateDirectory(Path.Combine(root, GameInstallationService.GraphicsDirectory, "images"));
            File.WriteAllText(Path.Combine(root, "Dwarf Fortress.exe"), "synthetic executable");
            foreach (var file in GameInstallationService.GraphicsFiles)
                File.Copy(Path.Combine(_directory, Path.GetFileName(file)), Path.Combine(root, file));
            using var bitmap = new SKBitmap(16, 16);
            bitmap.Erase(color);
            using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(root, GameInstallationService.GraphicsDirectory, "images/world_map_tiles.png"), png.ToArray());
            return root;
        }
        var first = Install("first", SKColors.Lime);
        var second = Install("second", SKColors.Blue);
        installations.Save(first);
        var generator = new WorldMapImageGenerator(_world, installations.FindFile);
        try
        {
            await generator.LoadExportedWorldMapAsync(Xml);
            Assert.AreEqual(SKColors.Lime, Pixel(generator));
            installations.Save(second);
            await generator.LoadExportedWorldMapAsync(Xml);
            Assert.AreEqual(SKColors.Blue, Pixel(generator));
        }
        finally { generator.Clear(); }
    }

    public void Dispose() { _world.Dispose(); Directory.Delete(_directory, true); }
}
