using LyricsDisplayer.Core.Protocol;
using Microsoft.Data.Sqlite;
using System.Security;
using System.Text.Json;

namespace LyricsDisplayer.Core.Library;

public sealed class LyricsLibrary : IDisposable
{
    private readonly Action<string, string, string>? _log;
    private readonly Action<int>? _beforeTimingBakeCommit;
    private readonly Action? _beforeLineCommit;
    private readonly HashSet<(string Source, string SourceTrackId)> _duplicateAssociations = [];
    private readonly HashSet<(string Source, string SourceTrackId)> _brokenAssociations = [];
    private readonly Dictionary<(string Source, string SourceTrackId), LocalTrackRecord> _brokenRecords = [];
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
        Action<int>? beforeTimingBakeCommit, Action? beforeLineCommit = null)
    {
        Paths = paths;
        _log = log;
        _beforeTimingBakeCommit = beforeTimingBakeCommit;
        _beforeLineCommit = beforeLineCommit;
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
        _brokenRecords.Clear();
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
                    {
                        _brokenAssociations.Add(association);
                        if (record is not null) _brokenRecords[association] = record;
                    }
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
                Error: "A broken portable record claims this source association.",
                Record: _brokenRecords.GetValueOrDefault((source, sourceTrackId)));
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
            if (indexed is null)
            {
                var broken = _brokenRecords.Values.FirstOrDefault(record =>
                    string.Equals(record.LocalTrackId, localTrackId, StringComparison.OrdinalIgnoreCase));
                return broken is null
                    ? new(LocalLyricsLookupStatus.NotFound)
                    : new(LocalLyricsLookupStatus.BrokenRecord,
                        Error: "The local lyrics record is currently invalid.", Record: broken);
            }
            var association = indexed.SourceAssociations.FirstOrDefault();
            return association is null
                ? new(LocalLyricsLookupStatus.BrokenRecord, Error: "Indexed record has no source association.", Record: indexed)
                : Load(indexed, association.Source, association.SourceTrackId, null);
        }
        catch (Exception exception) when (IsStorageException(exception))
        { return new(LocalLyricsLookupStatus.IndexUnavailable, Error: exception.Message); }
    }

    public IReadOnlyList<LocalTrackRecord> Search(string text) => _index?.Search(text) ?? [];

    public string ResolveLyricsPath(LocalTrackRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return ResolveRelative(record.LyricsRelativePath);
    }

    /// <summary>Loads the authoritative disk assets for editing, without requiring the LRC to parse as playable.</summary>
    public EditorAssetResult LoadForEditing(LocalTrackRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!LibraryAvailable) return new(EditorAssetStatus.Failed, Error: "The local lyrics library is unavailable.");
        try
        {
            var sidecarPath = ResolveRelative(record.SidecarRelativePath);
            if (!SidecarSerializer.TryRead(sidecarPath, out var sidecar, out var error))
                return new(EditorAssetStatus.Failed, Error: error);
            if (!string.Equals(sidecar!.LocalTrackId, record.LocalTrackId, StringComparison.OrdinalIgnoreCase))
                return new(EditorAssetStatus.Failed, Error: "The portable sidecar belongs to a different local track.");

            var directoryName = Path.GetFileName(Path.GetDirectoryName(sidecarPath));
            if (!string.Equals(directoryName, sidecar.LocalTrackId, StringComparison.OrdinalIgnoreCase))
                return new(EditorAssetStatus.Failed, Error: "The portable sidecar identity does not match its directory.");

            var authoritativeRecord = ToRecord(sidecar, directoryName!);
            var lyricsPath = ResolveRelative(authoritativeRecord.LyricsRelativePath);
            if (!File.Exists(lyricsPath)) return new(EditorAssetStatus.Missing, Error: "The authoritative local LRC is missing.");
            var snapshot = LrcFileSnapshot.Read(lyricsPath);
            var sidecarBytes = File.ReadAllBytes(sidecarPath);
            return new(EditorAssetStatus.Ready,
                new(authoritativeRecord, sidecar, lyricsPath, sidecarPath, snapshot.Content, snapshot.Hash,
                    LrcFileSnapshot.HashBytes(sidecarBytes), snapshot.Bytes.AsSpan().StartsWith(System.Text.Encoding.UTF8.Preamble)));
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return new(EditorAssetStatus.Failed, Error: exception.Message);
        }
    }

    /// <summary>Conditionally writes editor-owned LRC and/or portable user metadata without validating away user text.</summary>
    public EditorAssetResult SaveEditorAssets(EditorAssetSnapshot expected, string lrcContent,
        UserTrackMetadata userMetadata, bool overwriteExternalChanges = false)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(lrcContent);
        ArgumentNullException.ThrowIfNull(userMetadata);
        var normalisedMetadata = UserTrackMetadata.Normalise(userMetadata.Title, userMetadata.Artist);
        var writesLrc = !string.Equals(lrcContent, expected.LrcContent, StringComparison.Ordinal);
        var writesMetadata = normalisedMetadata != expected.Sidecar.UserMetadata;
        var currentResult = LoadForEditing(expected.Record);
        if (currentResult.Status == EditorAssetStatus.Missing && overwriteExternalChanges)
        {
            // Explicit overwrite means restore the editor's complete buffered document, even when only
            // sidecar metadata was dirty at the moment the external file disappeared.
            var sidecarRead = TryReadEditorSidecar(expected.Record);
            if (sidecarRead.Asset is null) return sidecarRead;
            currentResult = sidecarRead;
            writesLrc = true;
        }
        if (currentResult.Status != EditorAssetStatus.Ready || currentResult.Asset is null)
            return currentResult.Status == EditorAssetStatus.Missing
                ? new(EditorAssetStatus.Conflict, Error: "The authoritative LRC was deleted externally.")
                : currentResult;

        var current = currentResult.Asset;
        if (!overwriteExternalChanges &&
            (!string.Equals(current.LrcHash, expected.LrcHash, StringComparison.Ordinal) ||
             !string.Equals(current.SidecarHash, expected.SidecarHash, StringComparison.Ordinal)))
            return new(EditorAssetStatus.Conflict, current, "The LRC or portable sidecar changed externally.");

        var lrcWritten = false;
        try
        {
            if (writesLrc)
            {
                if (!File.Exists(current.LyricsPath))
                {
                    if (!overwriteExternalChanges)
                        return new(EditorAssetStatus.Conflict, current, "The authoritative LRC was deleted externally.");
                    AtomicFile.WriteNew(current.LyricsPath,
                        expected.HasUtf8Bom ? "\uFEFF" + lrcContent : lrcContent);
                }
                else
                {
                    var diskSnapshot = LrcFileSnapshot.Read(current.LyricsPath);
                    var encoded = diskSnapshot.Encode(lrcContent);
                    if (!AtomicFile.TryReplaceUnchanged(current.LyricsPath, encoded, current.LrcHash))
                        return new(EditorAssetStatus.Conflict,
                            LoadForEditing(current.Record).Asset, "The LRC changed while the editor was saving.");
                }
                lrcWritten = true;
            }

            if (writesMetadata)
            {
                var sidecarBytes = System.Text.Encoding.UTF8.GetBytes(
                    SidecarSerializer.Serialize(current.Sidecar with { UserMetadata = normalisedMetadata }));
                if (!AtomicFile.TryReplaceUnchanged(current.SidecarPath, sidecarBytes, current.SidecarHash))
                {
                    var refreshed = LoadForEditing(current.Record).Asset;
                    return new(lrcWritten ? EditorAssetStatus.PartialFailure : EditorAssetStatus.Conflict,
                        refreshed, "Portable metadata changed during save; any completed LRC write was preserved.");
                }
            }

            if (writesMetadata) ScanAndSynchronise();
            return LoadForEditing(current.Record) is { Status: EditorAssetStatus.Ready, Asset: { } saved }
                ? new(EditorAssetStatus.Saved, saved)
                : new(EditorAssetStatus.PartialFailure, LoadForEditing(current.Record).Asset,
                    "The assets were written, but could not be reloaded for confirmation.");
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            var refreshed = LoadForEditing(current.Record).Asset;
            return new(lrcWritten ? EditorAssetStatus.PartialFailure : EditorAssetStatus.Failed,
                refreshed, exception.Message);
        }
    }

    private EditorAssetResult TryReadEditorSidecar(LocalTrackRecord record)
    {
        try
        {
            var sidecarPath = ResolveRelative(record.SidecarRelativePath);
            if (!SidecarSerializer.TryRead(sidecarPath, out var sidecar, out var error))
                return new(EditorAssetStatus.Failed, Error: error);
            if (!string.Equals(sidecar!.LocalTrackId, record.LocalTrackId, StringComparison.OrdinalIgnoreCase))
                return new(EditorAssetStatus.Failed, Error: "The portable sidecar belongs to a different local track.");
            var directoryName = Path.GetFileName(Path.GetDirectoryName(sidecarPath));
            var authoritativeRecord = ToRecord(sidecar, directoryName!);
            var sidecarBytes = File.ReadAllBytes(sidecarPath);
            return new(EditorAssetStatus.Ready,
                new(authoritativeRecord, sidecar, ResolveRelative(authoritativeRecord.LyricsRelativePath), sidecarPath,
                    string.Empty, string.Empty, LrcFileSnapshot.HashBytes(sidecarBytes), false));
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return new(EditorAssetStatus.Failed, Error: exception.Message);
        }
    }

    public LocalLyricsLookupResult ReloadLocalLyrics(
        LocalTrackRecord record,
        string source,
        string sourceTrackId,
        SourceTrackMetadata? currentMetadata = null)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!LibraryAvailable) return new(LocalLyricsLookupStatus.LibraryUnavailable, Record: record);
        try
        {
            var sidecarPath = ResolveRelative(record.SidecarRelativePath);
            var expectedDirectoryName = Path.GetFileName(Path.GetDirectoryName(sidecarPath));
            if (!TryReadRecord(sidecarPath, expectedDirectoryName!, out var refreshedRecord,
                    out var sidecar, out var error))
                return new(LocalLyricsLookupStatus.BrokenRecord, Error: error, Record: record);

            var association = sidecar!.SourceAssociations.FirstOrDefault(item =>
                item.Source == source && item.SourceTrackId == sourceTrackId);
            if (association is null)
                return new(LocalLyricsLookupStatus.BrokenRecord,
                    Error: "The active playback association is no longer present in the local record.", Record: record);

            var document = LoadFromRecord(refreshedRecord!, sidecar, currentMetadata ?? association.Metadata);
            return document is null
                ? new(LocalLyricsLookupStatus.BrokenRecord, Error: "The local LRC is missing or unusable.", Record: record)
                : new(LocalLyricsLookupStatus.Found, document, Record: document.Record);
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return new(LocalLyricsLookupStatus.BrokenRecord, Error: exception.Message, Record: record);
        }
    }

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
            if (!parsed.Success || !parsed.HasTimedLyrics)
                return new(TimingAdjustmentStatus.StorageFailure, Error: parsed.Error);

            var savedBytes = AtomicFile.ReplacePair(lyricsPath, rewritten.Content!, sidecarPath, sidecarJson,
                _beforeTimingBakeCommit);
            var updated = document with
            {
                Sidecar = updatedSidecar, Lines = parsed.Lines,
                LrcContentHash = LrcFileSnapshot.HashBytes(savedBytes),
                TimestampOccurrences = parsed.TimestampOccurrences
            };
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

    public TimingAdjustmentResult AdjustLineTiming(CurrentLineTimingTarget target, long deltaMs)
    {
        ArgumentNullException.ThrowIfNull(target);
        var expected = target.Document;
        var lookup = Lookup(expected.Record.LocalTrackId);
        var document = lookup.Document;
        if (lookup.Status is LocalLyricsLookupStatus.LibraryUnavailable or LocalLyricsLookupStatus.IndexUnavailable)
            return new(TimingAdjustmentStatus.StorageFailure, Error: "The local lyrics library is currently unavailable.");
        if (lookup.Status != LocalLyricsLookupStatus.Found || document is null)
            return new(TimingAdjustmentStatus.FileChanged, Error: "The local LRC is missing or no longer usable. Reload lyrics before editing.");
        if (expected.LrcContentHash is null || expected.LrcContentHash != document.LrcContentHash ||
            expected.Record.LyricsRelativePath != document.Record.LyricsRelativePath ||
            expected.GlobalOffsetMs != document.GlobalOffsetMs)
            return new(TimingAdjustmentStatus.FileChanged, document,
                "The local LRC changed externally. Lyrics were reloaded; try again.");
        var index = target.LineIndex;
        if (index < 0 || index >= document.Lines.Count || index >= document.TimestampOccurrences.Count)
            return new(TimingAdjustmentStatus.NoCurrentLine, Error: "There is no current local lyric to edit.");
        if (!document.IsLrcWritable)
            return new(TimingAdjustmentStatus.StorageFailure, document, "The local LRC is not writable.");
        long adjusted;
        try { adjusted = checked(document.Lines[index].StartMs + deltaMs); }
        catch (OverflowException)
        { return new(TimingAdjustmentStatus.TimestampOverflow, Error: LrcTimestampRewriter.TimestampOverflowError); }
        if (adjusted < 0)
            return new(TimingAdjustmentStatus.NegativeTimestamp, Error: "Cannot move this lyric below 0 ms.");
        if (index > 0 && adjusted < document.Lines[index - 1].StartMs)
            return new(TimingAdjustmentStatus.PreviousLineBoundary, Error: "Cannot move this lyric past the previous lyric.");
        if (index + 1 < document.Lines.Count && adjusted > document.Lines[index + 1].StartMs)
            return new(TimingAdjustmentStatus.NextLineBoundary, Error: "Cannot move this lyric past the next lyric.");

        try
        {
            var path = ResolveRelative(document.Record.LyricsRelativePath);
            var original = LrcFileSnapshot.Read(path);
            if (original.Hash != expected.LrcContentHash)
                return ChangedLineEdit(expected.Record.LocalTrackId);
            var rewritten = LrcTimestampRewriter.RewriteOccurrence(original.Content,
                document.TimestampOccurrences[index], adjusted);
            if (!rewritten.Success)
                return new(TimingAdjustmentStatus.FileChanged, Error: rewritten.Error);
            var duration = document.Sidecar.SourceAssociations.Max(item => item.Metadata.DurationMs);
            var parsed = LrcCodec.Parse(rewritten.Content!, duration);
            if (!parsed.Success || !parsed.HasTimedLyrics)
                return new(TimingAdjustmentStatus.StorageFailure, Error: parsed.Error);
            var savedBytes = original.Encode(rewritten.Content!);
            if (!AtomicFile.TryReplaceUnchanged(path, savedBytes, expected.LrcContentHash, _beforeLineCommit))
                return ChangedLineEdit(expected.Record.LocalTrackId);
            var updated = document with
            {
                Lines = parsed.Lines, TimestampOccurrences = parsed.TimestampOccurrences,
                LrcContentHash = LrcFileSnapshot.HashBytes(savedBytes),
                EffectiveMetadata = expected.EffectiveMetadata
            };
            Log("Information", "Timing", $"Current line timing changed for LocalTrackId={document.Record.LocalTrackId}, line={index}, old={document.Lines[index].StartMs}, new={adjusted}.");
            return new(TimingAdjustmentStatus.Succeeded, updated);
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            Log("Error", "Timing", $"Current line timing save failed for LocalTrackId={expected.Record.LocalTrackId}: {exception.Message}");
            return new(TimingAdjustmentStatus.StorageFailure, document with
            {
                IsLrcWritable = LrcFileSnapshot.IsWritable(ResolveRelative(document.Record.LyricsRelativePath))
            }, exception.Message);
        }
    }

    private TimingAdjustmentResult ChangedLineEdit(string localTrackId) =>
        new(TimingAdjustmentStatus.FileChanged, Lookup(localTrackId).Document,
            "The local LRC changed externally. Lyrics were reloaded; try again.");

    public LyricsImportResult Import(TrackInfo track, LyricsSnapshotPayload lyrics, DateTimeOffset? importedAtUtc = null)
    {
        if (!string.Equals(track.SourceTrackId, lyrics.SourceTrackId, StringComparison.Ordinal))
            return new(LyricsImportStatus.Failed, Error: "Lyrics sourceTrackId does not match the authoritative track.");
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

        if (!lyrics.Available) return new(LyricsImportStatus.NotTimed);
        string lrcContent;
        if (lyrics.Timed)
        {
            if (lyrics.Lines.Count == 0 || lyrics.UntimedLines is { Count: > 0 })
                return new(LyricsImportStatus.NotTimed);
            lrcContent = LrcCodec.Serialize(lyrics.Lines);
        }
        else
        {
            var untimedLines = lyrics.UntimedLines ?? [];
            if (lyrics.Lines.Count != 0 || !untimedLines.Any(line => !string.IsNullOrWhiteSpace(line)))
                return new(LyricsImportStatus.NotTimed);
            lrcContent = LrcCodec.SerializeUntimed(untimedLines);
        }

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
            AtomicFile.WriteNew(Path.Combine(temporaryDirectory, "track.lrc"), lrcContent);
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
        Log("Information", "Library", $"Remote lyrics imported as local track {localTrackId}.");
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
            return new(LocalLyricsLookupStatus.BrokenRecord, Error: error, Record: indexed);
        }
        var association = authoritative!.SourceAssociations.FirstOrDefault(item =>
            item.Source == source && item.SourceTrackId == sourceTrackId);
        if (association is null)
            return new(LocalLyricsLookupStatus.BrokenRecord,
                Error: "Authoritative sidecar no longer contains the indexed association.", Record: indexed);
        var document = LoadFromRecord(record!, authoritative,
            currentMetadata ?? association.Metadata);
        if (document is null)
            return new(LocalLyricsLookupStatus.BrokenRecord, Error: "Local LRC is missing or unusable.", Record: record);
        Log("Information", "Library", $"Local association found and lyrics loaded for {source}:{sourceTrackId}.");
        return new(LocalLyricsLookupStatus.Found, document, Record: document.Record);
    }

    private LocalLyricsDocument? LoadFromRecord(LocalTrackRecord record, LyricsSidecar sidecar,
        SourceTrackMetadata currentMetadata)
    {
        try
        {
            var lyricsPath = ResolveRelative(record.LyricsRelativePath);
            if (!File.Exists(lyricsPath)) return null;
            var snapshot = LrcFileSnapshot.Read(lyricsPath);
            var parsed = LrcCodec.Parse(snapshot.Content, currentMetadata.DurationMs);
            if (!parsed.Success) return null;
            var stored = sidecar.SourceAssociations.First().Metadata;
            return new(record, sidecar, parsed.Lines,
                EffectiveTrackMetadata.From(stored, sidecar.UserMetadata, currentMetadata))
            {
                LrcContentHash = snapshot.Hash,
                TimestampOccurrences = parsed.TimestampOccurrences,
                IsLrcWritable = LrcFileSnapshot.IsWritable(lyricsPath),
                UntimedLines = parsed.UntimedLines
            };
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
        record = ToRecord(sidecar, directoryName);
        var lyricsPath = Path.Combine(Path.GetDirectoryName(sidecarPath)!, sidecar.Lyrics.File);
        if (!File.Exists(lyricsPath)) { error = "Referenced LRC is missing."; return false; }
        var duration = sidecar.SourceAssociations.Max(item => item.Metadata.DurationMs);
        var parsed = LrcCodec.Parse(File.ReadAllText(lyricsPath), duration);
        if (!parsed.Success)
        { error = parsed.Error!; return false; }
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
        exception is IOException or UnauthorizedAccessException or InvalidDataException or SqliteException or ArgumentException or SecurityException;

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
