using LyricsDisplayer.Core.Playback;
using LyricsDisplayer.Core.Protocol;

namespace LyricsDisplayer;

public sealed class PlaybackStateCoordinator
{
    private readonly SnapshotStateTracker _stateTracker;
    private readonly PlaybackClock _playbackClock;

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
        var decision = _stateTracker.Apply(snapshot);
        if (decision is SnapshotDecision.Accepted or SnapshotDecision.AcceptedNewSession)
        {
            _playbackClock.Rebase(
                snapshot.Payload.Playback.PositionMs,
                snapshot.Payload.Track.DurationMs,
                snapshot.Payload.Playback.Playing,
                snapshot.Payload.Playback.PlaybackRate);
        }

        return decision;
    }

    public void SourceDisconnected() => _playbackClock.Freeze();
}
