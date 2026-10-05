using LyricsDisplayer.Core.Playback;
using LyricsDisplayer.Core.Protocol;

namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class PlaybackStateCoordinatorTests
{
    private const string SessionOne = "550e8400-e29b-41d4-a716-446655440000";
    private const string SessionTwo = "d9428888-122b-11e1-b85c-61cd3cbb3210";

    [Test]
    public void AcceptedSnapshotUpdatesPlaybackClock()
    {
        var coordinator = CreateCoordinator(out var time);

        var decision = coordinator.Apply(CreateMessage(SessionOne, 1, positionMs: 10_000));
        time.AdvanceMilliseconds(1_000);

        Assert.Multiple(() =>
        {
            Assert.That(decision, Is.EqualTo(SnapshotDecision.Accepted));
            Assert.That(coordinator.GetLocalPositionMs(), Is.EqualTo(11_000));
        });
    }

    [Test]
    public void DuplicateSequenceDoesNotRebasePlaybackClock()
    {
        var coordinator = CreateCoordinator(out var time);
        coordinator.Apply(CreateMessage(SessionOne, 10, positionMs: 10_000));
        time.AdvanceMilliseconds(500);

        var decision = coordinator.Apply(CreateMessage(SessionOne, 10, positionMs: 80_000));

        Assert.Multiple(() =>
        {
            Assert.That(decision, Is.EqualTo(SnapshotDecision.RejectedDuplicate));
            Assert.That(coordinator.GetLocalPositionMs(), Is.EqualTo(10_500));
        });
    }

    [Test]
    public void LowerSequenceDoesNotRebasePlaybackClock()
    {
        var coordinator = CreateCoordinator(out var time);
        coordinator.Apply(CreateMessage(SessionOne, 10, positionMs: 10_000));
        time.AdvanceMilliseconds(500);

        var decision = coordinator.Apply(CreateMessage(SessionOne, 9, positionMs: 80_000));

        Assert.Multiple(() =>
        {
            Assert.That(decision, Is.EqualTo(SnapshotDecision.RejectedStale));
            Assert.That(coordinator.GetLocalPositionMs(), Is.EqualTo(10_500));
        });
    }

    [Test]
    public void HigherSequenceRebasesPlaybackClock()
    {
        var coordinator = CreateCoordinator(out _);
        coordinator.Apply(CreateMessage(SessionOne, 10, positionMs: 10_000));

        var decision = coordinator.Apply(CreateMessage(SessionOne, 11, positionMs: 20_000, playing: false));

        Assert.Multiple(() =>
        {
            Assert.That(decision, Is.EqualTo(SnapshotDecision.Accepted));
            Assert.That(coordinator.GetLocalPositionMs(), Is.EqualTo(20_000));
        });
    }

    [Test]
    public void SequenceGapIsAcceptedAndRebasesPlaybackClock()
    {
        var coordinator = CreateCoordinator(out _);
        coordinator.Apply(CreateMessage(SessionOne, 2, positionMs: 10_000));

        var decision = coordinator.Apply(CreateMessage(SessionOne, 99, positionMs: 30_000, playing: false));

        Assert.Multiple(() =>
        {
            Assert.That(decision, Is.EqualTo(SnapshotDecision.Accepted));
            Assert.That(coordinator.GetLocalPositionMs(), Is.EqualTo(30_000));
        });
    }

    [Test]
    public void SameSessionTrackChangeReplacesPreviousTimingState()
    {
        var coordinator = CreateCoordinator(out var time);
        coordinator.Apply(CreateMessage(SessionOne, 5, "track-a", 80_000, 100_000));
        time.AdvanceMilliseconds(500);

        coordinator.Apply(CreateMessage(SessionOne, 6, "track-b", 1_000, 5_000));
        time.AdvanceMilliseconds(10_000);

        Assert.Multiple(() =>
        {
            Assert.That(coordinator.Current!.Payload.Track.SourceTrackId, Is.EqualTo("track-b"));
            Assert.That(coordinator.GetLocalPositionMs(), Is.EqualTo(5_000));
        });
    }

    [Test]
    public void NewSourceSessionReplacesPreviousTimingState()
    {
        var coordinator = CreateCoordinator(out _);
        coordinator.Apply(CreateMessage(SessionOne, 100, positionMs: 80_000));

        var decision = coordinator.Apply(CreateMessage(SessionTwo, 1, positionMs: 2_000, playing: false));

        Assert.Multiple(() =>
        {
            Assert.That(decision, Is.EqualTo(SnapshotDecision.AcceptedNewSession));
            Assert.That(coordinator.GetLocalPositionMs(), Is.EqualTo(2_000));
        });
    }

    [Test]
    public void SourceDisconnectFreezesLocalClock()
    {
        var coordinator = CreateCoordinator(out var time);
        coordinator.Apply(CreateMessage(SessionOne, 1, positionMs: 10_000));
        time.AdvanceMilliseconds(1_000);

        coordinator.SourceDisconnected();
        time.AdvanceMilliseconds(30_000);

        Assert.That(coordinator.GetLocalPositionMs(), Is.EqualTo(11_000));
    }

    [Test]
    public void SnapshotAfterReconnectRebasesAndResumesClock()
    {
        var coordinator = CreateCoordinator(out var time);
        coordinator.Apply(CreateMessage(SessionOne, 1, positionMs: 10_000));
        time.AdvanceMilliseconds(1_000);
        coordinator.SourceDisconnected();
        time.AdvanceMilliseconds(30_000);

        coordinator.Apply(CreateMessage(SessionOne, 2, positionMs: 20_000));
        time.AdvanceMilliseconds(1_000);

        Assert.That(coordinator.GetLocalPositionMs(), Is.EqualTo(21_000));
    }

    [Test]
    public void RawLyricsSnapshotIsClearedWhenTrackOrSourceIdentityChanges()
    {
        var coordinator = CreateCoordinator(out _);
        coordinator.Apply(CreateMessage(SessionOne, 1, source: "source-a"));
        var firstLyrics = CreateLyricsMessage(SessionOne, 2, "track-a", "source-a", "raw track-a");
        coordinator.ApplyLyricsDetailed(firstLyrics);
        Assert.That(coordinator.CurrentRawLyricsSnapshot, Is.SameAs(firstLyrics));

        coordinator.Apply(CreateMessage(SessionOne, 3, "track-b", source: "source-a"));
        Assert.That(coordinator.CurrentRawLyricsSnapshot, Is.Null);

        var secondLyrics = CreateLyricsMessage(SessionOne, 4, "track-b", "source-a", "raw track-b");
        coordinator.ApplyLyricsDetailed(secondLyrics);
        Assert.That(coordinator.CurrentRawLyricsSnapshot, Is.SameAs(secondLyrics));

        coordinator.Apply(CreateMessage(SessionOne, 5, "track-b", source: "source-b"));
        Assert.That(coordinator.CurrentRawLyricsSnapshot, Is.Null);

        var sourceLyrics = CreateLyricsMessage(SessionOne, 6, "track-b", "source-b", "raw source-b");
        coordinator.ApplyLyricsDetailed(sourceLyrics);
        Assert.That(coordinator.CurrentRawLyricsSnapshot, Is.SameAs(sourceLyrics));
    }

    private static PlaybackStateCoordinator CreateCoordinator(out FakeMonotonicTimeSource time)
    {
        time = new FakeMonotonicTimeSource();
        return new PlaybackStateCoordinator(new SnapshotStateTracker(), new PlaybackClock(time));
    }

    private static PlaybackSnapshotMessage CreateMessage(
        string session,
        long sequence,
        string trackId = "track-a",
        long positionMs = 10_000,
        long durationMs = 100_000,
        bool playing = true,
        double playbackRate = 1.0,
        string source = "youtubeMusic")
    {
        var metadata = new EnvelopeMetadata(1, "playbackSnapshot", source, session, sequence,
            DateTimeOffset.Parse("2026-09-09T05:30:00Z"));
        var payload = new PlaybackSnapshotPayload(
            new TrackInfo(trackId, "title", "artist", null, durationMs),
            new PlaybackState(positionMs, playing, playbackRate),
            new LyricsInfo(false, false, null, []));
        return new PlaybackSnapshotMessage(metadata, payload, "{}");
    }

    private static LyricsSnapshotMessage CreateLyricsMessage(string session, long sequence, string trackId,
        string source, string rawJson)
    {
        var metadata = new EnvelopeMetadata(1, "lyricsSnapshot", source, session, sequence,
            DateTimeOffset.Parse("2026-09-09T05:30:00Z"));
        var payload = new LyricsSnapshotPayload(trackId, true, true, "provider", [], null);
        return new LyricsSnapshotMessage(metadata, payload, rawJson);
    }

    private sealed class FakeMonotonicTimeSource : IMonotonicTimeSource
    {
        private long _milliseconds;

        public long GetTimestamp() => _milliseconds;

        public TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp) =>
            TimeSpan.FromMilliseconds(endingTimestamp - startingTimestamp);

        public void AdvanceMilliseconds(long milliseconds) => _milliseconds += milliseconds;
    }
}
