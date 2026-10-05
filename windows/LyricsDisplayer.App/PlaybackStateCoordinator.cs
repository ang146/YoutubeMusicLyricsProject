using System.IO;
using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Core.Playback;
using LyricsDisplayer.Core.Protocol;
using LyricsDisplayer.Core.Timeline;

namespace LyricsDisplayer;

public sealed class PlaybackStateCoordinator
{
    private sealed record EditorLyricsPreview(
        LocalTrackRecord Record,
        IReadOnlyList<LyricsLine> Lines,
        IReadOnlyList<string> UntimedLines,
        LyricsTimeline Timeline);

    private readonly SnapshotStateTracker _stateTracker;
    private readonly PlaybackClock _playbackClock;
    private long _trackSequenceFloor;
    private long _lyricsSequence = -1;
    private readonly LyricsLibrary? _library;
    private readonly Action<string, string, string>? _log;
    private LyricsTimeline _lyricsTimeline = LyricsTimeline.Empty;
    private LocalTrackRecord? _activeLocalLyricsRecord;
    private EditorLyricsPreview? _editorLyricsPreview;
    private LyricsSnapshotPayload? _cachedEditorPreviewPayload;
    private string? _cachedEditorPreviewSourceTrackId;
    private bool _hasActiveLocalAssociation;
    private bool _externalLrcUsable;

    public LyricsSnapshotMessage? CurrentLyrics { get; private set; }
    public LyricsSnapshotMessage? CurrentRawLyricsSnapshot { get; private set; }
    public LocalLyricsDocument? CurrentLocalLyrics { get; private set; }
    public LocalTrackRecord? ActiveLocalLyricsRecord => _activeLocalLyricsRecord;
    public EffectiveTrackMetadata? EffectiveMetadata
    {
        get
        {
            if (Current is not { } playback) return null;

            var track = playback.Payload.Track;
            var currentSource = new SourceTrackMetadata(
                track.Title, track.Artist, track.Album, track.DurationMs);
            var storedSource = _activeLocalLyricsRecord?.SourceAssociations.FirstOrDefault(association =>
                association.Source == playback.Envelope.Source &&
                association.SourceTrackId == track.SourceTrackId)?.Metadata ?? currentSource;
            var userMetadata = _activeLocalLyricsRecord?.UserMetadata ?? new UserTrackMetadata(null, null);
            return EffectiveTrackMetadata.From(storedSource, userMetadata, currentSource);
        }
    }
    public bool IsCurrentLocalLrcUsable => CurrentLocalLyrics is not null && _externalLrcUsable;
    public bool IsCurrentLocalLrcMissing { get; private set; }
    public string LyricsLoadedFrom { get; private set; } = "Pending / unknown";
    public string LocalAssociationStatus { get; private set; } = "Not checked";
    public event Action? LocalMetadataChanged;

    public PlaybackStateCoordinator(SnapshotStateTracker stateTracker, PlaybackClock playbackClock,
        LyricsLibrary? library = null, Action<string, string, string>? log = null)
    {
        _stateTracker = stateTracker;
        _playbackClock = playbackClock;
        _library = library;
        _log = log;
    }

    public PlaybackSnapshotMessage? Current => _stateTracker.Current;

    public bool HasClockState => _playbackClock.HasState;

    public bool IsEditorPreviewActive => ActiveEditorLyricsPreview is not null;

    /// <summary>The lyrics currently presented by the runtime, including a matching editor preview.</summary>
    public LyricsSnapshotPayload? PresentationLyrics
    {
        get
        {
            if (ActiveEditorLyricsPreview is not { } preview || Current is not { } current)
                return CurrentLyrics?.Payload;
            var sourceTrackId = current.Payload.Track.SourceTrackId;
            if (_cachedEditorPreviewPayload is null ||
                !string.Equals(_cachedEditorPreviewSourceTrackId, sourceTrackId, StringComparison.Ordinal))
            {
                _cachedEditorPreviewSourceTrackId = sourceTrackId;
                _cachedEditorPreviewPayload = new(sourceTrackId, true, preview.Lines.Count > 0,
                    preview.Record.LyricsSource, preview.Lines, preview.Record.Attribution,
                    preview.Lines.Count == 0 ? preview.UntimedLines : null);
            }
            return _cachedEditorPreviewPayload;
        }
    }

    public IReadOnlyList<LyricsLine> GetTimelineOrderedLines() =>
        ActiveEditorLyricsPreview?.Timeline.OrderedLines ?? _lyricsTimeline.OrderedLines;

    public long GlobalOffsetMs => CurrentLocalLyrics?.GlobalOffsetMs ?? 0;

    public bool CanAdjustTiming => IsCurrentLocalLrcUsable && CurrentLocalLyrics!.IsTimed && _library is not null;

    public bool CanAdjustCurrentLineTiming => !IsEditorPreviewActive && _library is not null &&
        IsCurrentLocalLrcUsable && CurrentLocalLyrics is { IsLrcWritable: true, LrcContentHash: not null } &&
        GetTimelinePosition().CurrentIndex is not null;

    public CurrentLineTimingTarget? CaptureCurrentLineTimingTarget()
    {
        if (IsEditorPreviewActive) return null;
        var document = CurrentLocalLyrics;
        var index = GetTimelinePosition().CurrentIndex;
        return document is { IsLrcWritable: true, LrcContentHash: not null } &&
               _library is not null && _externalLrcUsable && index is not null
            ? new(document, index.Value) : null;
    }

    public TimingAdjustmentResult AdjustCurrentLineTiming(long deltaMs)
    {
        var target = CaptureCurrentLineTimingTarget();
        return target is null
            ? new(TimingAdjustmentStatus.NoCurrentLine, Error: "There is no writable current local lyric to edit.")
            : AdjustCurrentLineTiming(target, deltaMs);
    }

    public TimingAdjustmentResult AdjustCurrentLineTiming(CurrentLineTimingTarget target, long deltaMs)
    {
        ArgumentNullException.ThrowIfNull(target);
        var current = CurrentLocalLyrics;
        if (_library is null || current is null || !current.IsTimed ||
            current.Record.LocalTrackId != target.Document.Record.LocalTrackId ||
            current.Record.LyricsRelativePath != target.Document.Record.LyricsRelativePath ||
            current.LrcContentHash != target.Document.LrcContentHash)
            return new(TimingAdjustmentStatus.TrackChanged,
                Error: "The local lyrics asset changed before the line edit could be saved.");

        var result = _library.AdjustLineTiming(target, deltaMs);
        if (CurrentLocalLyrics?.Record.LocalTrackId == target.Document.Record.LocalTrackId &&
            CurrentLocalLyrics.Record.LyricsRelativePath == target.Document.Record.LyricsRelativePath &&
            CurrentLocalLyrics.LrcContentHash == target.Document.LrcContentHash)
        {
            if (result.Succeeded || result.Status == TimingAdjustmentStatus.FileChanged)
            {
                if (result.Document is not null)
                    SetLocalLyrics(result.Document with { EffectiveMetadata = current.EffectiveMetadata },
                        CurrentLyrics?.Envelope ?? Current!.Envelope);
                else
                {
                    CurrentLocalLyrics = null;
                    CurrentLyrics = null;
                    _lyricsTimeline = LyricsTimeline.Empty;
                    LocalAssociationStatus = "Changed / unusable";
                    LyricsLoadedFrom = "Local Library (changed / unusable)";
                }
            }
            else if (result.Status == TimingAdjustmentStatus.StorageFailure &&
                     result.Document is { IsLrcWritable: false })
                CurrentLocalLyrics = current with { IsLrcWritable = false };
        }
        return result;
    }

    public long GetLocalPositionMs() => _playbackClock.GetPositionMs();

    /// <summary>Publishes saved portable metadata to the live document only when it is still the active editor track.</summary>
    public bool ApplySavedEditorMetadata(LocalTrackRecord record, LyricsSidecar sidecar)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(sidecar);
        if (!string.Equals(record.LocalTrackId, sidecar.LocalTrackId, StringComparison.OrdinalIgnoreCase) ||
            CurrentLocalLyrics is not { } current || Current is not { } playback ||
            !string.Equals(current.Record.LocalTrackId, record.LocalTrackId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(_activeLocalLyricsRecord?.LocalTrackId, record.LocalTrackId,
                StringComparison.OrdinalIgnoreCase))
            return false;

        var track = playback.Payload.Track;
        var association = sidecar.SourceAssociations.FirstOrDefault(item =>
            item.Source == playback.Envelope.Source && item.SourceTrackId == track.SourceTrackId);
        if (association is null) return false;

        var liveSource = new SourceTrackMetadata(track.Title, track.Artist, track.Album, track.DurationMs);
        var updatedRecord = record with
        {
            UserMetadata = sidecar.UserMetadata,
            SourceAssociations = sidecar.SourceAssociations,
            LyricsSource = sidecar.Lyrics.Source,
            Attribution = sidecar.Lyrics.Attribution
        };
        CurrentLocalLyrics = current with
        {
            Record = updatedRecord,
            Sidecar = sidecar,
            EffectiveMetadata = EffectiveTrackMetadata.From(association.Metadata, sidecar.UserMetadata, liveSource)
        };
        _activeLocalLyricsRecord = updatedRecord;
        LocalMetadataChanged?.Invoke();
        return true;
    }

    public LyricsTimelinePosition GetTimelinePosition() =>
        (ActiveEditorLyricsPreview?.Timeline ?? _lyricsTimeline).Evaluate(LyricsTimingAdjustment.GetEvaluationPosition(
            _playbackClock.GetPositionMs(), GlobalOffsetMs));

    /// <summary>Temporarily publishes the editor's in-memory document to playback presentation.</summary>
    public void SetEditorPreview(LocalTrackRecord record, IReadOnlyList<LyricsLine> lines,
        IReadOnlyList<string> untimedLines)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(untimedLines);
        var lineSnapshot = lines.ToArray();
        var untimedSnapshot = untimedLines.ToArray();
        _editorLyricsPreview = new(record, lineSnapshot, untimedSnapshot, new LyricsTimeline(lineSnapshot));
        _cachedEditorPreviewPayload = null;
        _cachedEditorPreviewSourceTrackId = null;
    }

    public void ClearEditorPreview(string localTrackId)
    {
        if (_editorLyricsPreview is { } preview &&
            string.Equals(preview.Record.LocalTrackId, localTrackId, StringComparison.Ordinal))
        {
            _editorLyricsPreview = null;
            _cachedEditorPreviewPayload = null;
            _cachedEditorPreviewSourceTrackId = null;
        }
    }

    private EditorLyricsPreview? ActiveEditorLyricsPreview
    {
        get
        {
            if (_editorLyricsPreview is not { } preview || Current is not { } current ||
                !string.Equals(_activeLocalLyricsRecord?.LocalTrackId, preview.Record.LocalTrackId,
                    StringComparison.Ordinal))
                return null;

            return preview.Record.SourceAssociations.Any(association =>
                string.Equals(association.Source, current.Envelope.Source, StringComparison.Ordinal) &&
                string.Equals(association.SourceTrackId, current.Payload.Track.SourceTrackId,
                    StringComparison.Ordinal))
                ? preview
                : null;
        }
    }

    public TimingAdjustmentResult AdjustTiming(long deltaMs)
    {
        var target = CurrentLocalLyrics;
        if (target is null || !target.IsTimed || _library is null || !_externalLrcUsable)
            return new(TimingAdjustmentStatus.NoLocalLyrics);
        if (!LyricsTimingAdjustment.TryAdjustOffset(target.GlobalOffsetMs, deltaMs, out var adjusted))
            return new(TimingAdjustmentStatus.OffsetOverflow);

        var result = _library.SetGlobalOffset(target.Record.LocalTrackId, adjusted);
        // A sidecar-only change must keep the loaded LRC source identity aligned with the unchanged timeline.
        if (result.Succeeded && result.Document is not null &&
            CurrentLocalLyrics?.Record.LocalTrackId == target.Record.LocalTrackId)
            CurrentLocalLyrics = target with { Sidecar = result.Document.Sidecar };
        return result;
    }

    public TimingAdjustmentResult ResetTiming()
    {
        var target = CurrentLocalLyrics;
        if (target is null || !target.IsTimed || _library is null || !_externalLrcUsable)
            return new(TimingAdjustmentStatus.NoLocalLyrics);
        if (target.GlobalOffsetMs == 0)
            return new(TimingAdjustmentStatus.Succeeded, target);
        var result = _library.SetGlobalOffset(target.Record.LocalTrackId, 0);
        if (result.Succeeded && result.Document is not null &&
            CurrentLocalLyrics?.Record.LocalTrackId == target.Record.LocalTrackId)
            CurrentLocalLyrics = target with { Sidecar = result.Document.Sidecar };
        return result;
    }

    public TimingAdjustmentTarget? CaptureTimingAdjustmentTarget() =>
        CurrentLocalLyrics is not { IsTimed: true } || !IsCurrentLocalLrcUsable
            ? null
            : new(CurrentLocalLyrics.Record.LocalTrackId, CurrentLocalLyrics.GlobalOffsetMs);

    public TimingAdjustmentResult BakeTiming(TimingAdjustmentTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var current = CurrentLocalLyrics;
        if (current is null || !current.IsTimed || _library is null || !_externalLrcUsable)
            return new(TimingAdjustmentStatus.NoLocalLyrics);
        if (current.Record.LocalTrackId != target.LocalTrackId || current.GlobalOffsetMs != target.GlobalOffsetMs)
            return new(TimingAdjustmentStatus.TrackChanged,
                Error: "The current lyrics track or timing offset changed before Bake was confirmed.");

        var result = _library.BakeGlobalOffset(target.LocalTrackId);
        if (result.Succeeded && result.Document is not null &&
            CurrentLocalLyrics?.Record.LocalTrackId == target.LocalTrackId)
            SetLocalLyrics(result.Document with { EffectiveMetadata = current.EffectiveMetadata },
                CurrentLyrics?.Envelope ?? Current!.Envelope);
        return result;
    }

    public SnapshotDecision Apply(PlaybackSnapshotMessage snapshot)
    {
        var previous = Current;
        var decision = _stateTracker.Apply(snapshot);
        if (decision is SnapshotDecision.Accepted or SnapshotDecision.AcceptedNewSession)
        {
            if (previous is null ||
                !string.Equals(previous.Envelope.Source, snapshot.Envelope.Source, StringComparison.Ordinal) ||
                !string.Equals(previous.Envelope.SourceSessionId, snapshot.Envelope.SourceSessionId,
                    StringComparison.Ordinal) ||
                !string.Equals(previous.Payload.Track.SourceTrackId, snapshot.Payload.Track.SourceTrackId,
                    StringComparison.Ordinal))
                CurrentRawLyricsSnapshot = null;

            if (previous is null || decision == SnapshotDecision.AcceptedNewSession ||
                previous.Payload.Track.SourceTrackId != snapshot.Payload.Track.SourceTrackId)
            {
                CurrentLyrics = null;
                _activeLocalLyricsRecord = null;
                _hasActiveLocalAssociation = false;
                _externalLrcUsable = false;
                IsCurrentLocalLrcMissing = false;
                CurrentLocalLyrics = null;
                _lyricsTimeline = LyricsTimeline.Empty;
                LyricsLoadedFrom = "Pending / unknown";
                LocalAssociationStatus = "Not checked";
                _lyricsSequence = -1;
                // On initial connection, a retained lyrics result can predate the latest playback.
                // Within a known session, reject results from an earlier visit to this track.
                _trackSequenceFloor = previous is null || decision == SnapshotDecision.AcceptedNewSession
                    ? 0 : snapshot.Envelope.Sequence;
                LoadLocalLyrics(snapshot);
            }
            _playbackClock.Rebase(
                snapshot.Payload.Playback.PositionMs,
                snapshot.Payload.Track.DurationMs,
                snapshot.Payload.Playback.Playing,
                snapshot.Payload.Playback.PlaybackRate);
        }

        return decision;
    }

    public void SourceDisconnected() => _playbackClock.Freeze();

    public bool ApplyLyrics(LyricsSnapshotMessage snapshot) =>
        ApplyLyricsDetailed(snapshot) != LyricsApplyDecision.Rejected;

    public LyricsApplyDecision ApplyLyricsDetailed(LyricsSnapshotMessage snapshot)
    {
        var current = Current;
        if (current is null || snapshot.Envelope.Source != current.Envelope.Source ||
            snapshot.Envelope.SourceSessionId != current.Envelope.SourceSessionId ||
            snapshot.Payload.SourceTrackId != current.Payload.Track.SourceTrackId ||
            snapshot.Envelope.Sequence < _trackSequenceFloor ||
            snapshot.Envelope.Sequence <= _lyricsSequence)
        {
            return LyricsApplyDecision.Rejected;
        }
        CurrentRawLyricsSnapshot = snapshot;
        _lyricsSequence = snapshot.Envelope.Sequence;

        if (_hasActiveLocalAssociation)
        {
            _log?.Invoke("Information", "Library", CurrentLocalLyrics is null
                ? "Remote lyrics ignored because the current source has a local lyrics association that is unavailable or broken."
                : $"Remote lyrics ignored because local copy {CurrentLocalLyrics.Record.LocalTrackId} is authoritative.");
            return LyricsApplyDecision.IgnoredBecauseLocal;
        }

        CurrentLyrics = snapshot;
        SetTimeline(snapshot.Payload);
        LyricsLoadedFrom = "YouTube Music (runtime)";
        if (_library is null || !snapshot.Payload.Available ||
            (snapshot.Payload.Timed
                ? snapshot.Payload.Lines.Count == 0
                : snapshot.Payload.UntimedLines is not { Count: > 0 }))
            return LyricsApplyDecision.AcceptedRemote;

        var imported = _library.Import(current.Payload.Track, snapshot.Payload);
        LocalAssociationStatus = imported.Status.ToString();
        if (imported.Document is not null && imported.Status is LyricsImportStatus.Imported or LyricsImportStatus.AlreadyLocal)
        {
            SetLocalLyrics(imported.Document, snapshot.Envelope);
            return imported.Status == LyricsImportStatus.Imported
                ? LyricsApplyDecision.ImportedAsLocal : LyricsApplyDecision.IgnoredBecauseLocal;
        }
        return LyricsApplyDecision.AcceptedRemote;
    }

    private void LoadLocalLyrics(PlaybackSnapshotMessage snapshot)
    {
        if (_library is null) return;
        var track = snapshot.Payload.Track;
        var lookup = _library.Lookup(snapshot.Envelope.Source, track.SourceTrackId,
            new SourceTrackMetadata(track.Title, track.Artist, track.Album, track.DurationMs));
        LocalAssociationStatus = lookup.Status.ToString();
        _activeLocalLyricsRecord = lookup.Record ?? lookup.Document?.Record;
        _hasActiveLocalAssociation = lookup.Status is not (LocalLyricsLookupStatus.NotFound or
            LocalLyricsLookupStatus.LibraryUnavailable or LocalLyricsLookupStatus.IndexUnavailable);
        if (lookup.Document is not null) SetLocalLyrics(lookup.Document, snapshot.Envelope);
        else if (_hasActiveLocalAssociation)
        {
            _externalLrcUsable = false;
            CurrentLyrics = null;
            _lyricsTimeline = LyricsTimeline.Empty;
            LyricsLoadedFrom = "Local Library (unavailable)";
        }
    }

    private void SetLocalLyrics(LocalLyricsDocument document, EnvelopeMetadata envelope)
    {
        CurrentLocalLyrics = document;
        _activeLocalLyricsRecord = document.Record;
        _hasActiveLocalAssociation = true;
        _externalLrcUsable = true;
        IsCurrentLocalLrcMissing = false;
        LyricsLoadedFrom = "Local Library";
        CurrentLyrics = new LyricsSnapshotMessage(
            envelope with { MessageType = ProtocolConstants.LyricsSnapshot },
            new LyricsSnapshotPayload(Current!.Payload.Track.SourceTrackId, true, document.IsTimed,
                document.Sidecar.Lyrics.Source, document.Lines, document.Sidecar.Lyrics.Attribution,
                document.IsTimed ? null : document.UntimedLines),
            "");
        _lyricsTimeline = new LyricsTimeline(document.Lines);
    }

    public ExternalLocalLyricsUpdate ReloadExternalLocalLyrics(string localTrackId, string observedFingerprint)
    {
        if (_activeLocalLyricsRecord?.LocalTrackId != localTrackId || Current is null)
            return ExternalLocalLyricsUpdate.Stale;
        if (_library is null) return ExternalLocalLyricsUpdate.Unavailable;

        var record = _activeLocalLyricsRecord;
        var currentTrack = Current.Payload.Track;
        var lookup = _library.ReloadLocalLyrics(record,
            Current.Envelope.Source,
            currentTrack.SourceTrackId,
            new SourceTrackMetadata(currentTrack.Title, currentTrack.Artist, currentTrack.Album,
                currentTrack.DurationMs));
        if (lookup.Status == LocalLyricsLookupStatus.Found && lookup.Document is { } document)
        {
            if (!string.Equals(document.LrcContentHash, observedFingerprint, StringComparison.Ordinal))
                return ExternalLocalLyricsUpdate.Retry;
            _activeLocalLyricsRecord = document.Record;
            if (_externalLrcUsable && CurrentLocalLyrics?.LrcContentHash == observedFingerprint)
                return ExternalLocalLyricsUpdate.Unchanged;
            var effectiveMetadata = CurrentLocalLyrics?.EffectiveMetadata ?? document.EffectiveMetadata;
            SetLocalLyrics(document with { EffectiveMetadata = effectiveMetadata }, Current.Envelope);
            LocalAssociationStatus = "Found";
            _log?.Invoke("Information", "ExternalLyrics",
                $"External LRC reloaded for LocalTrackId={localTrackId}.");
            return ExternalLocalLyricsUpdate.Reloaded;
        }

        if (lookup.Status == LocalLyricsLookupStatus.BrokenRecord)
        {
            try
            {
                if (!File.Exists(_library.ResolveLyricsPath(record)))
                    return ExternalLocalLyricsUpdate.Retry;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
            {
                return ExternalLocalLyricsUpdate.Unavailable;
            }
            MarkExternalLocalLyricsInvalid(localTrackId, lookup.Error);
            return ExternalLocalLyricsUpdate.Invalid;
        }
        if (lookup.Status is LocalLyricsLookupStatus.LibraryUnavailable or LocalLyricsLookupStatus.IndexUnavailable)
        {
            MarkExternalLocalLyricsUnavailable(localTrackId, lookup.Error);
            return ExternalLocalLyricsUpdate.Unavailable;
        }
        return ExternalLocalLyricsUpdate.Retry;
    }

    public bool MarkExternalLocalLyricsInvalid(string localTrackId, string? error = null)
    {
        if (_activeLocalLyricsRecord?.LocalTrackId != localTrackId) return false;
        if (IsCurrentLocalLrcMissing && CurrentLyrics is null && CurrentLocalLyrics is { } lastKnownGood && Current is { } current)
            SetLocalLyrics(lastKnownGood, current.Envelope);
        _externalLrcUsable = false;
        IsCurrentLocalLrcMissing = false;
        LocalAssociationStatus = "External LRC rejected";
        LyricsLoadedFrom = CurrentLocalLyrics is null
            ? "Local Library (external LRC rejected)"
            : "Local Library (last valid lyrics retained)";
        _log?.Invoke("Warning", "ExternalLyrics",
            $"External LRC reload rejected for LocalTrackId={localTrackId}; keeping last-known-good runtime lyrics. {error}");
        return true;
    }

    public bool MarkExternalLocalLyricsMissing(string localTrackId)
    {
        if (_activeLocalLyricsRecord?.LocalTrackId != localTrackId) return false;
        _externalLrcUsable = false;
        IsCurrentLocalLrcMissing = true;
        CurrentLyrics = null;
        _lyricsTimeline = LyricsTimeline.Empty;
        LocalAssociationStatus = "Local LRC unavailable";
        LyricsLoadedFrom = "Local Library (file unavailable)";
        _log?.Invoke("Warning", "ExternalLyrics", $"External LRC is missing for LocalTrackId={localTrackId}.");
        return true;
    }

    public bool MarkExternalLocalLyricsUnavailable(string localTrackId, string? error = null)
    {
        if (_activeLocalLyricsRecord?.LocalTrackId != localTrackId) return false;
        _externalLrcUsable = false;
        IsCurrentLocalLrcMissing = false;
        LocalAssociationStatus = "External LRC temporarily unavailable";
        LyricsLoadedFrom = CurrentLocalLyrics is null
            ? "Local Library (file temporarily unavailable)"
            : "Local Library (last valid lyrics retained)";
        _log?.Invoke("Warning", "ExternalLyrics",
            $"External LRC could not be read for LocalTrackId={localTrackId}; keeping last-known-good runtime lyrics. {error}");
        return true;
    }

    private void SetTimeline(LyricsSnapshotPayload lyrics) =>
        _lyricsTimeline = lyrics.Available && lyrics.Timed && lyrics.Lines.Count > 0
            ? new LyricsTimeline(lyrics.Lines)
            : LyricsTimeline.Empty;
}

public enum LyricsApplyDecision
{
    Rejected,
    AcceptedRemote,
    ImportedAsLocal,
    IgnoredBecauseLocal
}

public enum ExternalLocalLyricsUpdate
{
    Reloaded,
    Unchanged,
    Invalid,
    Unavailable,
    Retry,
    Stale
}
