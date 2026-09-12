using LyricsDisplayer.Core.Protocol;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace LyricsDisplayer.Core.Library;

public sealed class LyricsLibrary : IDisposable
{
    private readonly Action<string, string, string>? _log;
    private readonly Action<int>? _beforeTimingBakeCommit;
    private readonly HashSet<(string Source, string SourceTrackId)> _duplicateAssociations = [];
    private readonly HashSet<(string Source, string SourceTrackId)> _brokenAssociations = [];
    private LyricsLibraryIndex? _index;

    public LibraryPaths Paths { get; }
    public bool LibraryAvailable { get; private set; }
    public string IndexStatus { get; private set; } = "Not initialised";
    public LibraryScanResult? LastScan { get; private set; }

    public LyricsLibrary(LibraryPaths paths, Action<string, string, string>? log = null)
        : this(paths, log, null)
    {
    }

    internal LyricsLibrary(LibraryPaths paths, Action<string, string, string>? log,
        Action<int>? beforeTimingBakeCommit)
    {
        Paths = paths;
        _log = log;
        _beforeTimingBakeCommit = beforeTimingBakeCommit;
    }

    public LibraryScanResult Initialise()
    {
        if (Paths.SettingsWarning is not null) Log("Warning", "Library", Paths.SettingsWarning);
        try
        {
            _index = OpenIndexWithRecovery();
            IndexStatus = $"Open (schema {_index.GetSchemaVersion()})";
            Log("Information", "Library", $"SQLite index opened with schema {_index.GetSchemaVersion()}.");
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            IndexStatus = "Unavailable";
            Log("Error", "Library", $"SQLite index unavailable: {exception.Message}");
        }

        try
        {
            if (Paths.UsesConfiguredPath && IsUnreachableUncRoot(Paths.LibraryPath))
                throw new DirectoryNotFoundException("Configured UNC library is unavailable.");
            Directory.CreateDirectory(Path.Combine(Paths.LibraryPath, "tracks"));
            LibraryAvailable = true;
            Log("Information", "Library", $"Lyrics library initialised at {Paths.LibraryPath}.");
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            LibraryAvailable = false;
            LastScan = new(false, 0, 0, 0, "Configured lyrics library unavailable.");
            Log("Warning", "Library", $"Configured lyrics library unavailable: {exception.Message}");
            return LastScan;
        }

        LastScan = ScanAndSynchronise();
        return LastScan;
    }

    public LibraryScanResult ScanAndSynchronise()
    {
        if (!LibraryAvailable)
            return LastScan = new(false, 0, 0, 0, "Lyrics library is unavailable.");

        Log("Information", "Library", "Library scan started.");
        _brokenAssociations.Clear();
        var valid = new List<LocalTrackRecord>();
        var existingDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var invalid = 0;
        try
        {
            var tracksRoot = Path.Combine(Paths.LibraryPath, "tracks");
            foreach (var directory in Directory.EnumerateDirectories(tracksRoot))
            {
                var directoryName = Path.GetFileName(directory);
                var directoryRelative = Relative(Path.Combine("tracks", directoryName));
                existingDirectories.Add(directoryRelative);
                if (directoryName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                var sidecarPath = Path.Combine(directory, "track.lyrics.json");
                if (!File.Exists(sidecarPath))
                {
                    invalid++;
                    Log("Warning", "Library", $"Invalid local record {directoryName}: sidecar is missing.");
                    continue;
                }
                if (!TryReadRecord(sidecarPath, directoryName, out var record, out _, out var error))
                {
                    invalid++;
                    foreach (var association in ReadAssociationIdentities(sidecarPath))
                        _brokenAssociations.Add(association);
                    Log("Warning", "Library", $"Invalid local record {directoryName}: {error}");
                    continue;
                }
                valid.Add(record!);
            }
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            LibraryAvailable = false;
            Log("Warning", "Library", $"Library scan failed; SQLite was not pruned: {exception.Message}");
            return LastScan = new(false, valid.Count, invalid, 0, exception.Message);
        }

        _duplicateAssociations.Clear();
        foreach (var group in valid.SelectMany(record => record.SourceAssociations)
                     .GroupBy(item => (item.Source, item.SourceTrackId)))
        {
            if (group.Count() <= 1) continue;
            _duplicateAssociations.Add(group.Key);
            Log("Warning", "Library",
                $"Duplicate source association detected: {group.Key.Source}:{group.Key.SourceTrackId}.");
        }

        if (_index is not null)
        {
            try
            {
                _index.Synchronise(valid, existingDirectories, _duplicateAssociations);
                Log("Information", "Library",
                    $"Library scan complete; indexed={valid.Count}, invalid={invalid}, duplicates={_duplicateAssociations.Count}.");
            }
            catch (Exception exception) when (IsStorageException(exception))
            {
                IndexStatus = "Synchronisation failed";
                Log("Error", "Library", $"SQLite synchronisation failed: {exception.Message}");
                return LastScan = new(false, valid.Count, invalid, _duplicateAssociations.Count, exception.Message);
            }
        }
        return LastScan = new(true, valid.Count, invalid, _duplicateAssociations.Count);
    }

    public LocalLyricsLookupResult Lookup(
        string source,
        string sourceTrackId,
        SourceTrackMetadata? currentMetadata = null)
    {
        if (!LibraryAvailable) return new(LocalLyricsLookupStatus.LibraryUnavailable);
        if (_duplicateAssociations.Contains((source, sourceTrackId)))
            return new(LocalLyricsLookupStatus.DuplicateAssociation, Error: "Duplicate source association.");
        if (_brokenAssociations.Contains((source, sourceTrackId)))
            return new(LocalLyricsLookupStatus.BrokenRecord,
                Error: "A broken portable record claims this source association.");
        if (_index is null) return new(LocalLyricsLookupStatus.IndexUnavailable);

        LocalTrackRecord? indexed;
        try { indexed = _index.FindByAssociation(source, sourceTrackId); }
        catch (Exception exception) when (IsStorageException(exception))
        { return new(LocalLyricsLookupStatus.IndexUnavailable, Error: exception.Message); }
        if (indexed is null) return new(LocalLyricsLookupStatus.NotFound);
        return Load(indexed, source, sourceTrackId, currentMetadata);
    }

    public LocalLyricsLookupResult Lookup(string localTrackId)
    {
        if (!LibraryAvailable) return new(LocalLyricsLookupStatus.LibraryUnavailable);
        if (_index is null) return new(LocalLyricsLookupStatus.IndexUnavailable);
        try
        {
            var indexed = _index.FindByLocalTrackId(localTrackId);
            if (indexed is null) return new(LocalLyricsLookupStatus.NotFound);
            var association = indexed.SourceAssociations.FirstOrDefault();
            return association is null
                ? new(LocalLyricsLookupStatus.BrokenRecord, Error: "Indexed record has no source association.")
                : Load(indexed, association.Source, association.SourceTrackId, null);
        }
        catch (Exception exception) when (IsStorageException(exception))
        { return new(LocalLyricsLookupStatus.IndexUnavailable, Error: exception.Message); }
    }

    public IReadOnlyList<LocalTrackRecord> Search(string text) => _index?.Search(text) ?? [];

    public TimingAdjustmentResult SetGlobalOffset(string localTrackId, long globalOffsetMs)
    {
        var lookup = Lookup(localTrackId);
        if (lookup.Status != LocalLyricsLookupStatus.Found || lookup.Document is null)
            return new(TimingAdjustmentStatus.NoLocalLyrics, Error: lookup.Error ?? lookup.Status.ToString());

        var document = lookup.Document;
        var updatedSidecar = document.Sidecar with { Timing = new LyricsTiming(globalOffsetMs) };
        try
        {
            AtomicFile.Replace(ResolveRelative(document.Record.SidecarRelativePath),
                SidecarSerializer.Serialize(updatedSidecar));
            var updated = document with { Sidecar = updatedSidecar };
            Log("Information", "Timing",
                $"Timing offset changed for LocalTrackId={localTrackId}, old={document.GlobalOffsetMs}, new={globalOffsetMs}.");
            return new(TimingAdjustmentStatus.Succeeded, updated);
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            Log("Error", "Timing", $"Timing offset save failed for LocalTrackId={localTrackId}: {exception.Message}");
            return new(TimingAdjustmentStatus.StorageFailure, Error: exception.Message);
        }
    }

    public TimingAdjustmentResult BakeGlobalOffset(string localTrackId)
    {
        var lookup = Lookup(localTrackId);
        if (lookup.Status != LocalLyricsLookupStatus.Found || lookup.Document is null)
            return new(TimingAdjustmentStatus.NoLocalLyrics, Error: lookup.Error ?? lookup.Status.ToString());
        var document = lookup.Document;
        var offset = document.GlobalOffsetMs;
        if (offset == 0) return new(TimingAdjustmentStatus.NothingToBake, document);

        var lyricsPath = ResolveRelative(document.Record.LyricsRelativePath);
        var sidecarPath = ResolveRelative(document.Record.SidecarRelativePath);
        try
        {
            var originalLrc = File.ReadAllText(lyricsPath, System.Text.Encoding.UTF8);
            var rewritten = LrcTimestampRewriter.Rewrite(originalLrc, offset);
            if (!rewritten.Success)
            {
                var status = rewritten.ContainsNegativeTimestamp
                    ? TimingAdjustmentStatus.NegativeTimestamp
                    : TimingAdjustmentStatus.TimestampOverflow;
                return new(status, Error: rewritten.Error);
            }

            var updatedSidecar = document.Sidecar with { Timing = new LyricsTiming(0) };
            var sidecarJson = SidecarSerializer.Serialize(updatedSidecar);
            var duration = updatedSidecar.SourceAssociations.Max(item => item.Metadata.DurationMs);
            var parsed = LrcCodec.Parse(rewritten.Content!, duration);
            if (!parsed.Success)
                return new(TimingAdjustmentStatus.StorageFailure, Error: parsed.Error);

            AtomicFile.ReplacePair(lyricsPath, rewritten.Content!, sidecarPath, sidecarJson,
                _beforeTimingBakeCommit);
            var updated = document with { Sidecar = updatedSidecar, Lines = parsed.Lines };
            Log("Information", "Timing",
                $"Timing offset baked into LRC for LocalTrackId={localTrackId}, offsetMs={offset}.");
            return new(TimingAdjustmentStatus.Succeeded, updated);
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            Log("Error", "Timing", $"Timing offset bake failed for LocalTrackId={localTrackId}: {exception.Message}");
            return new(TimingAdjustmentStatus.StorageFailure, Error: exception.Message);
        }
    }

    public LyricsImportResult Import(TrackInfo track, LyricsSnapshotPayload lyrics, DateTimeOffset? importedAtUtc = null)
    {
        if (!string.Equals(track.SourceTrackId, lyrics.SourceTrackId, StringComparison.Ordinal))
            return new(LyricsImportStatus.Failed, Error: "Lyrics sourceTrackId does not match the authoritative track.");
        if (!lyrics.Available || !lyrics.Timed || lyrics.Lines.Count == 0)
            return new(LyricsImportStatus.NotTimed);
        if (!LibraryAvailable) return new(LyricsImportStatus.LibraryUnavailable);
        if (_index is null) return new(LyricsImportStatus.IndexUnavailable);

        var lookup = Lookup(ProtocolConstants.YouTubeMusicSource, track.SourceTrackId,
            new(track.Title, track.Artist, track.Album, track.DurationMs));
        if (lookup.Status == LocalLyricsLookupStatus.Found)
            return new(LyricsImportStatus.AlreadyLocal, lookup.Document, lookup.Document!.Record.LocalTrackId);
        if (lookup.Status is LocalLyricsLookupStatus.BrokenRecord or LocalLyricsLookupStatus.DuplicateAssociation)
            return new(LyricsImportStatus.BlockedByBrokenRecord, Error: lookup.Error);
        if (lookup.Status != LocalLyricsLookupStatus.NotFound)
            return new(lookup.Status == LocalLyricsLookupStatus.LibraryUnavailable
                ? LyricsImportStatus.LibraryUnavailable : LyricsImportStatus.IndexUnavailable, Error: lookup.Error);

        var localTrackId = Guid.NewGuid().ToString("D");
        var association = new SourceTrackAssociation(ProtocolConstants.YouTubeMusicSource, track.SourceTrackId,
            new(track.Title, track.Artist, track.Album, track.DurationMs));
        var sidecar = new LyricsSidecar(SidecarSerializer.CurrentSchemaVersion, localTrackId, [association],
            UserTrackMetadata.Normalise(null, null),
            new("track.lrc", lyrics.Source, lyrics.Attribution, (importedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime()));
        var tracksRoot = Path.Combine(Paths.LibraryPath, "tracks");
        var finalDirectory = Path.Combine(tracksRoot, localTrackId);
        var temporaryDirectory = Path.Combine(tracksRoot, $"{localTrackId}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(temporaryDirectory);
            AtomicFile.WriteNew(Path.Combine(temporaryDirectory, "track.lrc"), LrcCodec.Serialize(lyrics.Lines));
            AtomicFile.WriteNew(Path.Combine(temporaryDirectory, "track.lyrics.json"), SidecarSerializer.Serialize(sidecar));
            Directory.Move(temporaryDirectory, finalDirectory);
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            TryDeleteOwnTemporaryDirectory(temporaryDirectory);
            Log("Error", "Library", $"Remote lyrics import failed for {track.SourceTrackId}: {exception.Message}");
            return new(LyricsImportStatus.Failed, LocalTrackId: localTrackId, Error: exception.Message);
        }

        var record = ToRecord(sidecar, localTrackId);
        try
        {
            _index.Upsert(record);
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            Log("Error", "Library",
                $"Remote lyrics files persisted for {track.SourceTrackId}, but SQLite indexing failed: {exception.Message}");
            return new(LyricsImportStatus.FilesPersistedIndexFailed,
                LoadFromRecord(record, sidecar, association.Metadata), localTrackId, exception.Message);
        }

        var document = LoadFromRecord(record, sidecar, association.Metadata);
        Log("Information", "Library", $"Remote timed lyrics imported as local track {localTrackId}.");
        return new(LyricsImportStatus.Imported, document, localTrackId);
    }

    private LocalLyricsLookupResult Load(LocalTrackRecord indexed, string source, string sourceTrackId,
        SourceTrackMetadata? currentMetadata)
    {
        var sidecarPath = ResolveRelative(indexed.SidecarRelativePath);
        var expectedDirectoryName = Path.GetFileName(Path.GetDirectoryName(sidecarPath));
        if (!TryReadRecord(sidecarPath, expectedDirectoryName!, out var record, out var authoritative, out var error))
        {
            Log("Warning", "Library", $"Local association is broken for {source}:{sourceTrackId}: {error}");
            return new(LocalLyricsLookupStatus.BrokenRecord, Error: error);
        }
        var association = authoritative!.SourceAssociations.FirstOrDefault(item =>
            item.Source == source && item.SourceTrackId == sourceTrackId);
        if (association is null)
            return new(LocalLyricsLookupStatus.BrokenRecord, Error: "Authoritative sidecar no longer contains the indexed association.");
        var document = LoadFromRecord(record!, authoritative,
            currentMetadata ?? association.Metadata);
        if (document is null)
            return new(LocalLyricsLookupStatus.BrokenRecord, Error: "Local LRC is missing or unusable.");
        Log("Information", "Library", $"Local association found and lyrics loaded for {source}:{sourceTrackId}.");
        return new(LocalLyricsLookupStatus.Found, document);
    }

    private LocalLyricsDocument? LoadFromRecord(LocalTrackRecord record, LyricsSidecar sidecar,
        SourceTrackMetadata currentMetadata)
    {
        try
        {
            var lyricsPath = ResolveRelative(record.LyricsRelativePath);
            if (!File.Exists(lyricsPath)) return null;
            var parsed = LrcCodec.Parse(File.ReadAllText(lyricsPath), currentMetadata.DurationMs);
            if (!parsed.Success) return null;
            var stored = sidecar.SourceAssociations.First().Metadata;
            return new(record, sidecar, parsed.Lines,
                EffectiveTrackMetadata.From(stored, sidecar.UserMetadata, currentMetadata));
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            Log("Warning", "Library", $"Invalid LRC for local track {record.LocalTrackId}: {exception.Message}");
            return null;
        }
    }

    private bool TryReadRecord(string sidecarPath, string directoryName, out LocalTrackRecord? record,
        out LyricsSidecar? sidecar, out string error)
    {
        record = null;
        if (!SidecarSerializer.TryRead(sidecarPath, out sidecar, out error)) return false;
        if (!string.Equals(sidecar!.LocalTrackId, directoryName, StringComparison.OrdinalIgnoreCase))
        { error = "localTrackId does not match its directory."; return false; }
        var lyricsPath = Path.Combine(Path.GetDirectoryName(sidecarPath)!, sidecar.Lyrics.File);
        if (!File.Exists(lyricsPath)) { error = "Referenced LRC is missing."; return false; }
        var duration = sidecar.SourceAssociations.Max(item => item.Metadata.DurationMs);
        var parsed = LrcCodec.Parse(File.ReadAllText(lyricsPath), duration);
        if (!parsed.Success) { error = parsed.Error!; return false; }
        record = ToRecord(sidecar, directoryName);
        return true;
    }

    private LocalTrackRecord ToRecord(LyricsSidecar sidecar, string directoryName)
    {
        var directory = Path.Combine("tracks", directoryName);
        return new(sidecar.LocalTrackId, sidecar.UserMetadata,
            Relative(Path.Combine(directory, sidecar.Lyrics.File)),
            Relative(Path.Combine(directory, "track.lyrics.json")),
            sidecar.Lyrics.Source, sidecar.Lyrics.Attribution, sidecar.SourceAssociations);
    }

    private LyricsLibraryIndex OpenIndexWithRecovery()
    {
        try { return new LyricsLibraryIndex(Paths.IndexPath); }
        catch (SqliteException) when (File.Exists(Paths.IndexPath))
        {
            var recovered = $"{Paths.IndexPath}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
            File.Move(Paths.IndexPath, recovered);
            Log("Warning", "Library", $"Corrupt SQLite index preserved as {Path.GetFileName(recovered)}; rebuilding.");
            return new LyricsLibraryIndex(Paths.IndexPath);
        }
    }

    private string ResolveRelative(string relative)
    {
        var root = Path.GetFullPath(Paths.LibraryPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Library path escapes its root.");
        return full;
    }

    private static string Relative(string path) => path.Replace(Path.DirectorySeparatorChar, '/');

    private static bool IsUnreachableUncRoot(string path) =>
        path.StartsWith("\\\\", StringComparison.Ordinal) && !Directory.Exists(path);

    private static IReadOnlyList<(string Source, string SourceTrackId)> ReadAssociationIdentities(string path)
    {
        var identities = new List<(string, string)>();
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("sourceAssociations", out var associations) ||
                associations.ValueKind != JsonValueKind.Array)
                return identities;
            foreach (var association in associations.EnumerateArray())
            {
                if (!association.TryGetProperty("source", out var sourceElement) ||
                    !association.TryGetProperty("sourceTrackId", out var idElement) ||
                    sourceElement.ValueKind != JsonValueKind.String || idElement.ValueKind != JsonValueKind.String)
                    continue;
                var source = sourceElement.GetString();
                var sourceTrackId = idElement.GetString();
                if (!string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(sourceTrackId))
                    identities.Add((source, sourceTrackId));
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // Identity cannot be recovered conservatively from this sidecar.
        }
        return identities;
    }

    private static bool IsStorageException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidDataException or SqliteException or ArgumentException;

    private static void TryDeleteOwnTemporaryDirectory(string path)
    {
        try
        {
            if (Path.GetFileName(path).EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) && Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception) { }
    }

    private void Log(string level, string category, string message) => _log?.Invoke(level, category, message);

    public void Dispose() => _index?.Dispose();
}
