using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Core.Protocol;
using LyricsDisplayer.Core.Timeline;

namespace LyricsDisplayer.Core.Tests;

[TestFixture]
public sealed class CurrentLineTimingStorageTests
{
    private string _root = null!;
    private LibraryPaths _paths = null!;
    private LyricsLibrary _library = null!;
    private string _id = null!;
    private Action? _beforeCommit;
    private string LrcPath => Path.Combine(_paths.LibraryPath, "tracks", _id, "track.lrc");
    private string SidecarPath => Path.Combine(_paths.LibraryPath, "tracks", _id, "track.lyrics.json");

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "LyricsDisplayerTests", Guid.NewGuid().ToString("N"));
        _paths = new(_root, Path.Combine(_root, "settings.json"), Path.Combine(_root, "Lyrics"),
            Path.Combine(_root, "library-index.db"), false);
        _beforeCommit = null;
        _library = new(_paths, null, null, () => _beforeCommit?.Invoke());
        Assert.That(_library.Initialise().Completed, Is.True);
        _id = _library.Import(new("track-a", "Title", "Artist", null, 60_000),
            new("track-a", true, true, "youtubeMusic",
                [new(10_000, 20_000, "A"), new(20_000, 30_000, "B"), new(30_000, 60_000, "C")], null)).LocalTrackId!;
    }

    [TearDown]
    public void TearDown()
    {
        _library.Dispose();
        if (File.Exists(LrcPath)) File.SetAttributes(LrcPath, FileAttributes.Normal);
        Directory.Delete(_root, true);
    }

    private LocalLyricsDocument Load(string content)
    {
        File.WriteAllText(LrcPath, content);
        return _library.Lookup(_id).Document!;
    }

    [TestCase(500, 20_500)]
    [TestCase(-500, 19_500)]
    [TestCase(100, 20_100)]
    [TestCase(-100, 19_900)]
    public void MiddleLineUsesExactIntegerDeltaAndLeavesOtherTokensAndSidecarUnchanged(long delta, long expected)
    {
        var document = Load("[00:10.00]A\n[00:20.000]B\n[00:30.00]C\n");
        var sidecar = File.ReadAllBytes(SidecarPath);
        var result = _library.AdjustLineTiming(new(document, 1), delta);
        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Document!.Lines.Select(line => line.StartMs), Is.EqualTo(new[] { 10_000L, expected, 30_000L }));
            Assert.That(File.ReadAllText(LrcPath), Does.StartWith("[00:10.00]A\n").And.EndWith("[00:30.00]C\n"));
            Assert.That(File.ReadAllBytes(SidecarPath), Is.EqualTo(sidecar));
        });
    }

    [TestCase("[00:10.000]A\n[00:10.200]B\n[00:30.000]C", -500, TimingAdjustmentStatus.PreviousLineBoundary)]
    [TestCase("[00:10.000]A\n[00:19.800]B\n[00:20.000]C", 500, TimingAdjustmentStatus.NextLineBoundary)]
    public void CrossingAdjacentLineIsRejectedWithoutChangingFiles(string content, long delta, TimingAdjustmentStatus status)
    {
        var document = Load(content);
        var before = File.ReadAllBytes(LrcPath);
        Assert.That(_library.AdjustLineTiming(new(document, 1), delta).Status, Is.EqualTo(status));
        Assert.That(File.ReadAllBytes(LrcPath), Is.EqualTo(before));
    }

    [TestCase(-10_000, 10_000, "B")]
    [TestCase(10_000, 30_000, "C")]
    public void EqualAdjacentBoundariesRemainDeterministic(long delta, long boundary, string current)
    {
        var document = Load("[00:10.000]A\n[00:20.000]B\n[00:30.000]C");
        var result = _library.AdjustLineTiming(new(document, 1), delta);
        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Document!.Lines.Select(line => line.Text), Is.EqualTo(new[] { "A", "B", "C" }));
        Assert.That(new LyricsTimeline(result.Document.Lines).Evaluate(boundary).CurrentLine!.Text, Is.EqualTo(current));
    }

    [Test]
    public void FirstLineCannotBecomeNegative()
    {
        var document = Load("[00:00.200]A\n[00:20.000]B");
        Assert.That(_library.AdjustLineTiming(new(document, 0), -500).Status, Is.EqualTo(TimingAdjustmentStatus.NegativeTimestamp));
        Assert.That(File.ReadAllText(LrcPath), Is.EqualTo("[00:00.200]A\n[00:20.000]B"));
    }

    [TestCase(-100, 100)]
    [TestCase(500, 700)]
    public void FirstLineCanMoveWithinItsBounds(long delta, long expected)
    {
        var document = Load("[00:00.200]A\n[00:20.000]B");
        var result = _library.AdjustLineTiming(new(document, 0), delta);
        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Document!.Lines[0].StartMs, Is.EqualTo(expected));
    }

    [Test]
    public void FinalLineHasNoArtificialTrackDurationLimit()
    {
        var document = Load("[00:10.000]A\n[00:59.900]B");
        var result = _library.AdjustLineTiming(new(document, 1), 500);
        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Document!.Lines[1].StartMs, Is.EqualTo(60_400));
    }

    [Test]
    public void MultipleTimestampsOnOnePhysicalLineEditOnlySelectedOccurrence()
    {
        var document = Load("[ar:Artist]\r\n[00:10.00][00:20.000]Same text\r\n");
        Assert.That(_library.AdjustLineTiming(new(document, 1), 500).Succeeded, Is.True);
        Assert.That(File.ReadAllText(LrcPath), Is.EqualTo("[ar:Artist]\r\n[00:10.00][00:20.500]Same text\r\n"));
    }

    [Test]
    public void DuplicateTextIsIdentifiedBySourceOccurrence()
    {
        var document = Load("[00:10.000]Same\n[00:20.000]Same\n");
        Assert.That(_library.AdjustLineTiming(new(document, 1), 100).Succeeded, Is.True);
        Assert.That(File.ReadAllText(LrcPath), Is.EqualTo("[00:10.000]Same\n[00:20.100]Same\n"));
    }

    [Test]
    public void DuplicateTimestampIsIdentifiedBySourceOccurrence()
    {
        var document = Load("[00:10.000]A\n[00:10.000]B\n[00:30.000]C");
        Assert.That(_library.AdjustLineTiming(new(document, 1), 100).Succeeded, Is.True);
        Assert.That(File.ReadAllText(LrcPath), Is.EqualTo("[00:10.000]A\n[00:10.100]B\n[00:30.000]C"));
    }

    [TestCase("繁體中文")]
    [TestCase("简体中文")]
    [TestCase("日本語")]
    [TestCase("한국어")]
    [TestCase("♪")]
    public void MetadataUnknownTagsBlankLinesFormattingAndUnicodeArePreserved(string lyric)
    {
        var content = $"[ar:Artist]\r\n[ti:Title]\r\n[custom:keep]\r\n\r\n[00:10.00]A\r[00:13.000]{lyric}\n";
        var document = Load(content);
        Assert.That(_library.AdjustLineTiming(new(document, 1), 500).Succeeded, Is.True);
        Assert.That(File.ReadAllText(LrcPath), Is.EqualTo(content.Replace("[00:13.000]", "[00:13.500]")));
    }

    [Test]
    public void ParsedOrderMapsBackToExactPhysicalOccurrenceAfterUntimedAndUnsortedLines()
    {
        var content = "[by:Editor]\n[00:30.000]C\ninvalid\n[00:10.000]A\n[00:20.000]B";
        var document = Load(content);
        Assert.That(_library.AdjustLineTiming(new(document, 1), 500).Succeeded, Is.True);
        Assert.That(File.ReadAllText(LrcPath), Is.EqualTo(content.Replace("[00:20.000]", "[00:20.500]")));
    }

    [Test]
    public void GlobalOffsetAndBakeComposeWithDirectLineEdit()
    {
        Load("[00:10.000]A\n[00:20.000]B\n[00:30.000]C");
        var document = _library.SetGlobalOffset(_id, 500).Document!;
        var sidecar = File.ReadAllBytes(SidecarPath);
        var edited = _library.AdjustLineTiming(new(document, 1), 100).Document!;
        Assert.Multiple(() =>
        {
            Assert.That(edited.Lines[1].StartMs, Is.EqualTo(20_100));
            Assert.That(edited.GlobalOffsetMs, Is.EqualTo(500));
            Assert.That(File.ReadAllBytes(SidecarPath), Is.EqualTo(sidecar));
        });
        var before = new LyricsTimeline(edited.Lines).Evaluate(20_600 - edited.GlobalOffsetMs);
        var baked = _library.BakeGlobalOffset(_id).Document!;
        Assert.Multiple(() =>
        {
            Assert.That(baked.Lines[1].StartMs, Is.EqualTo(20_600));
            Assert.That(baked.GlobalOffsetMs, Is.Zero);
            Assert.That(new LyricsTimeline(baked.Lines).Evaluate(20_600).CurrentLine, Is.EqualTo(before.CurrentLine! with { StartMs = 20_600, EndMs = 30_500 }));
        });
        Assert.That(_library.AdjustLineTiming(new(baked, 1), 100).Succeeded, Is.True, "Bake must refresh source occurrences and content identity.");
    }

    [Test]
    public void RestartPersistsEditedLrcAndNeedsNoPerLineMetadata()
    {
        var document = Load("[00:10.000]A\n[00:20.000]B");
        Assert.That(_library.AdjustLineTiming(new(document, 1), 500).Succeeded, Is.True);
        _library.Dispose();
        using var restarted = new LyricsLibrary(_paths);
        Assert.That(restarted.Initialise().Completed, Is.True);
        Assert.That(restarted.Lookup(_id).Document!.Lines[1].StartMs, Is.EqualTo(20_500));
        Assert.That(File.ReadAllText(SidecarPath), Does.Not.Contain("lineOffset").And.Not.Contain("lineIndex"));
    }

    [Test]
    public void ExternalSameLengthAndWriteTimeEditIsDetectedByHashAndSafelyReloaded()
    {
        var document = Load("[00:10.000]A\n[00:20.000]B");
        var writeTime = File.GetLastWriteTimeUtc(LrcPath);
        File.WriteAllText(LrcPath, "[00:10.000]A\n[00:20.000]Z");
        File.SetLastWriteTimeUtc(LrcPath, writeTime);
        var result = _library.AdjustLineTiming(new(document, 1), 500);
        Assert.That(result.Status, Is.EqualTo(TimingAdjustmentStatus.FileChanged));
        Assert.That(result.Document!.Lines[1].Text, Is.EqualTo("Z"));
        Assert.That(File.ReadAllText(LrcPath), Is.EqualTo("[00:10.000]A\n[00:20.000]Z"));
    }

    [Test]
    public void ExternalEditAfterPreparingTemporaryFileIsNotOverwritten()
    {
        var document = Load("[00:10.000]A\n[00:20.000]B");
        _beforeCommit = () => File.WriteAllText(LrcPath, "[00:10.000]External\n[00:20.000]B");
        var result = _library.AdjustLineTiming(new(document, 1), 500);
        Assert.That(result.Status, Is.EqualTo(TimingAdjustmentStatus.FileChanged));
        Assert.That(File.ReadAllText(LrcPath), Is.EqualTo("[00:10.000]External\n[00:20.000]B"));
        Assert.That(Directory.GetFiles(Path.GetDirectoryName(LrcPath)!, "*.tmp"), Is.Empty);
    }

    [Test]
    public void WriteFailureRetainsOriginalBytesAndOffsetAndCleansTemporaryFile()
    {
        File.WriteAllText(LrcPath, "[00:10.000]A\r\n[00:20.000]B", new System.Text.UTF8Encoding(true));
        var document = _library.SetGlobalOffset(_id, 500).Document!;
        var before = File.ReadAllBytes(LrcPath);
        _beforeCommit = () => throw new IOException("Injected replacement failure.");
        Assert.That(_library.AdjustLineTiming(new(document, 1), 500).Status, Is.EqualTo(TimingAdjustmentStatus.StorageFailure));
        Assert.That(File.ReadAllBytes(LrcPath), Is.EqualTo(before));
        Assert.That(_library.Lookup(_id).Document!.GlobalOffsetMs, Is.EqualTo(500));
        Assert.That(Directory.GetFiles(Path.GetDirectoryName(LrcPath)!, "*.tmp"), Is.Empty);
    }

    [Test]
    public void ReadOnlyLrcCannotBeEdited()
    {
        var document = Load("[00:10.000]A\n[00:20.000]B");
        File.SetAttributes(LrcPath, FileAttributes.ReadOnly);
        Assert.That(_library.Lookup(_id).Document!.IsLrcWritable, Is.False);
        Assert.That(_library.AdjustLineTiming(new(document, 1), 500).Status, Is.EqualTo(TimingAdjustmentStatus.StorageFailure));
        Assert.That(File.ReadAllText(LrcPath), Is.EqualTo("[00:10.000]A\n[00:20.000]B"));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void SuccessfulEditPreservesUtf8BomPresenceAndAllOtherBytes(bool bom)
    {
        const string original = "[00:10.00]繁體\r\n[00:20.000]♪\r\n";
        var encoding = new System.Text.UTF8Encoding(bom);
        File.WriteAllText(LrcPath, original, encoding);
        var document = _library.Lookup(_id).Document!;
        Assert.That(_library.AdjustLineTiming(new(document, 1), 100).Succeeded, Is.True);
        byte[] expected = [.. encoding.GetPreamble(), .. encoding.GetBytes(original.Replace("[00:20.000]", "[00:20.100]"))];
        Assert.That(File.ReadAllBytes(LrcPath), Is.EqualTo(expected));
    }

    [Test]
    public void SidecarChangingAssetPathToIdenticalContentDoesNotRetargetCapturedEdit()
    {
        var document = Load("[00:10.000]A\n[00:20.000]B");
        var alternatePath = Path.Combine(Path.GetDirectoryName(LrcPath)!, "alternate.lrc");
        File.Copy(LrcPath, alternatePath);
        File.WriteAllText(SidecarPath, SidecarSerializer.Serialize(document.Sidecar with
        {
            Lyrics = document.Sidecar.Lyrics with { File = "alternate.lrc" }
        }));
        Assert.That(_library.AdjustLineTiming(new(document, 1), 500).Status, Is.EqualTo(TimingAdjustmentStatus.FileChanged));
        Assert.That(File.ReadAllText(LrcPath), Is.EqualTo("[00:10.000]A\n[00:20.000]B"));
        Assert.That(File.ReadAllText(alternatePath), Is.EqualTo("[00:10.000]A\n[00:20.000]B"));
    }

    [Test]
    public void MalformedExternalEncodingIsRejectedWithoutReplacingUnrelatedBytes()
    {
        var document = Load("[00:10.000]A\n[00:20.000]B");
        byte[] changed = [.. System.Text.Encoding.UTF8.GetBytes("[00:10.000]A\n[00:20.000]"), 0xff];
        File.WriteAllBytes(LrcPath, changed);
        Assert.That(_library.AdjustLineTiming(new(document, 1), 100).Status, Is.EqualTo(TimingAdjustmentStatus.FileChanged));
        Assert.That(File.ReadAllBytes(LrcPath), Is.EqualTo(changed));
    }

    [Test]
    public void TimestampOverflowIsRejected()
    {
        var start = long.MaxValue - 100;
        var content = $"[{start / 60_000}:{start % 60_000 / 1000:00}.{start % 1000:000}]A";
        var document = Load(content);
        Assert.That(_library.AdjustLineTiming(new(document, 0), 500).Status, Is.EqualTo(TimingAdjustmentStatus.TimestampOverflow));
        Assert.That(File.ReadAllText(LrcPath), Is.EqualTo(content));
    }

    [Test]
    public void CanonicalTokenLengthChangesRefreshDownstreamSourceLocations()
    {
        var document = Load("[00:10.00]A\n[00:20.00]B\n[00:30.00]C");
        var first = _library.AdjustLineTiming(new(document, 0), 100).Document!;
        var second = _library.AdjustLineTiming(new(first, 1), 100).Document!;
        Assert.That(_library.AdjustLineTiming(new(second, 2), 100).Succeeded, Is.True);
        Assert.That(File.ReadAllText(LrcPath), Is.EqualTo("[00:10.100]A\n[00:20.100]B\n[00:30.100]C"));
    }
}
