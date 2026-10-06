using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Core.Playback;
using LyricsDisplayer.Core.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class CurrentLyricsTimingServiceTests
{
    private string _root = null!;
    private LibraryPaths _paths = null!;
    private LyricsLibrary _library = null!;
    private PlaybackStateCoordinator _playback = null!;
    private CurrentLyricsTimingService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "LyricsDisplayerTests", Guid.NewGuid().ToString("N"));
        _paths = new(_root, Path.Combine(_root, "settings.json"), Path.Combine(_root, "Lyrics"),
            Path.Combine(_root, "library-index.db"), false);
        _library = new(_paths);
        Assert.That(_library.Initialise().Completed, Is.True);
        _playback = new(NullLogger<PlaybackStateCoordinator>.Instance,
            new SnapshotStateTracker(), new PlaybackClock(), _library);
        _service = new(_playback, _library, new LyricsTimingAdjustmentService(),
            NullLogger<CurrentLyricsTimingService>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _library.Dispose();
        foreach (var file in Directory.GetFiles(_root, "*.lrc", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, true);
    }

    [Test]
    public void CurrentLineAdjustmentPersistsOnlyTheExactCurrentOccurrenceAndReloadsPresentation()
    {
        var localId = Import("track-a", "[00:10.000]A\n[00:10.000]B\n[00:30.000]C\n");
        _playback.Apply(Playback("track-a", 1, 10_000));

        var result = _service.AdjustCurrentLine(100);

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True, result.Error);
            Assert.That(File.ReadAllText(LrcPath(localId)), Is.EqualTo(
                "[00:10.000]A\n[00:10.100]B\n[00:30.000]C\n"));
            Assert.That(_playback.CurrentLocalLyrics!.Lines[1].StartMs, Is.EqualTo(10_100));
            Assert.That(_playback.GetTimelinePosition().CurrentLine!.Text, Is.EqualTo("A"));
        });
    }

    [Test]
    public void ShiftAllPersistsEveryTimestampAndReloadsTheCurrentDocument()
    {
        var localId = Import("track-a", "[00:10.000][00:11.000]A\n[00:20.000]B\n");
        _playback.Apply(Playback("track-a", 1, 20_000));

        var result = _service.ShiftAll(100);

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True, result.Error);
            Assert.That(File.ReadAllText(LrcPath(localId)), Is.EqualTo(
                "[00:10.100][00:11.100]A\n[00:20.100]B\n"));
            Assert.That(_playback.CurrentLocalLyrics!.TimestampOccurrences.Select(item => item.StartMs),
                Is.EqualTo(new long[] { 10_100, 11_100, 20_100 }));
        });
    }

    [Test]
    public void NegativeAllLyricsShiftIsRejectedAtomicallyWithoutClampingOrPartialWrite()
    {
        const string content = "[00:00.200]First\n[00:05.000]Second\n";
        var localId = Import("track-a", content);
        _playback.Apply(Playback("track-a", 1, 5_000));
        var before = File.ReadAllBytes(LrcPath(localId));

        var result = _service.ShiftAll(-500);

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Status, Is.EqualTo(TimingAdjustmentStatus.NegativeTimestamp));
            Assert.That(File.ReadAllBytes(LrcPath(localId)), Is.EqualTo(before));
            Assert.That(_playback.CurrentLocalLyrics!.Lines.Select(line => line.StartMs),
                Is.EqualTo(new long[] { 200, 5_000 }));
        });
    }

    [Test]
    public void AllLyricsShiftKeepsProtectedZeroBlankAnchorAndShiftsOrdinaryLines()
    {
        const string content = "[00:00.000]\n[00:01.000]First\n[00:02.000]Second\n";
        var localId = Import("track-a", content);
        _playback.Apply(Playback("track-a", 1, 1_500));

        var result = _service.ShiftAll(-500);

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True, result.Error);
            Assert.That(File.ReadAllText(LrcPath(localId)), Is.EqualTo(
                "[00:00.000]\n[00:00.500]First\n[00:01.500]Second\n"));
        });
    }

    [Test]
    public void CurrentLineActionDoesNotEditTheNextLyricBeforeTheFirstTimestamp()
    {
        const string content = "[00:10.000]First\n[00:20.000]Second\n";
        var localId = Import("track-a", content);
        _playback.Apply(Playback("track-a", 1, 9_999));
        var before = File.ReadAllBytes(LrcPath(localId));

        Assert.Multiple(() =>
        {
            Assert.That(_service.CanAdjustCurrentLine, Is.False);
            Assert.That(_service.AdjustCurrentLine(100).Status, Is.EqualTo(TimingAdjustmentStatus.NoCurrentLine));
            Assert.That(File.ReadAllBytes(LrcPath(localId)), Is.EqualTo(before));
        });
    }

    [Test]
    public void MissingLocalLyricsAndExternalChangeRemainUnavailableOrConflictSafely()
    {
        _playback.Apply(Playback("remote-only", 1, 20_000));
        Assert.Multiple(() =>
        {
            Assert.That(_service.CanAdjustCurrentLine, Is.False);
            Assert.That(_service.CanShiftAll, Is.False);
            Assert.That(_service.ShiftAll(100).Status, Is.EqualTo(TimingAdjustmentStatus.NoLocalLyrics));
            Assert.That(_service.AdjustCurrentLine(100).Status, Is.EqualTo(TimingAdjustmentStatus.NoCurrentLine));
        });

        var localId = Import("track-a", "[00:10.000]A\n[00:20.000]B\n[00:30.000]C\n");
        _playback.Apply(Playback("track-a", 2, 20_000));
        var path = LrcPath(localId);
        File.WriteAllText(path, "[00:01.000]External A\n[00:02.000]External B\n");
        var result = _service.ShiftAll(100);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(TimingAdjustmentStatus.FileChanged));
            Assert.That(File.ReadAllText(path), Is.EqualTo("[00:01.000]External A\n[00:02.000]External B\n"));
        });
    }

    [Test]
    public void QuickActionsAreUnavailableWhileEditorPreviewOwnsThePresentation()
    {
        const string content = "[00:10.000]A\n[00:20.000]B\n";
        var localId = Import("track-a", content);
        _playback.Apply(Playback("track-a", 1, 10_000));
        var before = File.ReadAllBytes(LrcPath(localId));
        _playback.SetEditorPreview(_playback.ActiveLocalLyricsRecord!, _playback.CurrentLocalLyrics!.Lines, []);

        Assert.Multiple(() =>
        {
            Assert.That(_service.CanAdjustCurrentLine, Is.False);
            Assert.That(_service.CanShiftAll, Is.False);
            Assert.That(_service.ShiftAll(100).Status, Is.EqualTo(TimingAdjustmentStatus.NoLocalLyrics));
            Assert.That(File.ReadAllBytes(LrcPath(localId)), Is.EqualTo(before));
        });
    }

    [Test]
    public void MissingAuthoritativeLrcIsReportedAsUnavailableWithoutWriting()
    {
        var localId = Import("track-a", "[00:10.000]A\n");
        _playback.Apply(Playback("track-a", 1, 10_000));
        var path = LrcPath(localId);
        File.Delete(path);
        _playback.MarkExternalLocalLyricsMissing(localId);

        Assert.Multiple(() =>
        {
            Assert.That(_service.CanShiftAll, Is.False);
            Assert.That(_service.ShiftAll(100).Status, Is.EqualTo(TimingAdjustmentStatus.NoLocalLyrics));
            Assert.That(File.Exists(path), Is.False);
        });
    }

    [Test]
    public void MissingLrcBetweenWatcherUpdatesIsRejectedWithoutRecreatingIt()
    {
        var localId = Import("track-a", "[00:10.000]A\n");
        _playback.Apply(Playback("track-a", 1, 10_000));
        var path = LrcPath(localId);
        File.Delete(path);

        var result = _service.ShiftAll(100);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(TimingAdjustmentStatus.FileChanged));
            Assert.That(File.Exists(path), Is.False);
        });
    }

    private string Import(string id, string content)
    {
        var localId = _library.Import(Track(id), Lyrics(id, 1).Payload).LocalTrackId!;
        File.WriteAllText(LrcPath(localId), content);
        return localId;
    }

    private string LrcPath(string id) => Path.Combine(_paths.LibraryPath, "tracks", id, "track.lrc");
    private static TrackInfo Track(string id) => new(id, id, "Artist", null, 60_000);
    private static EnvelopeMetadata Envelope(long sequence, string type) =>
        new(1, type, "youtubeMusic", "session", sequence, DateTimeOffset.UnixEpoch);
    private static PlaybackSnapshotMessage Playback(string id, long sequence, long position) =>
        new(Envelope(sequence, ProtocolConstants.PlaybackSnapshot),
            new(Track(id), new(position, false, 1), new(false, false, null, [])), "{}");
    private static LyricsSnapshotMessage Lyrics(string id, long sequence) =>
        new(Envelope(sequence, ProtocolConstants.LyricsSnapshot),
            new(id, true, true, "youtubeMusic", [new(10_000, 20_000, "A"),
                new(20_000, 30_000, "B"), new(30_000, 60_000, "C")], null), "{}");
}
