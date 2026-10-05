using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Core.Playback;
using LyricsDisplayer.Core.Protocol;

namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class TimingAdjustmentCoordinatorTests
{
    private string _root = null!;
    private LibraryPaths _paths = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "LyricsDisplayerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _paths = new(_root, Path.Combine(_root, "settings.json"), Path.Combine(_root, "Lyrics"),
            Path.Combine(_root, "library-index.db"), false);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Test]
    public void PositiveAdjustmentWhilePausedMakesLyricsLaterWithoutChangingPlaybackClock()
    {
        using var library = Library();
        Import(library, "track-a");
        var coordinator = Coordinator(library, out _);
        coordinator.Apply(Playback("track-a", 1, 10_499, playing: false));
        Assert.That(coordinator.GetTimelinePosition().CurrentLine!.Text, Is.EqualTo("A"));

        Assert.That(coordinator.AdjustTiming(500).Succeeded, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(coordinator.GlobalOffsetMs, Is.EqualTo(500));
            Assert.That(coordinator.GetLocalPositionMs(), Is.EqualTo(10_499));
            Assert.That(coordinator.GetTimelinePosition().CurrentLine, Is.Null);
            Assert.That(coordinator.GetTimelinePosition().NextLine!.Text, Is.EqualTo("A"));
        });
    }

    [Test]
    public void NegativeAdjustmentMakesLyricsEarlierAndResumeStillUsesPlaybackClock()
    {
        using var library = Library();
        Import(library, "track-a");
        var coordinator = Coordinator(library, out var time);
        coordinator.Apply(Playback("track-a", 1, 9_499, playing: false));
        coordinator.AdjustTiming(-500);
        Assert.That(coordinator.GetTimelinePosition().CurrentLine, Is.Null);

        coordinator.Apply(Playback("track-a", 2, 9_499, playing: true));
        time.AdvanceMilliseconds(1);
        Assert.That(coordinator.GetTimelinePosition().CurrentLine!.Text, Is.EqualTo("A"));
    }

    [Test]
    public void FineAdjustmentsAreCumulativeAndResetIsImmediateWithoutLrcRewrite()
    {
        using var library = Library();
        var id = Import(library, "track-a");
        var lrcPath = Path.Combine(_paths.LibraryPath, "tracks", id, "track.lrc");
        var original = File.ReadAllBytes(lrcPath);
        var coordinator = Coordinator(library, out _);
        coordinator.Apply(Playback("track-a", 1, 10_050, playing: false));

        foreach (var delta in new long[] { 500, 100, -500, -100 })
            Assert.That(coordinator.AdjustTiming(delta).Succeeded, Is.True);
        Assert.That(coordinator.GlobalOffsetMs, Is.Zero);
        coordinator.AdjustTiming(700);
        Assert.That(coordinator.ResetTiming().Succeeded, Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(coordinator.GlobalOffsetMs, Is.Zero);
            Assert.That(coordinator.GetTimelinePosition().CurrentLine!.Text, Is.EqualTo("A"));
            Assert.That(File.ReadAllBytes(lrcPath), Is.EqualTo(original));
            Assert.That(library.Lookup(id).Document!.GlobalOffsetMs, Is.Zero);
        });
    }

    [Test]
    public void ForwardAndBackwardSeeksUseCurrentTrackOffset()
    {
        using var library = Library();
        var id = Import(library, "track-a");
        library.SetGlobalOffset(id, 500);
        var coordinator = Coordinator(library, out _);
        coordinator.Apply(Playback("track-a", 1, 10_500, playing: false));
        Assert.That(coordinator.GetTimelinePosition().CurrentLine!.Text, Is.EqualTo("A"));

        coordinator.Apply(Playback("track-a", 2, 20_500, playing: false));
        Assert.That(coordinator.GetTimelinePosition().CurrentLine!.Text, Is.EqualTo("B"));
        coordinator.Apply(Playback("track-a", 3, 10_499, playing: false));
        Assert.That(coordinator.GetTimelinePosition().CurrentLine, Is.Null);
    }

    [Test]
    public void TrackSwitchLoadsPerTrackOffsetAndPendingStateIsNeutral()
    {
        using var library = Library();
        var a = Import(library, "track-a");
        var b = Import(library, "track-b");
        library.SetGlobalOffset(a, 500);
        library.SetGlobalOffset(b, -200);
        var coordinator = Coordinator(library, out _);

        coordinator.Apply(Playback("track-a", 1, 10_000, playing: false));
        Assert.That(coordinator.GlobalOffsetMs, Is.EqualTo(500));
        coordinator.Apply(Playback("track-b", 2, 10_000, playing: false));
        Assert.That(coordinator.GlobalOffsetMs, Is.EqualTo(-200));
        coordinator.Apply(Playback("track-pending", 3, 10_000, playing: false));
        Assert.Multiple(() =>
        {
            Assert.That(coordinator.GlobalOffsetMs, Is.Zero);
            Assert.That(coordinator.CanAdjustTiming, Is.False);
            Assert.That(coordinator.GetTimelinePosition().HasLyrics, Is.False);
        });
        coordinator.Apply(Playback("track-a", 4, 10_000, playing: false));
        Assert.That(coordinator.GlobalOffsetMs, Is.EqualTo(500));
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    public void UnavailableAndUntimedLyricsCannotCreateTimingState(bool available, bool timed)
    {
        using var library = Library();
        var coordinator = Coordinator(library, out _);
        coordinator.Apply(Playback("track-a", 1, 10_000, playing: false));
        coordinator.ApplyLyrics(Lyrics("track-a", 2, available, timed));

        Assert.Multiple(() =>
        {
            Assert.That(coordinator.CanAdjustTiming, Is.False);
            Assert.That(coordinator.AdjustTiming(500).Status, Is.EqualTo(TimingAdjustmentStatus.NoLocalLyrics));
            Assert.That(Directory.EnumerateDirectories(Path.Combine(_paths.LibraryPath, "tracks")), Is.Empty);
        });
    }

    [Test]
    public void TimedRuntimeOnlyLyricsCannotCreateTimingStateWhenLibraryIsUnavailable()
    {
        var unavailablePaths = _paths with
        {
            LibraryPath = "\\\\invalid-server-for-lyrics-tests\\missing-share\\Lyrics",
            UsesConfiguredPath = true
        };
        using var library = new LyricsLibrary(unavailablePaths);
        Assert.That(library.Initialise().Completed, Is.False);
        var coordinator = Coordinator(library, out _);
        coordinator.Apply(Playback("track-a", 1, 10_000, playing: false));
        coordinator.ApplyLyrics(Lyrics("track-a", 2));

        Assert.Multiple(() =>
        {
            Assert.That(coordinator.CurrentLyrics!.Payload.Timed, Is.True);
            Assert.That(coordinator.CurrentLocalLyrics, Is.Null);
            Assert.That(coordinator.CanAdjustTiming, Is.False);
            Assert.That(coordinator.AdjustTiming(500).Status, Is.EqualTo(TimingAdjustmentStatus.NoLocalLyrics));
        });
    }

    [Test]
    public void BakeConfirmationTargetCannotAffectNewlyCurrentTrack()
    {
        using var library = Library();
        var a = Import(library, "track-a");
        var b = Import(library, "track-b");
        library.SetGlobalOffset(a, 500);
        var aLrc = Path.Combine(_paths.LibraryPath, "tracks", a, "track.lrc");
        var bLrc = Path.Combine(_paths.LibraryPath, "tracks", b, "track.lrc");
        var coordinator = Coordinator(library, out _);
        coordinator.Apply(Playback("track-a", 1, 10_500, playing: false));
        var target = coordinator.CaptureTimingAdjustmentTarget()!;

        coordinator.Apply(Playback("track-b", 2, 10_500, playing: false));
        var result = coordinator.BakeTiming(target);
        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(TimingAdjustmentStatus.TrackChanged));
            Assert.That(File.ReadAllText(aLrc), Does.Contain("[00:10.000]"));
            Assert.That(File.ReadAllText(bLrc), Does.Contain("[00:10.000]"));
        });
    }

    [Test]
    public void AdjustedTimelineFlowsThroughNormalOverlayPresentation()
    {
        using var library = Library();
        Import(library, "track-a");
        var coordinator = Coordinator(library, out _);
        coordinator.Apply(Playback("track-a", 1, 19_800, playing: false));
        Assert.That(coordinator.GetTimelinePosition().CurrentLine!.Text, Is.EqualTo("A"));
        coordinator.AdjustTiming(-500);

        var presentation = LyricsOverlayPresentationState.FromLyrics(
            coordinator.CurrentLyrics!.Payload, coordinator.GetTimelinePosition());
        Assert.That(presentation.PrimaryText, Is.EqualTo("B"));
    }

    [Test]
    public void TimingSavePreservesCurrentEffectiveMetadata()
    {
        using var library = Library();
        Import(library, "track-a");
        var coordinator = Coordinator(library, out _);
        var liveTrack = new TrackInfo("track-a", "Live title", "Live artist", null, 60_000);
        coordinator.Apply(new PlaybackSnapshotMessage(
            Metadata(1, ProtocolConstants.PlaybackSnapshot),
            new(liveTrack, new(10_000, false, 1), new(false, false, null, [])), "{}"));

        Assert.That(coordinator.AdjustTiming(100).Succeeded, Is.True);
        Assert.That(coordinator.CurrentLocalLyrics!.EffectiveMetadata,
            Is.EqualTo(new EffectiveTrackMetadata("Live title", "Live artist")));
    }

    private LyricsLibrary Library()
    {
        var library = new LyricsLibrary(_paths);
        Assert.That(library.Initialise().Completed, Is.True);
        return library;
    }

    private static PlaybackStateCoordinator Coordinator(LyricsLibrary library, out FakeTime time)
    {
        time = new FakeTime();
        return new(NullLogger<PlaybackStateCoordinator>.Instance,
            new SnapshotStateTracker(), new PlaybackClock(time), library);
    }

    private static string Import(LyricsLibrary library, string id) =>
        library.Import(Track(id), Lyrics(id, 1).Payload).LocalTrackId!;

    private static TrackInfo Track(string id) => new(id, $"Title {id}", "Artist", null, 60_000);

    private static PlaybackSnapshotMessage Playback(string id, long sequence, long positionMs, bool playing) =>
        new(Metadata(sequence, ProtocolConstants.PlaybackSnapshot),
            new(Track(id), new(positionMs, playing, 1), new(false, false, null, [])), "{}");

    private static LyricsSnapshotMessage Lyrics(string id, long sequence, bool available = true, bool timed = true) =>
        new(Metadata(sequence, ProtocolConstants.LyricsSnapshot),
            new(id, available, timed, available ? "youtubeMusic" : null,
                timed
                    ? [new LyricsLine(10_000, 20_000, "A"), new LyricsLine(20_000, 30_000, "B"),
                       new LyricsLine(30_000, 40_000, "C")]
                    : [], null), "{}");

    private static EnvelopeMetadata Metadata(long sequence, string type) =>
        new(1, type, ProtocolConstants.YouTubeMusicSource, "session", sequence, DateTimeOffset.UnixEpoch);

    private sealed class FakeTime : IMonotonicTimeSource
    {
        private long _milliseconds;
        public long GetTimestamp() => _milliseconds;
        public TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp) =>
            TimeSpan.FromMilliseconds(endingTimestamp - startingTimestamp);
        public void AdvanceMilliseconds(long milliseconds) => _milliseconds += milliseconds;
    }
}
