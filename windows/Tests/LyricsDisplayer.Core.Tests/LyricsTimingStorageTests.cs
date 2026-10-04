using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Core.Protocol;
using LyricsDisplayer.Core.Timeline;

namespace LyricsDisplayer.Core.Tests;

[TestFixture]
public sealed class LyricsTimingStorageTests
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
    public void OffsetPersistsInSidecarAcrossLibraryRestartWithoutChangingLrc()
    {
        string localTrackId;
        string lrcPath;
        using (var library = CreateLibrary())
        {
            localTrackId = Import(library, "track-a");
            lrcPath = LrcPath(localTrackId);
            var lrcBefore = File.ReadAllBytes(lrcPath);
            var result = library.SetGlobalOffset(localTrackId, 500);
            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.True);
                Assert.That(result.Document!.GlobalOffsetMs, Is.EqualTo(500));
                Assert.That(File.ReadAllBytes(lrcPath), Is.EqualTo(lrcBefore));
            });
        }

        using var restarted = CreateLibrary();
        Assert.That(restarted.Lookup(localTrackId).Document!.GlobalOffsetMs, Is.EqualTo(500));
    }

    [Test]
    public void DifferentLocalTracksKeepIndependentOffsets()
    {
        using var library = CreateLibrary();
        var trackA = Import(library, "track-a");
        var trackB = Import(library, "track-b");
        Assert.That(library.SetGlobalOffset(trackA, 500).Succeeded, Is.True);
        Assert.That(library.SetGlobalOffset(trackB, -200).Succeeded, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(library.Lookup(trackA).Document!.GlobalOffsetMs, Is.EqualTo(500));
            Assert.That(library.Lookup(trackB).Document!.GlobalOffsetMs, Is.EqualTo(-200));
            Assert.That(library.Lookup(trackA).Document!.GlobalOffsetMs, Is.EqualTo(500));
        });
    }

    [TestCase(500, "[00:10.500]A\n[00:20.500]B\n")]
    [TestCase(-500, "[00:09.500]A\n[00:19.500]B\n")]
    public void BakeRewritesTimestampsAndResetsOffset(long offset, string expected)
    {
        using var library = CreateLibrary();
        var id = Import(library, "track-a");
        File.WriteAllText(LrcPath(id), "[00:10.000]A\n[00:20.000]B\n");
        Assert.That(library.SetGlobalOffset(id, offset).Succeeded, Is.True);

        var result = library.BakeGlobalOffset(id);
        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Document!.GlobalOffsetMs, Is.Zero);
            Assert.That(File.ReadAllText(LrcPath(id)), Is.EqualTo(expected));
            Assert.That(library.Lookup(id).Document!.GlobalOffsetMs, Is.Zero);
        });
    }

    [Test]
    public void ZeroOffsetBakeIsANoOp()
    {
        using var library = CreateLibrary();
        var id = Import(library, "track-zero-offset");
        const string original = "[00:00.00]\r\n";
        File.WriteAllText(LrcPath(id), original);

        var result = library.BakeGlobalOffset(id);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(TimingAdjustmentStatus.NothingToBake));
            Assert.That(File.ReadAllText(LrcPath(id)), Is.EqualTo(original));
            Assert.That(library.Lookup(id).Document!.GlobalOffsetMs, Is.Zero);
        });
    }

    [Test]
    public void BakePreservesEffectiveTimelineBoundary()
    {
        using var library = CreateLibrary();
        var id = Import(library, "track-a");
        File.WriteAllText(LrcPath(id), "[00:10.000]A\n[00:20.000]B\n");
        var adjusted = library.SetGlobalOffset(id, 500).Document!;
        var before = new LyricsTimeline(adjusted.Lines).Evaluate(
            LyricsTimingAdjustment.GetEvaluationPosition(10_500, adjusted.GlobalOffsetMs));

        var baked = library.BakeGlobalOffset(id).Document!;
        var after = new LyricsTimeline(baked.Lines).Evaluate(
            LyricsTimingAdjustment.GetEvaluationPosition(10_500, baked.GlobalOffsetMs));
        Assert.Multiple(() =>
        {
            Assert.That(before.CurrentLine!.Text, Is.EqualTo("A"));
            Assert.That(after.CurrentLine!.Text, Is.EqualTo("A"));
            Assert.That(baked.Lines[0].StartMs, Is.EqualTo(10_500));
        });
    }

    [Test]
    public void InvalidNegativeBakeLeavesLrcAndOffsetUnchanged()
    {
        using var library = CreateLibrary();
        var id = Import(library, "track-a");
        File.WriteAllText(LrcPath(id), "[00:00.200]A\n");
        library.SetGlobalOffset(id, -500);
        var lrcBefore = File.ReadAllText(LrcPath(id));
        var sidecarBefore = File.ReadAllText(SidecarPath(id));

        var result = library.BakeGlobalOffset(id);
        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(TimingAdjustmentStatus.NegativeTimestamp));
            Assert.That(File.ReadAllText(LrcPath(id)), Is.EqualTo(lrcBefore));
            Assert.That(File.ReadAllText(SidecarPath(id)), Is.EqualTo(sidecarBefore));
            Assert.That(library.Lookup(id).Document!.GlobalOffsetMs, Is.EqualTo(-500));
        });
    }

    [Test]
    public void NegativeBakePreservesBlankAndBreakAnchorsBakesEveryOccurrenceAndResetsOffset()
    {
        using var library = CreateLibrary();
        var id = Import(library, "track-zero-anchors");
        const string original = "[ar:Artist]\r\n[00:00.000]\r\n[00:00.000]  ♪\r\n" +
                                "[00:00.000][02:00.000]♪\r\n[00:13.854]Line A\r\n";
        File.WriteAllText(LrcPath(id), original);
        Assert.That(library.SetGlobalOffset(id, -100).Succeeded, Is.True);

        var result = library.BakeGlobalOffset(id);

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True, result.Error);
            Assert.That(File.ReadAllText(LrcPath(id)), Is.EqualTo(
                "[ar:Artist]\r\n[00:00.000]\r\n[00:00.000]  ♪\r\n" +
                "[00:00.000][01:59.900]♪\r\n[00:13.754]Line A\r\n"));
            Assert.That(result.Document!.GlobalOffsetMs, Is.Zero);
            Assert.That(library.Lookup(id).Document!.GlobalOffsetMs, Is.Zero);
            Assert.That(result.Document.Lines.Any(line => line.Text == ""), Is.True,
                "The zero-time blank line remains a timed runtime line.");
        });
    }

    [Test]
    public void NegativeBakeWithProtectedZeroAndFailingNearZeroLeavesBothFilesAndOffsetUnchanged()
    {
        using var library = CreateLibrary();
        var id = Import(library, "track-zero-anchor-reject");
        const string original = "[00:00.000]\n[00:00.050]Actual lyric\n[00:10.000]Later\n";
        File.WriteAllText(LrcPath(id), original);
        Assert.That(library.SetGlobalOffset(id, -100).Succeeded, Is.True);
        var lrcBefore = File.ReadAllBytes(LrcPath(id));
        var sidecarBefore = File.ReadAllBytes(SidecarPath(id));

        var result = library.BakeGlobalOffset(id);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(TimingAdjustmentStatus.NegativeTimestamp));
            Assert.That(File.ReadAllBytes(LrcPath(id)), Is.EqualTo(lrcBefore));
            Assert.That(File.ReadAllBytes(SidecarPath(id)), Is.EqualTo(sidecarBefore));
            Assert.That(library.Lookup(id).Document!.GlobalOffsetMs, Is.EqualTo(-100));
        });
    }

    [Test]
    public void SecondFileCommitFailureRollsBackLrcAndKeepsOffset()
    {
        using var library = CreateLibrary(phase =>
        {
            if (phase == 2) throw new IOException("Injected sidecar commit failure.");
        });
        var id = Import(library, "track-a");
        File.WriteAllText(LrcPath(id), "[00:10.000]A\n");
        library.SetGlobalOffset(id, 500);
        var lrcBefore = File.ReadAllText(LrcPath(id));
        var sidecarBefore = File.ReadAllText(SidecarPath(id));

        var result = library.BakeGlobalOffset(id);
        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(TimingAdjustmentStatus.StorageFailure));
            Assert.That(File.ReadAllText(LrcPath(id)), Is.EqualTo(lrcBefore));
            Assert.That(File.ReadAllText(SidecarPath(id)), Is.EqualTo(sidecarBefore));
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(LrcPath(id))!, "*.tmp"), Is.Empty);
        });
    }

    [Test]
    public void FailureBeforeFirstCommitLeavesBothFilesUnchanged()
    {
        using var library = CreateLibrary(phase =>
        {
            if (phase == 1) throw new IOException("Injected pre-commit failure.");
        });
        var id = Import(library, "track-a");
        library.SetGlobalOffset(id, 500);
        var lrcBefore = File.ReadAllBytes(LrcPath(id));
        var sidecarBefore = File.ReadAllBytes(SidecarPath(id));

        var result = library.BakeGlobalOffset(id);
        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(TimingAdjustmentStatus.StorageFailure));
            Assert.That(File.ReadAllBytes(LrcPath(id)), Is.EqualTo(lrcBefore));
            Assert.That(File.ReadAllBytes(SidecarPath(id)), Is.EqualTo(sidecarBefore));
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(LrcPath(id))!, "*.tmp"), Is.Empty);
        });
    }

    [Test]
    public void BakePreservesMetadataMultipleTimestampsUnicodeAndBreakMarker()
    {
        using var library = CreateLibrary();
        var id = Import(library, "track-a");
        const string original = "[ar:Artist]\n[ti:Title]\n\n[00:10.000][00:20.000]繁體 简体 日本語 한국어\n[00:30.000]♪\n";
        File.WriteAllText(LrcPath(id), original);
        library.SetGlobalOffset(id, 500);

        Assert.That(library.BakeGlobalOffset(id).Succeeded, Is.True);
        Assert.That(File.ReadAllText(LrcPath(id)), Is.EqualTo(
            "[ar:Artist]\n[ti:Title]\n\n[00:10.500][00:20.500]繁體 简体 日本語 한국어\n[00:30.500]♪\n"));
    }

    [Test]
    public void BakePreservesExistingUtf8Bom()
    {
        using var library = CreateLibrary();
        var id = Import(library, "track-a");
        File.WriteAllText(LrcPath(id), "[00:10.000]A\n", new System.Text.UTF8Encoding(true));
        library.SetGlobalOffset(id, 500);

        Assert.That(library.BakeGlobalOffset(id).Succeeded, Is.True);
        Assert.That(File.ReadAllBytes(LrcPath(id)).AsSpan().StartsWith(System.Text.Encoding.UTF8.Preamble), Is.True);
    }

    private LyricsLibrary CreateLibrary(Action<int>? beforeBakeCommit = null)
    {
        var library = new LyricsLibrary(_paths, null, beforeBakeCommit);
        Assert.That(library.Initialise().Completed, Is.True);
        return library;
    }

    private string Import(LyricsLibrary library, string sourceTrackId)
    {
        var payload = new LyricsSnapshotPayload(sourceTrackId, true, true, "youtubeMusic",
            [new LyricsLine(10_000, 20_000, "A"), new LyricsLine(20_000, 30_000, "B")], null);
        return library.Import(new TrackInfo(sourceTrackId, "Title", "Artist", null, 60_000), payload).LocalTrackId!;
    }

    private string LrcPath(string id) => Path.Combine(_paths.LibraryPath, "tracks", id, "track.lrc");
    private string SidecarPath(string id) => Path.Combine(_paths.LibraryPath, "tracks", id, "track.lyrics.json");
}
