using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LegendsViewer.Backend.Legends;
using LegendsViewer.Backend.WorldLibrary;

namespace LegendsViewer.Backend.WorldCache;

public record SourceDependency(string Kind, bool Present, long Length, DateTime ModifiedUtc, string? Sha256);
public record CacheManifest(int Schema, int Parser, string Contract, string SnapshotId, DateTime CreatedUtc,
    SourceDependency[] Sources, long PayloadBytes, long ExpandedBytes, string Sha256, int Figures, int Events);
public record CacheInfo(string State, string? Message = null, long? Bytes = null, DateTime? CreatedUtc = null);
public record CacheRestore(World World, bool Offline);
public sealed class SourceChangedException(string message) : IOException(message);

public sealed class WorldCacheService
{
    public const int SchemaVersion = 1;
    // Bump whenever parsing/resolution semantics change, even when model fields do not.
    public const int ParserCompatibilityVersion = 1;
    public const long MaxCacheBytes = 1024L * 1024 * 1024;
    public const long MaxExpandedBytes = 2L * 1024 * 1024 * 1024;
    private const int HeaderBytes = 65536;
    private const string Magic = "LVNPC001";
    private readonly string directory;
    private readonly ConcurrentDictionary<string, CacheInfo> phases = new();
    private static readonly string[] Historical = ["main", "extended", "history", "populations"];
    public WorldCacheService() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LegendsViewer-NextPlus", "cache")) { }
    public WorldCacheService(string directory) => this.directory = Path.GetFullPath(directory);

    public static string SnapshotId(string main)
    {
        var full = Path.GetFullPath(main); var parsed = ExportName.Parse(Path.GetFileName(full)) ?? throw new ArgumentException("Invalid export name.");
        return ExportName.Id(ExportName.PathKey(Path.GetDirectoryName(full)!) + "\n" + parsed.Prefix);
    }
    public string CachePath(string main) => Path.Combine(directory, SnapshotId(main) + ".lvwc");
    private static string Companion(string main, string kind)
    {
        var parsed = ExportName.Parse(Path.GetFileName(main))!;
        return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(main))!, parsed.Prefix + ExportName.Suffixes[kind]);
    }
    public void Phase(string main, string state, string? message = null) => phases[SnapshotId(main)] = new(state, message);
    public void EndPhase(string main) => phases.TryRemove(SnapshotId(main), out _);

    public SourceDependency[] Sources(string main, bool hash)
    {
        return ExportName.Suffixes.Keys.Select(kind =>
        {
            var path = Companion(main, kind); var info = new FileInfo(path);
            if (!info.Exists) return new SourceDependency(kind, false, 0, default, null);
            var length = info.Length; var modified = info.LastWriteTimeUtc;
            string? fingerprint = null;
            if (hash && Historical.Contains(kind))
            {
                using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                fingerprint = Convert.ToHexString(SHA256.HashData(file));
                info.Refresh();
                if (!info.Exists || info.Length != length || info.LastWriteTimeUtc != modified) throw new IOException("Source changed while fingerprinting.");
            }
            return new SourceDependency(kind, true, length, modified, fingerprint);
        }).ToArray();
    }

    private static bool SameHistory(SourceDependency[] a, SourceDependency[] b, bool hash, bool offline = false) => Historical.All(kind =>
    {
        var x = a.Single(s => s.Kind == kind); var y = b.Single(s => s.Kind == kind);
        if (offline && !y.Present) return true;
        if (x.Present != y.Present) return false;
        if (!x.Present) return true;
        return hash ? x.Sha256 == y.Sha256 : x.Length == y.Length && x.ModifiedUtc == y.ModifiedUtc;
    });

    public CacheInfo Info(string main)
    {
        if (phases.TryGetValue(SnapshotId(main), out var phase)) return phase;
        var path = CachePath(main);
        if (!File.Exists(path)) return new("NeverImported", "No parsed-world cache.");
        try
        {
            using var file = File.OpenRead(path); var manifest = ReadHeader(file, SnapshotId(main));
            var sources = Sources(main, false);
            var offline = !sources.Single(s => s.Kind == "main").Present;
            if (!SameHistory(manifest.Sources, sources, false, offline)) return new("SourceChanged", "Historical inputs changed; rebuild on next load.", file.Length, manifest.CreatedUtc);
            return new(offline ? "Offline" : "Ready", offline ? "Source unavailable. Cached history can be opened read-only." : "Metadata matches. Content and integrity are verified when opened.", file.Length, manifest.CreatedUtc);
        }
        catch (Exception ex) when (Recoverable(ex)) { return new("Invalid", ex.Message); }
    }

    public CacheRestore? Restore(string main)
    {
        var path = CachePath(main);
        if (!File.Exists(path)) return null;
        Phase(main, "Verifying", "Verifying source SHA-256 and cache integrity");
        using var file = File.OpenRead(path);
        var manifest = ReadHeader(file, SnapshotId(main));
        var metadata = Sources(main, false);
        var offline = !metadata.Single(s => s.Kind == "main").Present;
        if (!SameHistory(manifest.Sources, metadata, false, offline)) return null;
        var current = Sources(main, true);
        offline = !current.Single(s => s.Kind == "main").Present;
        if (!SameHistory(manifest.Sources, current, true, offline)) return null;
        var digest = Convert.ToHexString(SHA256.HashData(file));
        if (digest != manifest.Sha256) throw new InvalidDataException("Cache checksum mismatch.");
        file.Position = HeaderBytes;
        Phase(main, "Restoring", "Loading processed historical data from cache");
        using var gzip = new GZipStream(file, CompressionMode.Decompress, true);
        using var bounded = new LimitedReadStream(gzip, manifest.ExpandedBytes);
        var world = new WorldGraphCodec().Read(bounded);
        if (bounded.BytesRead != manifest.ExpandedBytes || world.HistoricalFigures.Count != manifest.Figures || world.Events.Count != manifest.Events)
            throw new InvalidDataException("Cache statistics do not match payload.");
        Phase(main, "Validating", "Validating restored world");
        WorldGraphValidation.Validate(world);
        // Detect replacements/additions during restoration as well as before it.
        if (!SameHistory(current, Sources(main, true), true, offline)) throw new IOException("Sources changed during cache restoration.");
        return new(world, offline);
    }

    public void Publish(string main, World world, SourceDependency[] before)
    {
        Phase(main, "Writing", "Creating parsed-world cache");
        if (!SameHistory(before, Sources(main, true), true)) throw new SourceChangedException("Sources changed during import; cache was not published.");
        Directory.CreateDirectory(directory);
        var path = CachePath(main); var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                file.SetLength(HeaderBytes); file.Position = HeaderBytes;
                long expanded;
                using (var gzip = new GZipStream(file, CompressionLevel.Fastest, true))
                using (var counter = new CountingWriteStream(gzip, MaxExpandedBytes))
                { new WorldGraphCodec().Write(counter, world); expanded = counter.BytesWritten; }
                var length = file.Length;
                if (length > MaxCacheBytes) throw new InvalidDataException("Cache exceeds size limit.");
                file.Position = HeaderBytes;
                var digest = Convert.ToHexString(SHA256.HashData(file));
                var manifest = new CacheManifest(SchemaVersion, ParserCompatibilityVersion, WorldGraphCodec.ContractVersion,
                    SnapshotId(main), DateTime.UtcNow, before, length - HeaderBytes, expanded, digest, world.HistoricalFigures.Count, world.Events.Count);
                var header = JsonSerializer.SerializeToUtf8Bytes(manifest);
                if (header.Length > HeaderBytes - 12) throw new InvalidDataException("Manifest too large.");
                file.Position = 0;
                using var writer = new BinaryWriter(file, Encoding.UTF8, true);
                writer.Write(Encoding.ASCII.GetBytes(Magic)); writer.Write(header.Length); writer.Write(header); file.Flush(true);
            }
            if (!SameHistory(before, Sources(main, true), true)) throw new SourceChangedException("Sources changed while writing cache.");
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
        finally { File.Delete(temporary); }
    }

    public void Delete(string main) { File.Delete(CachePath(main)); EndPhase(main); }
    private static CacheManifest ReadHeader(FileStream file, string snapshotId)
    {
        if (file.Length < HeaderBytes || file.Length > MaxCacheBytes) throw new InvalidDataException("Invalid cache size.");
        using var reader = new BinaryReader(file, Encoding.UTF8, true);
        if (Encoding.ASCII.GetString(reader.ReadBytes(8)) != Magic) throw new InvalidDataException("Invalid cache header.");
        var length = reader.ReadInt32();
        if (length < 1 || length > HeaderBytes - 12) throw new InvalidDataException("Invalid manifest length.");
        var manifest = JsonSerializer.Deserialize<CacheManifest>(reader.ReadBytes(length)) ?? throw new InvalidDataException("Missing manifest.");
        if (manifest.Schema != SchemaVersion || manifest.Parser != ParserCompatibilityVersion || manifest.Contract != WorldGraphCodec.ContractVersion)
            throw new InvalidDataException("Cache version is incompatible. Rebuild required.");
        if (manifest.SnapshotId != snapshotId || manifest.PayloadBytes != file.Length - HeaderBytes
            || manifest.ExpandedBytes < 4 || manifest.ExpandedBytes > MaxExpandedBytes || manifest.Figures < 0 || manifest.Events < 0
            || manifest.Sha256 is not { Length: 64 } || manifest.Sources is not { Length: 6 }
            || manifest.Sources.Any(s => s == null)
            || !manifest.Sources.Select(s => s.Kind).Order().SequenceEqual(ExportName.Suffixes.Keys.Order())
            || manifest.Sources.Any(s => s.Length < 0 || s.Present && Historical.Contains(s.Kind) && s.Sha256 is not { Length: 64 }))
            throw new InvalidDataException("Invalid cache manifest.");
        file.Position = HeaderBytes; return manifest;
    }
    public static bool Recoverable(Exception ex) => ex is IOException or UnauthorizedAccessException or InvalidDataException
        or JsonException or ArgumentException or OverflowException or NotSupportedException;
}

internal sealed class LimitedReadStream(Stream inner, long maximum) : Stream
{
    public long BytesRead { get; private set; }
    public override int Read(byte[] buffer, int offset, int count)
    {
        var n = inner.Read(buffer, offset, (int)Math.Min(count, Math.Max(1, maximum - BytesRead + 1)));
        BytesRead += n;
        if (BytesRead > maximum) throw new InvalidDataException("Expanded cache exceeds limit.");
        return n;
    }
    public override int ReadByte() { var b = inner.ReadByte(); if (b >= 0 && ++BytesRead > maximum) throw new InvalidDataException("Expanded cache exceeds limit."); return b; }
    public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
    public override long Length => maximum; public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
    public override void Flush() { } public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
    public override void SetLength(long v) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
}

internal sealed class CountingWriteStream(Stream inner, long maximum) : Stream
{
    public long BytesWritten { get; private set; }
    public override void Write(byte[] buffer, int offset, int count) { BytesWritten += count; if (BytesWritten > maximum) throw new InvalidDataException("Expanded cache exceeds limit."); inner.Write(buffer, offset, count); }
    public override void Write(ReadOnlySpan<byte> buffer) { BytesWritten += buffer.Length; if (BytesWritten > maximum) throw new InvalidDataException("Expanded cache exceeds limit."); inner.Write(buffer); }
    public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
    public override long Length => BytesWritten; public override long Position { get => BytesWritten; set => throw new NotSupportedException(); }
    public override void Flush() => inner.Flush(); public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
    public override void SetLength(long v) => throw new NotSupportedException(); public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
}
