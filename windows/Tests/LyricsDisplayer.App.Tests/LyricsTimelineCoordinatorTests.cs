using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Core.Playback;
using LyricsDisplayer.Core.Protocol;

namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class LyricsTimelineCoordinatorTests
{
    [Test]
    public void PlayingClockProgressesAcrossLyricBoundaries()
    {
        var coordinator = Coordinator(out var time);
        coordinator.Apply(Playback("track-a", 1, 9_000, playing: true));
        coordinator.ApplyLyrics(Lyrics("track-a", 2));

        AssertTimeline(coordinator, null, "A");
        time.AdvanceMilliseconds(1_000);
        AssertTimeline(coordinator, "A", "B");
        time.AdvanceMilliseconds(5_000);
        AssertTimeline(coordinator, "B", "C");
        time.AdvanceMilliseconds(5_000);
        AssertTimeline(coordinator, "C", null);
    }

    [Test]
    public void PauseFreezesTimelineAndResumeContinuesIt()
    {
        var coordinator = Coordinator(out var time);
        coordinator.Apply(Playback("track-a", 1, 10_000, playing: true));
        coordinator.ApplyLyrics(Lyrics("track-a", 2));
        time.AdvanceMilliseconds(1_000);
        coordinator.Apply(Playback("track-a", 3, 11_000, playing: false));

        time.AdvanceMilliseconds(30_000);
        Assert.Multiple(() =>
        {
            Assert.That(coordinator.GetLocalPositionMs(), Is.EqualTo(11_000));
            Assert.That(coordinator.GetTimelinePosition().CurrentLine!.Text, Is.EqualTo("A"));
        });

        coordinator.Apply(Playback("track-a", 4, 11_000, playing: true));
        time.AdvanceMilliseconds(4_000);
        AssertTimeline(coordinator, "B", "C");
    }

    [Test]
    public void ForwardAndBackwardSnapshotSeeksImmediatelyReevaluateTimeline()
    {
        var coordinator = Coordinator(out _);
        coordinator.Apply(Playback("track-a", 1, 10_000, playing: false));
        coordinator.ApplyLyrics(Lyrics("track-a", 2));
        AssertTimeline(coordinator, "A", "B");

        coordinator.Apply(Playback("track-a", 3, 20_000, playing: false));
        AssertTimeline(coordinator, "C", null);
        coordinator.Apply(Playback("track-a", 4, 15_000, playing: false));
        AssertTimeline(coordinator, "B", "C");
        coordinator.Apply(Playback("track-a", 5, 5_000, playing: false));
        AssertTimeline(coordinator, null, "A");
    }

    [Test]
    public void TrackAndSessionChangesClearPreviousTimelineBeforeNewLyrics()
    {
        var coordinator = Coordinator(out _);
        coordinator.Apply(Playback("track-a", 1, 10_000, playing: false));
        coordinator.ApplyLyrics(Lyrics("track-a", 2));
        AssertTimeline(coordinator, "A", "B");

        coordinator.Apply(Playback("track-b", 3, 10_000, playing: false));
        AssertEmpty(coordinator);
        coordinator.ApplyLyrics(Lyrics("track-b", 4, prefix: "B-"));
        AssertTimeline(coordinator, "B-A", "B-B");

        coordinator.Apply(Playback("track-b", 1, 10_000, playing: false, session: "new-session"));
        AssertEmpty(coordinator);
        coordinator.ApplyLyrics(Lyrics("track-b", 2, session: "new-session", prefix: "New-"));
        AssertTimeline(coordinator, "New-A", "New-B");
    }

    [TestCase(true, false)]
    [TestCase(false, false)]
    public void UntimedAndUnavailableLyricsHaveNoTimeline(bool available, bool timed)
    {
        var coordinator = Coordinator(out _);
        coordinator.Apply(Playback("track-a", 1, 10_000, playing: false));
        coordinator.ApplyLyrics(Lyrics("track-a", 2, available, timed));
        AssertEmpty(coordinator);
    }

    [Test]
    public void RestartedEditedLocalLrcDrivesTimelineAndRemoteCannotReplaceIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "LyricsDisplayerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = new LibraryPaths(root, Path.Combine(root, "settings.json"), Path.Combine(root, "Lyrics"),
                Path.Combine(root, "library-index.db"), false);
            string localTrackId;
            using (var library = new LyricsLibrary(paths))
            {
                library.Initialise();
                localTrackId = library.Import(Track("track-a"), Lyrics("track-a", 2).Payload).LocalTrackId!;
            }
            var lrc = Path.Combine(paths.LibraryPath, "tracks", localTrackId, "track.lrc");
            File.WriteAllText(lrc, "[00:12.000]Edited local A\n[00:18.000]Edited local B\n");

            using var restarted = new LyricsLibrary(paths);
            restarted.Initialise();
            var time = new FakeMonotonicTimeSource();
            var coordinator = new PlaybackStateCoordinator(new SnapshotStateTracker(), new PlaybackClock(time), restarted);
            coordinator.Apply(Playback("track-a", 3, 11_999, playing: false));
            AssertTimeline(coordinator, null, "Edited local A");
            coordinator.Apply(Playback("track-a", 4, 12_000, playing: false));
            AssertTimeline(coordinator, "Edited local A", "Edited local B");

            var decision = coordinator.ApplyLyricsDetailed(Lyrics("track-a", 5, prefix: "Remote-"));
            Assert.Multiple(() =>
            {
                Assert.That(decision, Is.EqualTo(LyricsApplyDecision.IgnoredBecauseLocal));
                Assert.That(coordinator.GetTimelinePosition().CurrentLine!.Text, Is.EqualTo("Edited local A"));
                Assert.That(File.ReadAllText(lrc), Does.Not.Contain("Remote-"));
            });
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static PlaybackStateCoordinator Coordinator(out FakeMonotonicTimeSource time)
    {
        time = new FakeMonotonicTimeSource();
        return new(new SnapshotStateTracker(), new PlaybackClock(time));
    }

    private static void AssertTimeline(PlaybackStateCoordinator coordinator, string? current, string? next)
    {
        var timeline = coordinator.GetTimelinePosition();
        Assert.Multiple(() =>
        {
            Assert.That(timeline.CurrentLine?.Text, Is.EqualTo(current));
            Assert.That(timeline.NextLine?.Text, Is.EqualTo(next));
        });
    }

    private static void AssertEmpty(PlaybackStateCoordinator coordinator)
    {
        var timeline = coordinator.GetTimelinePosition();
        Assert.Multiple(() =>
        {
            Assert.That(timeline.HasLyrics, Is.False);
            Assert.That(timeline.CurrentLine, Is.Null);
            Assert.That(timeline.NextLine, Is.Null);
        });
    }

    private static TrackInfo Track(string id) => new(id, $"Title {id}", "Artist", null, 60_000);

    private static PlaybackSnapshotMessage Playback(string id, long sequence, long positionMs, bool playing,
        string session = "session-a") =>
        new(Metadata(sequence, session, ProtocolConstants.PlaybackSnapshot),
            new(Track(id), new(positionMs, playing, 1), new(false, false, null, [])), "{}");

    private static LyricsSnapshotMessage Lyrics(string id, long sequence, bool available = true, bool timed = true,
        string session = "session-a", string prefix = "") =>
        new(Metadata(sequence, session, ProtocolConstants.LyricsSnapshot),
            new(id, available, timed, available ? "youtubeMusic" : null,
                timed
                    ? [new LyricsLine(10_000, 10_100, $"{prefix}A"),
                       new LyricsLine(15_000, 15_100, $"{prefix}B"),
                       new LyricsLine(20_000, 20_100, $"{prefix}C")]
                    : [], "Provider"), "{}");

    private static EnvelopeMetadata Metadata(long sequence, string session, string type) =>
        new(1, type, ProtocolConstants.YouTubeMusicSource, session, sequence, DateTimeOffset.UnixEpoch);

    private sealed class FakeMonotonicTimeSource : IMonotonicTimeSource
    {
        private long _milliseconds;
        public long GetTimestamp() => _milliseconds;
        public TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp) =>
            TimeSpan.FromMilliseconds(endingTimestamp - startingTimestamp);
        public void AdvanceMilliseconds(long milliseconds) => _milliseconds += milliseconds;
    }
}
