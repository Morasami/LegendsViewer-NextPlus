using System.Text.Json;
using System.Xml;
using System.Text.Json.Serialization;
using LegendsViewer.Backend.WorldCache;
using LegendsViewer.Backend.Legends.Bookmarks;
using LegendsViewer.Backend.Settings;

namespace LegendsViewer.Backend.WorldLibrary;

public record ExportFile(string Kind, string Path, string Status, long? Size, DateTime? ModifiedUtc);
public record ExportRoot(string Path, string Source, string? Warning);
public record LibrarySnapshot
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CacheInfo? Cache { get; set; }
    public string Id { get; init; } = "";
    public string WorldId { get; set; } = "";
    public string Directory { get; init; } = "";
    public string Prefix { get; init; } = "";
    public string Region { get; init; } = "";
    public string? Date { get; init; }
    public string? Name { get; set; }
    public string? AlternativeName { get; set; }
    public string? IdentityEvidence { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public byte[]? Thumbnail { get; set; }
    public List<ExportFile> Files { get; set; } = [];
    public string Status { get; set; } = "Discovered";
    public string? Warning { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public bool Available => Files.Any(f => f.Kind == "main" && f.Status == "Available");
}
public record LibraryIndex
{
    public int Version { get; init; } = 1;
    public List<LibrarySnapshot> Snapshots { get; set; } = [];
}
public record LibraryState(IReadOnlyList<LibrarySnapshot> Snapshots, IReadOnlyList<ExportRoot> Roots,
    string[] ExportDirectories, bool PremiumGraphicsAvailable, string? Warning);

// Only discovery metadata is persisted here; cache state is enriched on detached API snapshots.
public sealed class WorldLibraryService
{
    public const int MaxEntriesPerRoot = 10000;
    public const int MaxRoots = 64;
    private readonly ApplicationSettingsService settings;
    private readonly GameInstallationService installations;
    private readonly IBookmarkService bookmarks;
    private readonly string indexPath;
    private readonly object gate = new();
    private LibraryIndex index;
    private string? warning;
    private string? scanWarning;
    private List<ExportRoot> roots = [];
    private readonly WorldCacheService? cache;
    public WorldLibraryService(ApplicationSettingsService settings, GameInstallationService installations, IBookmarkService bookmarks, WorldCacheService cache)
        : this(settings, installations, bookmarks) => this.cache = cache;
    public WorldLibraryService(ApplicationSettingsService settings, GameInstallationService installations, IBookmarkService bookmarks)
        : this(settings, installations, bookmarks, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LegendsViewer-NextPlus", "world-library.json")) { }
    public WorldLibraryService(ApplicationSettingsService settings, GameInstallationService installations, IBookmarkService bookmarks, string indexPath)
    {
        this.settings = settings; this.installations = installations; this.bookmarks = bookmarks; this.indexPath = indexPath;
        index = ReadIndex(indexPath) ?? ReadIndex(indexPath + ".bak") ?? new();
    }

    private LibraryIndex? ReadIndex(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new JsonException("Index exceeds size limit.");
            var result = JsonSerializer.Deserialize<LibraryIndex>(File.ReadAllText(path));
            if (result is not { Version: 1 } || result.Snapshots == null || result.Snapshots.Any(s => s == null
                || string.IsNullOrWhiteSpace(s.Id) || string.IsNullOrWhiteSpace(s.Directory)
                || !Path.IsPathFullyQualified(s.Directory) || s.Files == null || s.Files.Any(f => f == null))
                || result.Snapshots.Select(s => s.Id).Distinct().Count() != result.Snapshots.Count)
                throw new JsonException("Unsupported or invalid index.");
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { warning = "Library index could not be read; recovered backup where available. Rescan to rebuild discovery metadata. " + ex.Message; return null; }
    }

    private void SaveIndex(LibraryIndex value)
    {
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
        var temporary = indexPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, value); stream.Flush(true); }
            // Keep the last valid generation for recovery. Never replace a good backup with corrupt data.
            if (File.Exists(indexPath) && ReadIndex(indexPath) != null) File.Replace(temporary, indexPath, indexPath + ".bak");
            else File.Move(temporary, indexPath, true);
        }
        finally { File.Delete(temporary); }
    }

    public LibraryState Get()
    {
        CacheInfo? CacheState(LibrarySnapshot snapshot)
        {
            var info = cache?.Info(Path.Combine(snapshot.Directory, snapshot.Prefix + "-legends.xml"));
            return info?.State == "NeverImported" && snapshot.Width.HasValue
                ? new("Unavailable", "No parsed-world cache. Rebuild on next load.") : info;
        }
        lock (gate) return new(index.Snapshots.Select(s => Clone(s) with { Cache = CacheState(s) }).ToArray(), roots.ToArray(), settings.Read().ExportDirectories,
            installations.Discover().Selected?.PremiumGraphicsAvailable == true, warning ?? scanWarning);
    }
    private static LibrarySnapshot Clone(LibrarySnapshot s) => s with { Files = [.. s.Files], Thumbnail = s.Thumbnail?.ToArray() };

    public LibraryState SetDirectories(string[] paths)
    {
        if (paths == null || paths.Length > 32) throw new ArgumentException("Choose at most 32 additional export folders.");
        var normalized = paths.Select(GameInstallationService.Normalize).DistinctBy(ExportName.PathKey).ToArray();
        if (normalized.Any(p => Path.GetPathRoot(p) == p)) throw new ArgumentException("Choose an export folder rather than an entire drive.");
        lock (gate) settings.Update(saved => saved with { ExportDirectories = normalized });
        return Scan();
    }

    private IEnumerable<ExportRoot> FindRoots()
    {
        var saved = settings.Read();
        foreach (var path in saved.ExportDirectories) yield return new(path, "Additional", null);
        if (saved.PreferredExportDirectory != null) yield return new(saved.PreferredExportDirectory, "Previously selected", null);
        var selected = installations.Discover().Selected;
        if (selected != null)
        {
            yield return new(selected.Path, "Game installation", null);
            foreach (var child in new[] { "exports", "export", "legends", "user/exports" })
            {
                var path = Path.Combine(selected.Path, child);
                if (System.IO.Directory.Exists(path)) yield return new(path, "Game exports", null);
            }
        }
        foreach (var bookmark in bookmarks.GetAll())
            if (Path.GetDirectoryName(bookmark.FilePath) is { } path) yield return new(path, "Previously imported", null);
    }

    public LibraryState Scan(CancellationToken cancellation = default)
    {
        lock (gate)
        {
            var next = new LibraryIndex { Snapshots = index.Snapshots.Select(Clone).ToList() };
            var previousSnapshots = index.Snapshots.ToDictionary(s => s.Id);
            var snapshotsById = next.Snapshots.ToDictionary(s => s.Id);
            foreach (var snapshot in next.Snapshots)
            { snapshot.Files = snapshot.Files.Select(f => f with { Status = "Unavailable" }).ToList(); snapshot.Warning = "Export is unavailable or outside current discovery roots. Restore the folder or add its location."; }
            var scannedRoots = new List<ExportRoot>();
            var knownBookmarks = bookmarks.GetAll();
            var discoveryRoots = FindRoots().DistinctBy(r => ExportName.PathKey(r.Path)).ToArray();
            foreach (var root in discoveryRoots.Take(MaxRoots))
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    if (Path.GetPathRoot(root.Path) == root.Path) throw new IOException("Drive-wide scans are disabled; choose an export folder.");
                    // Refuse reparse points in any path component, including explicitly configured roots.
                    for (var parent = new DirectoryInfo(root.Path); parent != null; parent = parent.Parent)
                        if ((parent.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked folders are not scanned; choose the physical export location.");
                    var files = new List<(string Path, ExportName Name)>();
                    int count = 0;
                    foreach (var path in System.IO.Directory.EnumerateFiles(root.Path))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (++count > MaxEntriesPerRoot) break;
                        if (ExportName.Parse(Path.GetFileName(path)) is { } name) files.Add((path, name));
                    }
                    foreach (var group in files.GroupBy(f => f.Name.Prefix, StringComparer.Ordinal))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        var name = group.First().Name;
                        var id = ExportName.Id(ExportName.PathKey(root.Path) + "\n" + name.Prefix);
                        snapshotsById.TryGetValue(id, out var snapshot);
                        if (snapshot == null)
                        {
                            snapshot = new() { Id = id, WorldId = id, Directory = root.Path, Prefix = name.Prefix, Region = name.Region, Date = name.Date };
                            next.Snapshots.Add(snapshot);
                            snapshotsById.Add(id, snapshot);
                        }
                        var previous = snapshot.Files;
                        snapshot.Files = ExportName.Suffixes.Select(pair => Inspect(pair.Key, Path.Combine(root.Path, name.Prefix + pair.Value))).ToList();
                        snapshot.Warning = previousSnapshots.GetValueOrDefault(id) is { Status: "Failed" } failed ? failed.Warning : null;
                        snapshot.LastSeenUtc = DateTime.UtcNow;
                        var plus = snapshot.Files.Single(f => f.Kind == "extended");
                        var oldPlus = previous.Find(f => f.Kind == "extended");
                        if (plus.Status == "Available" && (oldPlus?.Size != plus.Size || oldPlus?.ModifiedUtc != plus.ModifiedUtc))
                            ReadIdentity(snapshot, plus.Path);
                        var main = snapshot.Files.Single(f => f.Kind == "main");
                        var oldMain = previous.Find(f => f.Kind == "main");
                        if (oldMain != null && (oldMain.Size != main.Size || oldMain.ModifiedUtc != main.ModifiedUtc))
                        { snapshot.Status = "Changed"; snapshot.Warning = null; snapshot.Thumbnail = null; snapshot.Width = null; snapshot.Height = null; }
                        // Enrich only an exact known bookmark path and date; never infer identity from its legacy ID.
                        if (snapshot.Thumbnail == null && snapshot.Status != "Changed")
                        {
                            var bookmark = knownBookmarks.Find(b => b.WorldTimestamps.Contains(snapshot.Date ?? "")
                                && ExportName.PathKey(b.FilePath.Replace(BookmarkService.TimestampPlaceholder, snapshot.Date ?? "")) == ExportName.PathKey(main.Path));
                            if (bookmark != null)
                            { snapshot.Thumbnail = bookmark.WorldMapImage; snapshot.Width = bookmark.WorldWidth; snapshot.Height = bookmark.WorldHeight;
                                snapshot.Name ??= bookmark.WorldName; snapshot.AlternativeName ??= bookmark.WorldAlternativeName; }
                        }
                        if (snapshot.Date != null && snapshot.IdentityEvidence != null)
                            snapshot.WorldId = ExportName.Id(ExportName.PathKey(root.Path) + "\n" + snapshot.Region + "\n" + snapshot.IdentityEvidence);
                        else snapshot.WorldId = snapshot.Id;
                        if (!snapshot.Available) snapshot.Warning = "Main legends XML is missing or inaccessible. Restore it before loading.";
                    }
                    scannedRoots.Add(root with { Warning = count > MaxEntriesPerRoot ? "Scan stopped at 10,000 entries. Use a smaller export folder." : null });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                { scannedRoots.Add(root with { Warning = "Cannot scan folder. Restore it, check access, or remove it: " + ex.Message }); }
            }
            cancellation.ThrowIfCancellationRequested();
            SaveIndex(next); index = next; roots = scannedRoots;
            scanWarning = discoveryRoots.Length > MaxRoots ? "Only the first 64 discovery folders were scanned. Reduce configured or previously imported locations." : null;
            return Get();
        }
    }

    private static ExportFile Inspect(string kind, string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return new(kind, path, "Missing", null, null);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0) return new(kind, path, "Inaccessible", null, null);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return new(kind, path, "Available", info.Length, info.LastWriteTimeUtc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new(kind, path, "Inaccessible", null, null); }
    }

    private static void ReadIdentity(LibrarySnapshot snapshot, string path)
    {
        snapshot.IdentityEvidence = null;
        try
        {
            // Read at most 64 KiB, only on new/changed exports. DTDs and external entities are disabled.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            byte[] bytes = new byte[65536];
            int length = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
            using var memory = new MemoryStream(bytes, 0, length);
            using var reader = XmlReader.Create(memory, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var evidence = new List<string>();
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || reader.Depth != 1) continue;
                if (reader.Name == "name") { snapshot.Name = reader.ReadString(); evidence.Add(snapshot.Name); }
                else if (reader.Name == "altname") { snapshot.AlternativeName = reader.ReadString(); evidence.Add(snapshot.AlternativeName); }
                else if (reader.Name == "landmasses")
                {
                    var geography = reader.ReadOuterXml();
                    if (evidence.Count == 2 && geography.Contains("<landmass>"))
                        snapshot.IdentityEvidence = ExportName.Id(string.Join("\n", evidence) + "\n" + geography);
                    break;
                }
                else break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException) { /* Ambiguous identity stays separate. */ }
    }

    public void RecordLoad(string path, Bookmark? bookmark, string? error = null)
    {
        lock (gate)
        {
            // Manual imports become discoverable without rewriting settings or bookmarks during scans.
            var parsed = ExportName.Parse(Path.GetFileName(path));
            if (parsed == null) return;
            var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            var id = ExportName.Id(ExportName.PathKey(directory) + "\n" + parsed.Prefix);
            var snapshot = index.Snapshots.Find(s => s.Id == id);
            if (snapshot == null)
            {
                snapshot = new() { Id = id, WorldId = id, Directory = directory, Prefix = parsed.Prefix, Region = parsed.Region, Date = parsed.Date,
                    Files = ExportName.Suffixes.Select(p => Inspect(p.Key, Path.Combine(directory, parsed.Prefix + p.Value))).ToList() };
                index.Snapshots.Add(snapshot);
            }
            snapshot.Status = bookmark == null ? "Failed" : "Validated";
            snapshot.Warning = error;
            if (bookmark != null)
            {
                snapshot.Name = bookmark.WorldName; snapshot.AlternativeName = bookmark.WorldAlternativeName;
                snapshot.Width = bookmark.WorldWidth; snapshot.Height = bookmark.WorldHeight; snapshot.Thumbnail = bookmark.WorldMapImage;
            }
            SaveIndex(index);
        }
    }
}
