using LyricsDisplayer.Core.Playback;
using LyricsDisplayer.Core.Protocol;

namespace LyricsDisplayer;

public sealed class PlaybackStateCoordinator
{
    private readonly SnapshotStateTracker _stateTracker;
    private readonly PlaybackClock _playbackClock;
    private long _trackSequenceFloor;
    private long _lyricsSequence = -1;

    public LyricsSnapshotMessage? CurrentLyrics { get; private set; }

    public PlaybackStateCoordinator(SnapshotStateTracker stateTracker, PlaybackClock playbackClock)
    {
        _stateTracker = stateTracker;
        _playbackClock = playbackClock;
    }

    public PlaybackSnapshotMessage? Current => _stateTracker.Current;

    public bool HasClockState => _playbackClock.HasState;

    public long GetLocalPositionMs() => _playbackClock.GetPositionMs();

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
                _lyricsSequence = -1;
                // On initial connection, a retained lyrics result can predate the latest playback.
                // Within a known session, reject results from an earlier visit to this track.
                _trackSequenceFloor = previous is null || decision == SnapshotDecision.AcceptedNewSession
                    ? 0 : snapshot.Envelope.Sequence;
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

    public bool ApplyLyrics(LyricsSnapshotMessage snapshot)
    {
        var current = Current;
        if (current is null || snapshot.Envelope.Source != current.Envelope.Source ||
            snapshot.Envelope.SourceSessionId != current.Envelope.SourceSessionId ||
            snapshot.Payload.SourceTrackId != current.Payload.Track.SourceTrackId ||
            snapshot.Envelope.Sequence < _trackSequenceFloor ||
            snapshot.Envelope.Sequence <= _lyricsSequence)
        {
            return false;
        }
        CurrentLyrics = snapshot;
        _lyricsSequence = snapshot.Envelope.Sequence;
        return true;
    }
}
