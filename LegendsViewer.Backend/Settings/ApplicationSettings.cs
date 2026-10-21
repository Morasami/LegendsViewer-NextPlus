using System.Text.Json;

namespace LegendsViewer.Backend.Settings;

public record ApplicationSettings
{
    public int Version { get; init; } = 1;
    public string? InstallationPath { get; init; }
    public bool ExplicitSelection { get; init; }
    public string? PreferredExportDirectory { get; init; }
    public string[] ExportDirectories { get; init; } = [];
}

public sealed class ApplicationSettingsService
{
    private readonly string filePath;
    private readonly object gate = new();
    public string? Warning { get; private set; }
    public ApplicationSettingsService() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LegendsViewer-NextPlus", "settings.json")) { }
    public ApplicationSettingsService(string filePath) => this.filePath = Path.GetFullPath(filePath);

    public void Update(Func<ApplicationSettings, ApplicationSettings> update)
    {
        lock (gate) Save(update(Read()));
    }

    public ApplicationSettings Read()
    {
        lock (gate)
        {
            Warning = null;
            try
            {
                if (!File.Exists(filePath)) return new();
                var settings = JsonSerializer.Deserialize<ApplicationSettings>(File.ReadAllText(filePath))
                    ?? throw new JsonException("Empty settings.");
                if (settings.Version != 1) throw new JsonException("Unsupported settings version.");
                return settings;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                Warning = "Settings could not be read. Choose an installation to repair settings. " + ex.Message;
                return new();
            }
        }
    }

    public void Save(ApplicationSettings settings)
    {
        lock (gate)
        {
            var directory = Path.GetDirectoryName(filePath)!;
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, $".settings-{Guid.NewGuid():N}.tmp");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, settings, new JsonSerializerOptions { WriteIndented = true });
                    stream.Flush(true);
                }
                File.Move(temporary, filePath, true);
                Warning = null;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
