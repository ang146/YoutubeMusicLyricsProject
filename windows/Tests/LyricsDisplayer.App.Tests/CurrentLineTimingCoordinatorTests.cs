using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Core.Playback;
using LyricsDisplayer.Core.Protocol;

namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class CurrentLineTimingCoordinatorTests
{
    private string _root = null!;
    private LibraryPaths _paths = null!;
    private LyricsLibrary _library = null!;
    private PlaybackStateCoordinator _coordinator = null!;
    private FakeTime _time = null!;
    private Action? _beforeCommit;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "LyricsDisplayerTests", Guid.NewGuid().ToString("N"));
        _paths = new(_root, Path.Combine(_root, "settings.json"), Path.Combine(_root, "Lyrics"),
            Path.Combine(_root, "library-index.db"), false);
        _beforeCommit = null;
        _library = new(_paths, null, null, () => _beforeCommit?.Invoke());
        Assert.That(_library.Initialise().Completed, Is.True);
        _time = new();
        _coordinator = new(new SnapshotStateTracker(), new PlaybackClock(_time), _library);
    }

    [TearDown]
    public void TearDown()
    {
        _library.Dispose();
        foreach (var file in Directory.GetFiles(_root, "*.lrc", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, true);
    }

    private string Import(string id = "track-a", string? content = null)
    {
        var localId = _library.Import(Track(id), Lyrics(id, 1).Payload).LocalTrackId!;
        if (content is not null) File.WriteAllText(LrcPath(localId), content);
        return localId;
    }

    private string LrcPath(string id) => Path.Combine(_paths.LibraryPath, "tracks", id, "track.lrc");
    private static TrackInfo Track(string id) => new(id, id, "Artist", null, 60_000);
    private static EnvelopeMetadata Envelope(long sequence, string type) =>
        new(1, type, "youtubeMusic", "session", sequence, DateTimeOffset.UnixEpoch);
    private static PlaybackSnapshotMessage Playback(string id, long sequence, long position, bool playing = false) =>
        new(Envelope(sequence, ProtocolConstants.PlaybackSnapshot),
            new(Track(id), new(position, playing, 1), new(false, false, null, [])), "{}");
    private static LyricsSnapshotMessage Lyrics(string id, long sequence, bool available = true, bool timed = true) =>
        new(Envelope(sequence, ProtocolConstants.LyricsSnapshot),
            new(id, available, timed, available ? "youtubeMusic" : null,
                timed ? [new(10_000, 20_000, "A"), new(20_000, 30_000, "B"), new(30_000, 60_000, "C")] : [], null), "{}");

    [Test]
    public void PausedEditImmediatelyRebuildsTimelineAndOverlayWithoutChangingClock()
    {
        var id = Import();
        _coordinator.Apply(Playback("track-a", 1, 20_000));
        Assert.That(_coordinator.GetTimelinePosition().CurrentLine!.Text, Is.EqualTo("B"));
        Assert.That(_coordinator.AdjustCurrentLineTiming(500).Succeeded, Is.True);
        var timeline = _coordinator.GetTimelinePosition();
        Assert.Multiple(() =>
        {
            Assert.That(timeline.CurrentLine!.Text, Is.EqualTo("A"));
            Assert.That(timeline.NextLine!.StartMs, Is.EqualTo(20_500));
            Assert.That(_coordinator.GetLocalPositionMs(), Is.EqualTo(20_000));
            Assert.That(_coordinator.CurrentLyrics!.Payload.Lines[1].StartMs, Is.EqualTo(20_500));
            Assert.That(File.ReadAllText(LrcPath(id)), Does.Contain("[00:20.500]B"));
            Assert.That(LyricsOverlayPresentationState.FromLyrics(_coordinator.CurrentLyrics.Payload, timeline).PrimaryText,
                Is.EqualTo("A"));
        });
        _coordinator.Apply(Playback("track-a", 2, 20_000, true));
        _time.Advance(500);
        Assert.That(_coordinator.GetTimelinePosition().CurrentLine!.Text, Is.EqualTo("B"));
    }

    [Test]
    public void CurrentLineIsSelectedThroughGlobalOffsetAndComposesWithBake()
    {
        var id = Import();
        _library.SetGlobalOffset(id, 500);
        _coordinator.Apply(Playback("track-a", 1, 20_500));
        Assert.That(_coordinator.AdjustCurrentLineTiming(100).Succeeded, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(_coordinator.GlobalOffsetMs, Is.EqualTo(500));
            Assert.That(_coordinator.CurrentLocalLyrics!.Lines[1].StartMs, Is.EqualTo(20_100));
            Assert.That(_coordinator.GetTimelinePosition().CurrentLine!.Text, Is.EqualTo("A"));
        });
        _coordinator.Apply(Playback("track-a", 2, 20_600));
        var before = _coordinator.GetTimelinePosition().CurrentLine!.Text;
        Assert.That(_coordinator.BakeTiming(_coordinator.CaptureTimingAdjustmentTarget()!).Succeeded, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(_coordinator.GlobalOffsetMs, Is.Zero);
            Assert.That(_coordinator.CurrentLocalLyrics!.Lines[1].StartMs, Is.EqualTo(20_600));
            Assert.That(_coordinator.GetTimelinePosition().CurrentLine!.Text, Is.EqualTo(before));
        });
        Assert.That(_coordinator.AdjustCurrentLineTiming(100).Succeeded, Is.True);
        Assert.That(_coordinator.CurrentLocalLyrics!.Lines[1].StartMs, Is.EqualTo(20_700));
    }

    [Test]
    public void SuccessfulNegativeBakeRefreshesRuntimeLyricsAndKeepsZeroIntroAnchor()
    {
        const string content = "[00:00.000]\n[00:10.000]A\n[00:20.000]B\n";
        var id = Import(content: content);
        Assert.That(_library.SetGlobalOffset(id, -100).Succeeded, Is.True);
        _coordinator.Apply(Playback("track-a", 1, 10_000));

        var result = _coordinator.BakeTiming(_coordinator.CaptureTimingAdjustmentTarget()!);

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True, result.Error);
            Assert.That(File.ReadAllText(LrcPath(id)), Is.EqualTo(
                "[00:00.000]\n[00:09.900]A\n[00:19.900]B\n"));
            Assert.That(_coordinator.GlobalOffsetMs, Is.Zero);
            Assert.That(_coordinator.CurrentLocalLyrics!.Lines.Select(line => line.StartMs),
                Is.EqualTo(new long[] { 0, 9_900, 19_900 }));
            Assert.That(_coordinator.CurrentLyrics!.Payload.Lines.Select(line => line.StartMs),
                Is.EqualTo(new long[] { 0, 9_900, 19_900 }));
            Assert.That(_coordinator.GetTimelinePosition().CurrentLine!.Text, Is.EqualTo("A"));
        });
    }

    [Test]
    public void NoCurrentLineDoesNotEditTheNextLine()
    {
        var id = Import();
        var before = File.ReadAllBytes(LrcPath(id));
        _coordinator.Apply(Playback("track-a", 1, 9_999));
        Assert.Multiple(() =>
        {
            Assert.That(_coordinator.GetTimelinePosition().NextLine!.Text, Is.EqualTo("A"));
            Assert.That(_coordinator.CanAdjustCurrentLineTiming, Is.False);
            Assert.That(_coordinator.CaptureCurrentLineTimingTarget(), Is.Null);
            Assert.That(_coordinator.AdjustCurrentLineTiming(100).Status, Is.EqualTo(TimingAdjustmentStatus.NoCurrentLine));
            Assert.That(File.ReadAllBytes(LrcPath(id)), Is.EqualTo(before));
        });
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    public void NoLyricsAndUntimedStatesCannotCreateFakeTimingRecords(bool available, bool timed)
    {
        _coordinator.Apply(Playback("track-a", 1, 20_000));
        _coordinator.ApplyLyrics(Lyrics("track-a", 2, available, timed));
        Assert.That(_coordinator.CanAdjustCurrentLineTiming, Is.False);
        Assert.That(_coordinator.AdjustCurrentLineTiming(100).Status, Is.EqualTo(TimingAdjustmentStatus.NoCurrentLine));
        Assert.That(Directory.GetDirectories(Path.Combine(_paths.LibraryPath, "tracks")), Is.Empty);
    }

    [Test]
    public void TimedRuntimeOnlyLyricsRemainUnavailableForLineEdits()
    {
        var runtime = new PlaybackStateCoordinator(new SnapshotStateTracker(), new PlaybackClock(_time));
        runtime.Apply(Playback("track-a", 1, 20_000));
        runtime.ApplyLyrics(Lyrics("track-a", 2));
        Assert.That(runtime.GetTimelinePosition().CurrentLine!.Text, Is.EqualTo("B"));
        Assert.That(runtime.CanAdjustCurrentLineTiming, Is.False);
        Assert.That(runtime.AdjustCurrentLineTiming(100).Status, Is.EqualTo(TimingAdjustmentStatus.NoCurrentLine));
    }

    [Test]
    public void CapturedTrackATargetCannotEditNewlyCurrentTrackB()
    {
        var a = Import();
        var b = Import("track-b");
        var aBefore = File.ReadAllBytes(LrcPath(a));
        var bBefore = File.ReadAllBytes(LrcPath(b));
        _coordinator.Apply(Playback("track-a", 1, 20_000));
        var target = _coordinator.CaptureCurrentLineTimingTarget()!;
        _coordinator.Apply(Playback("track-b", 2, 20_000));
        Assert.That(_coordinator.AdjustCurrentLineTiming(target, 500).Status, Is.EqualTo(TimingAdjustmentStatus.TrackChanged));
        Assert.That(File.ReadAllBytes(LrcPath(a)), Is.EqualTo(aBefore));
        Assert.That(File.ReadAllBytes(LrcPath(b)), Is.EqualTo(bBefore));
    }

    [Test]
    public void TrackChangeDuringPersistenceKeepsEditBoundToAAndDoesNotReplaceBState()
    {
        var a = Import();
        var b = Import("track-b");
        var bBefore = File.ReadAllBytes(LrcPath(b));
        _coordinator.Apply(Playback("track-a", 1, 20_000));
        _beforeCommit = () => _coordinator.Apply(Playback("track-b", 2, 20_000));
        Assert.That(_coordinator.AdjustCurrentLineTiming(500).Succeeded, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(LrcPath(a)), Does.Contain("[00:20.500]B"));
            Assert.That(File.ReadAllBytes(LrcPath(b)), Is.EqualTo(bBefore));
            Assert.That(_coordinator.CurrentLocalLyrics!.Record.LocalTrackId, Is.EqualTo(b));
            Assert.That(_coordinator.CurrentLocalLyrics.Lines[1].StartMs, Is.EqualTo(20_000));
        });
    }

    [Test]
    public void ExternalLrcEditAbortsAndReloadsRatherThanOverwriting()
    {
        var id = Import();
        _coordinator.Apply(Playback("track-a", 1, 20_000));
        File.WriteAllText(LrcPath(id), "[00:10.000]External A\n[00:19.000]External B");
        Assert.That(_coordinator.AdjustCurrentLineTiming(100).Status, Is.EqualTo(TimingAdjustmentStatus.FileChanged));
        Assert.That(_coordinator.GetTimelinePosition().CurrentLine!.Text, Is.EqualTo("External B"));
        Assert.That(File.ReadAllText(LrcPath(id)), Does.Contain("[00:19.000]External B"));
    }

    [Test]
    public void ExternalEditFollowedByGlobalAdjustmentDoesNotReplaceLoadedLineOccurrenceIdentity()
    {
        var id = Import();
        _coordinator.Apply(Playback("track-a", 1, 20_000));
        File.WriteAllText(LrcPath(id), "[00:05.000]New first\n[00:10.000]A\n[00:20.000]B");
        Assert.That(_coordinator.AdjustTiming(100).Succeeded, Is.True);
        Assert.That(_coordinator.AdjustCurrentLineTiming(100).Status, Is.EqualTo(TimingAdjustmentStatus.FileChanged));
        Assert.That(File.ReadAllText(LrcPath(id)), Is.EqualTo("[00:05.000]New first\n[00:10.000]A\n[00:20.000]B"));
    }

    [Test]
    public void BrokenExternallyChangedFileDisablesLineEditingAndClearsStaleTimeline()
    {
        var id = Import();
        _coordinator.Apply(Playback("track-a", 1, 20_000));
        File.WriteAllText(LrcPath(id), "not timed lyrics");
        Assert.That(_coordinator.AdjustCurrentLineTiming(100).Status, Is.EqualTo(TimingAdjustmentStatus.FileChanged));
        Assert.That(_coordinator.CanAdjustCurrentLineTiming, Is.False);
        Assert.That(_coordinator.GetTimelinePosition().HasLyrics, Is.False);
        Assert.That(File.ReadAllText(LrcPath(id)), Is.EqualTo("not timed lyrics"));
    }

    [Test]
    public void PersistenceFailureKeepsPreviousRuntimeLineAndOriginalLrc()
    {
        var id = Import();
        _coordinator.Apply(Playback("track-a", 1, 20_000));
        var before = File.ReadAllBytes(LrcPath(id));
        _beforeCommit = () => throw new IOException("Injected failure.");
        Assert.That(_coordinator.AdjustCurrentLineTiming(500).Status, Is.EqualTo(TimingAdjustmentStatus.StorageFailure));
        Assert.That(_coordinator.GetTimelinePosition().CurrentLine!.Text, Is.EqualTo("B"));
        Assert.That(_coordinator.CurrentLocalLyrics!.Lines[1].StartMs, Is.EqualTo(20_000));
        Assert.That(File.ReadAllBytes(LrcPath(id)), Is.EqualTo(before));
    }

    [Test]
    public void ReadOnlyLocalLrcDisablesCurrentLineControls()
    {
        var id = Import();
        File.SetAttributes(LrcPath(id), FileAttributes.ReadOnly);
        _coordinator.Apply(Playback("track-a", 1, 20_000));
        Assert.That(_coordinator.GetTimelinePosition().CurrentLine!.Text, Is.EqualTo("B"));
        Assert.That(_coordinator.CanAdjustCurrentLineTiming, Is.False);
    }

    [Test]
    public void LrcBecomingReadOnlyAfterLoadRejectsEditAndDisablesControlsWithoutChangingTimeline()
    {
        var id = Import();
        _coordinator.Apply(Playback("track-a", 1, 20_000));
        File.SetAttributes(LrcPath(id), FileAttributes.ReadOnly);
        Assert.That(_coordinator.AdjustCurrentLineTiming(100).Status, Is.EqualTo(TimingAdjustmentStatus.StorageFailure));
        Assert.That(_coordinator.CanAdjustCurrentLineTiming, Is.False);
        Assert.That(_coordinator.GetTimelinePosition().CurrentLine!.Text, Is.EqualTo("B"));
    }

    [Test]
    public void DuplicateTimestampCurrentEntryEditsOnlyTheLastStableOccurrence()
    {
        var id = Import(content: "[00:10.000]A\n[00:10.000]B\n[00:30.000]C");
        _coordinator.Apply(Playback("track-a", 1, 10_000));
        Assert.That(_coordinator.GetTimelinePosition().CurrentIndex, Is.EqualTo(1));
        Assert.That(_coordinator.AdjustCurrentLineTiming(100).Succeeded, Is.True);
        Assert.That(File.ReadAllText(LrcPath(id)), Is.EqualTo("[00:10.000]A\n[00:10.100]B\n[00:30.000]C"));
    }

    [Test]
    public void LaterRemoteLyricsCannotRestoreProviderTimestamps()
    {
        var id = Import();
        _coordinator.Apply(Playback("track-a", 1, 20_000));
        Assert.That(_coordinator.AdjustCurrentLineTiming(100).Succeeded, Is.True);
        Assert.That(_coordinator.ApplyLyricsDetailed(Lyrics("track-a", 2)), Is.EqualTo(LyricsApplyDecision.IgnoredBecauseLocal));
        Assert.That(_coordinator.CurrentLocalLyrics!.Lines[1].StartMs, Is.EqualTo(20_100));
        Assert.That(File.ReadAllText(LrcPath(id)), Does.Contain("[00:20.100]B"));
    }

    [Test]
    public void RestartedCoordinatorLoadsEditedTimestampsFromLrc()
    {
        Import();
        _coordinator.Apply(Playback("track-a", 1, 20_000));
        Assert.That(_coordinator.AdjustCurrentLineTiming(500).Succeeded, Is.True);
        using var restartedLibrary = new LyricsLibrary(_paths);
        Assert.That(restartedLibrary.Initialise().Completed, Is.True);
        var restarted = new PlaybackStateCoordinator(new SnapshotStateTracker(), new PlaybackClock(_time), restartedLibrary);
        restarted.Apply(Playback("track-a", 1, 20_000));
        Assert.That(restarted.GetTimelinePosition().NextLine!.StartMs, Is.EqualTo(20_500));
        Assert.That(restarted.GetTimelinePosition().CurrentLine!.Text, Is.EqualTo("A"));
    }

    private sealed class FakeTime : IMonotonicTimeSource
    {
        private long _milliseconds;
        public long GetTimestamp() => _milliseconds;
        public TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp) =>
            TimeSpan.FromMilliseconds(endingTimestamp - startingTimestamp);
        public void Advance(long milliseconds) => _milliseconds += milliseconds;
    }
}
