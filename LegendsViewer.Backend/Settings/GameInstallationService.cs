using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace LegendsViewer.Backend.Settings;

public record GameInstallation(string Path, bool Valid, bool PremiumGraphicsAvailable, string Status);
public record GameInstallationState(ApplicationSettings Settings, GameInstallation? Selected,
    string Source, string? Warning, IReadOnlyList<GameInstallation> Candidates);

// Platform discovery is separate from validation/resolution so tests never depend on installed games.
public class InstallationLocations
{
    public virtual string? EnvironmentOverride => Environment.GetEnvironmentVariable("DF_INSTALL_DIR");
    public virtual IEnumerable<string> SteamRoots()
    {
        var roots = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            foreach (var key in new[] { @"Software\Valve\Steam", @"SOFTWARE\WOW6432Node\Valve\Steam" })
            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                try
                {
                    using var registry = hive.OpenSubKey(key);
                    foreach (var value in new[] { "SteamPath", "InstallPath" })
                        if (registry?.GetValue(value) is string path && !string.IsNullOrWhiteSpace(path)) roots.Add(path);
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
            }
            foreach (var folder in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles })
            {
                var path = Environment.GetFolderPath(folder);
                if (!string.IsNullOrEmpty(path)) roots.Add(Path.Combine(path, "Steam"));
            }
        }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        roots.AddRange(new[] { ".local/share/Steam", ".steam/steam", ".steam/root",
            ".var/app/com.valvesoftware.Steam/data/Steam", ".var/app/com.valvesoftware.Steam/.local/share/Steam",
            "Library/Application Support/Steam" }.Select(path => Path.Combine(home, path)));
        return roots;
    }
    public virtual IEnumerable<string> StandaloneRoots() =>
        [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/Bay 12 Games/Dwarf Fortress")];
}

public sealed class GameInstallationService(ApplicationSettingsService settings, InstallationLocations locations)
{
    public const string GraphicsDirectory = "data/vanilla/vanilla_world_map/graphics/";
    public static readonly string[] GraphicsFiles = [GraphicsDirectory + "graphics_world_map.txt",
        .. new[] { "tiles", "forests", "mountains", "details" }.Select(part => GraphicsDirectory + $"images/world_map_{part}.png")];
    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path.Trim()))
            throw new ArgumentException("Enter an absolute game-folder path.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
    }

    public GameInstallation Validate(string path)
    {
        try
        {
            path = Normalize(path);
            if (!Directory.Exists(path)) return new(path, false, false, "Folder is unavailable or does not exist.");
            // Both executable and game data are required; a folder name alone is never sufficient.
            bool executable = new[] { "Dwarf Fortress.exe", "Dwarf_Fortress", "dwarfort", "Dwarf Fortress", "df" }
                .Any(name => File.Exists(Path.Combine(path, name)));
            if (!executable || !Directory.Exists(Path.Combine(path, "data")))
                return new(path, false, false, "This folder does not contain a Dwarf Fortress executable and data directory.");
            var missing = GraphicsFiles.Where(file => !File.Exists(Path.Combine(path, file))).ToArray();
            return new(path, true, missing.Length == 0, missing.Length == 0
                ? "Installation valid. Premium world-map graphics available."
                : "Installation valid. Premium graphics missing: " + string.Join(", ", missing.Select(Path.GetFileName)));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        { return new(path, false, false, "Invalid or inaccessible folder: " + ex.Message); }
    }

    public static IEnumerable<string> ParseLibraryPaths(string text)
    {
        // Supports modern path properties and the numeric entries in older libraryfolders.vdf files.
        return Regex.Matches(text, "\"(?:path|[0-9]+)\"\\s*\"((?:\\\\.|[^\"\\\\])*)\"", RegexOptions.IgnoreCase)
            .Select(match => match.Groups[1].Value.Replace(@"\\", @"\").Replace("\\\"", "\""))
            .Where(Path.IsPathFullyQualified);
    }

    public GameInstallationState Discover()
    {
        var saved = settings.Read();
        var paths = new List<string>();
        if (saved.InstallationPath != null) paths.Add(saved.InstallationPath);
        if (!string.IsNullOrWhiteSpace(locations.EnvironmentOverride)) paths.Add(locations.EnvironmentOverride);
        foreach (var steam in locations.SteamRoots())
        {
            paths.Add(Path.Combine(steam, "steamapps", "common", "Dwarf Fortress"));
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            try
            {
                if (File.Exists(vdf) && new FileInfo(vdf).Length < 2_000_000)
                    foreach (var library in ParseLibraryPaths(File.ReadAllText(vdf)))
                    {
                        paths.Add(Path.Combine(library, "steamapps", "common", "Dwarf Fortress"));
                        paths.Add(Path.Combine(library, "common", "Dwarf Fortress"));
                    }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        paths.AddRange(locations.StandaloneRoots());
        var candidates = paths.Select(Validate).Where(candidate => candidate.Valid)
            .DistinctBy(candidate => candidate.Path, PathComparer).ToArray();
        GameInstallation? selected = null;
        string source = "None";
        string? warning = settings.Warning;
        if (saved.ExplicitSelection)
        {
            selected = Validate(saved.InstallationPath ?? "");
            source = "Manual";
            if (!selected.Valid) warning = "Your selected installation is unavailable. Choose an alternative or restore the folder. " + warning;
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(locations.EnvironmentOverride))
            {
                var environment = Validate(locations.EnvironmentOverride);
                if (environment.Valid) { selected = environment; source = "Environment"; }
            }
            if (selected == null && candidates.Length == 1) { selected = candidates[0]; source = "Automatic"; }
            if (selected == null && candidates.Length > 1) warning = "Multiple installations found. Choose one in Dwarf Fortress Settings. " + warning;
        }
        return new(saved, selected, source, warning, candidates);
    }

    public GameInstallationState Save(string path)
    {
        var installation = Validate(path);
        if (!installation.Valid) throw new ArgumentException(installation.Status);
        settings.Update(saved => saved with { InstallationPath = installation.Path, ExplicitSelection = true });
        return Discover();
    }

    public string? FindFile(string relativePath)
    {
        var selected = Discover().Selected;
        if (selected is not { Valid: true }) return null;
        var path = Path.GetFullPath(Path.Combine(selected.Path, relativePath));
        var prefix = Path.EndsInDirectorySeparator(selected.Path) ? selected.Path : selected.Path + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return null;
        return File.Exists(path) ? path : null;
    }
}
