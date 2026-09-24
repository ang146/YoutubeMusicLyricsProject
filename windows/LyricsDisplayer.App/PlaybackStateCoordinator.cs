using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Core.Playback;
using LyricsDisplayer.Core.Protocol;
using LyricsDisplayer.Core.Timeline;

namespace LyricsDisplayer;

public sealed class PlaybackStateCoordinator
{
    private readonly SnapshotStateTracker _stateTracker;
    private readonly PlaybackClock _playbackClock;
    private long _trackSequenceFloor;
    private long _lyricsSequence = -1;
    private readonly LyricsLibrary? _library;
    private readonly Action<string, string, string>? _log;
    private LyricsTimeline _lyricsTimeline = LyricsTimeline.Empty;

    public LyricsSnapshotMessage? CurrentLyrics { get; private set; }
    public LocalLyricsDocument? CurrentLocalLyrics { get; private set; }
    public string LyricsLoadedFrom { get; private set; } = "Pending / unknown";
    public string LocalAssociationStatus { get; private set; } = "Not checked";

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

    public long GlobalOffsetMs => CurrentLocalLyrics?.GlobalOffsetMs ?? 0;

    public bool CanAdjustTiming => CurrentLocalLyrics is not null && _library is not null;

    public bool CanAdjustCurrentLineTiming => _library is not null &&
        CurrentLocalLyrics is { IsLrcWritable: true, LrcContentHash: not null } &&
        GetTimelinePosition().CurrentIndex is not null;

    public CurrentLineTimingTarget? CaptureCurrentLineTimingTarget()
    {
        var document = CurrentLocalLyrics;
        var index = GetTimelinePosition().CurrentIndex;
        return document is { IsLrcWritable: true, LrcContentHash: not null } &&
               _library is not null && index is not null
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
        if (_library is null || current is null ||
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

    public LyricsTimelinePosition GetTimelinePosition() =>
        _lyricsTimeline.Evaluate(LyricsTimingAdjustment.GetEvaluationPosition(
            _playbackClock.GetPositionMs(), GlobalOffsetMs));

    public TimingAdjustmentResult AdjustTiming(long deltaMs)
    {
        var target = CurrentLocalLyrics;
        if (target is null || _library is null)
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
        if (target is null || _library is null)
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
        CurrentLocalLyrics is null
            ? null
            : new(CurrentLocalLyrics.Record.LocalTrackId, CurrentLocalLyrics.GlobalOffsetMs);

    public TimingAdjustmentResult BakeTiming(TimingAdjustmentTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var current = CurrentLocalLyrics;
        if (current is null || _library is null)
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
            if (previous is null || decision == SnapshotDecision.AcceptedNewSession ||
                previous.Payload.Track.SourceTrackId != snapshot.Payload.Track.SourceTrackId)
            {
                CurrentLyrics = null;
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
        _lyricsSequence = snapshot.Envelope.Sequence;

        if (CurrentLocalLyrics is not null)
        {
            _log?.Invoke("Information", "Library",
                $"Remote lyrics ignored because local copy {CurrentLocalLyrics.Record.LocalTrackId} is authoritative.");
            return LyricsApplyDecision.IgnoredBecauseLocal;
        }

        CurrentLyrics = snapshot;
        SetTimeline(snapshot.Payload);
        LyricsLoadedFrom = "YouTube Music (runtime)";
        if (_library is null || !snapshot.Payload.Available || !snapshot.Payload.Timed || snapshot.Payload.Lines.Count == 0)
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
        if (lookup.Document is not null) SetLocalLyrics(lookup.Document, snapshot.Envelope);
    }

    private void SetLocalLyrics(LocalLyricsDocument document, EnvelopeMetadata envelope)
    {
        CurrentLocalLyrics = document;
        LyricsLoadedFrom = "Local Library";
        CurrentLyrics = new LyricsSnapshotMessage(
            envelope with { MessageType = ProtocolConstants.LyricsSnapshot },
            new LyricsSnapshotPayload(Current!.Payload.Track.SourceTrackId, true, true,
                document.Sidecar.Lyrics.Source, document.Lines, document.Sidecar.Lyrics.Attribution),
            "");
        _lyricsTimeline = new LyricsTimeline(document.Lines);
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
