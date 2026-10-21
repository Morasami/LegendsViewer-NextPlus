using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace LegendsViewer.Backend.WorldLibrary;

public record ExportName(string Prefix, string Kind, string Region, string? Date)
{
    public static readonly Dictionary<string, string> Suffixes = new()
    {
        ["main"] = "-legends.xml", ["extended"] = "-legends_plus.xml",
        ["history"] = "-world_history.txt", ["populations"] = "-world_sites_and_pops.txt",
        ["csv"] = "-world_map.csv", ["bmp"] = "-world_map.bmp"
    };

    public static ExportName? Parse(string name)
    {
        var suffix = Suffixes.FirstOrDefault(pair => name.EndsWith(pair.Value, StringComparison.Ordinal));
        if (suffix.Key == null) return null;
        var prefix = name[..^suffix.Value.Length];
        if (string.IsNullOrWhiteSpace(prefix)) return null;
        var match = Regex.Match(prefix, @"^(.+)[-_](\d{5})-(\d{2})-(\d{2})$");
        if (match.Success && int.Parse(match.Groups[3].Value) is >= 1 and <= 12
            && int.Parse(match.Groups[4].Value) is >= 1 and <= 28)
            return new(prefix, suffix.Key, match.Groups[1].Value,
                $"{match.Groups[2].Value}-{match.Groups[3].Value}-{match.Groups[4].Value}");
        // Undated exports remain usable, but are never merged using a guessed date.
        return new(prefix, suffix.Key, prefix, null);
    }

    public static string Id(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public static string PathKey(string path) => OperatingSystem.IsWindows() ? Path.GetFullPath(path).ToUpperInvariant() : Path.GetFullPath(path);
}
