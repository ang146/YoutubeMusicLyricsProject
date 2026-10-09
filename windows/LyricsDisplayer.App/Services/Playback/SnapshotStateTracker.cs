using LyricsDisplayer.Core.Protocol;

namespace LyricsDisplayer;

public enum SnapshotDecision
{
    Accepted,
    AcceptedNewSession,
    RejectedDuplicate,
    RejectedStale
}
public sealed class SnapshotStateTracker
{
    private readonly object _gate = new();

    public PlaybackSnapshotMessage? Current { get; private set; }

    public SnapshotDecision Apply(PlaybackSnapshotMessage candidate)
    {
        lock (_gate)
        {
            if (Current is null)
            {
                Current = candidate;
                return SnapshotDecision.Accepted;
            }

            if (!string.Equals(Current.Envelope.SourceSessionId, candidate.Envelope.SourceSessionId,
                    StringComparison.Ordinal))
            {
                Current = candidate;
                return SnapshotDecision.AcceptedNewSession;
            }

            if (candidate.Envelope.Sequence == Current.Envelope.Sequence)
            {
                return SnapshotDecision.RejectedDuplicate;
            }

            if (candidate.Envelope.Sequence < Current.Envelope.Sequence)
            {
                return SnapshotDecision.RejectedStale;
            }

            Current = candidate;
            return SnapshotDecision.Accepted;
        }
    }
}
