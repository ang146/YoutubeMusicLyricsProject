using LyricsDisplayer.Core.Protocol;

namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class SnapshotStateTrackerTests
{
    private const string SessionOne = "550e8400-e29b-41d4-a716-446655440000";
    private const string SessionTwo = "d9428888-122b-11e1-b85c-61cd3cbb3210";

    [Test]
    public void FirstSnapshotIsAccepted()
    {
        var tracker = new SnapshotStateTracker();
        Assert.That(tracker.Apply(CreateMessage(SessionOne, 10, "first")), Is.EqualTo(SnapshotDecision.Accepted));
    }

    [Test]
    public void HigherSequenceIsAcceptedIncludingAGap()
    {
        var tracker = new SnapshotStateTracker();
        tracker.Apply(CreateMessage(SessionOne, 10, "old"));
        Assert.That(tracker.Apply(CreateMessage(SessionOne, 99, "new")), Is.EqualTo(SnapshotDecision.Accepted));
        Assert.That(tracker.Current!.Payload.Track.Title, Is.EqualTo("new"));
    }

    [Test]
    public void DuplicateSequenceIsIgnored()
    {
        var tracker = new SnapshotStateTracker();
        tracker.Apply(CreateMessage(SessionOne, 10, "accepted"));
        Assert.That(tracker.Apply(CreateMessage(SessionOne, 10, "duplicate")),
            Is.EqualTo(SnapshotDecision.RejectedDuplicate));
        Assert.That(tracker.Current!.Payload.Track.Title, Is.EqualTo("accepted"));
    }

    [Test]
    public void LowerSequenceIsIgnored()
    {
        var tracker = new SnapshotStateTracker();
        tracker.Apply(CreateMessage(SessionOne, 10, "accepted"));
        Assert.That(tracker.Apply(CreateMessage(SessionOne, 9, "stale")),
            Is.EqualTo(SnapshotDecision.RejectedStale));
    }

    [Test]
    public void StaleStateCannotOverwriteNewerState()
    {
        var tracker = new SnapshotStateTracker();
        tracker.Apply(CreateMessage(SessionOne, 20, "newest"));
        tracker.Apply(CreateMessage(SessionOne, 3, "old"));
        Assert.Multiple(() =>
        {
            Assert.That(tracker.Current!.Envelope.Sequence, Is.EqualTo(20));
            Assert.That(tracker.Current.Payload.Track.Title, Is.EqualTo("newest"));
        });
    }

    [Test]
    public void NewSourceSessionStartsFreshSequenceTracking()
    {
        var tracker = new SnapshotStateTracker();
        tracker.Apply(CreateMessage(SessionOne, 100, "old session"));
        var decision = tracker.Apply(CreateMessage(SessionTwo, 1, "new session"));
        Assert.Multiple(() =>
        {
            Assert.That(decision, Is.EqualTo(SnapshotDecision.AcceptedNewSession));
            Assert.That(tracker.Current!.Envelope.Sequence, Is.EqualTo(1));
            Assert.That(tracker.Current.Payload.Track.Title, Is.EqualTo("new session"));
        });
    }

    private static PlaybackSnapshotMessage CreateMessage(string session, long sequence, string title)
    {
        var metadata = new EnvelopeMetadata(1, "playbackSnapshot", "youtubeMusic", session, sequence,
            DateTimeOffset.Parse("2026-09-09T05:30:00Z"));
        var payload = new PlaybackSnapshotPayload(
            new TrackInfo("id", title, "artist", "album", 1000),
            new PlaybackState(100, true, 1),
            new LyricsInfo(true, true, "youtubeMusic", [new LyricsLine(0, 1000, "line")]));
        return new PlaybackSnapshotMessage(metadata, payload, "{}");
    }
}
