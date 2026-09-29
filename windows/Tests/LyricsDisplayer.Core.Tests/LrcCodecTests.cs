using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Core.Protocol;

namespace LyricsDisplayer.Core.Tests;

[TestFixture]
public sealed class LrcCodecTests
{
    [Test]
    public void SerializesMillisecondPrecision()
    {
        Assert.That(LrcCodec.Serialize([new LyricsLine(12_340, 15_000, "時間 一眨下眼就過")]),
            Is.EqualTo("[00:12.340]時間 一眨下眼就過\n"));
    }

    [Test]
    public void UnicodeRoundTrips()
    {
        var lines = new[]
        {
            new LyricsLine(1_000, 2_000, "繁體中文"), new LyricsLine(2_000, 3_000, "简体中文"),
            new LyricsLine(3_000, 4_000, "日本語"), new LyricsLine(4_000, 5_000, "한국어")
        };
        var parsed = LrcCodec.Parse(LrcCodec.Serialize(lines), 5_000);
        Assert.That(parsed.Success, Is.True);
        Assert.That(parsed.Lines.Select(line => line.Text), Is.EqualTo(lines.Select(line => line.Text)));
    }

    [TestCase("[00:12.34]text", 12_340)]
    [TestCase("[00:12.340]text", 12_340)]
    public void ParsesHundredthsAndMilliseconds(string text, long expected)
    {
        Assert.That(LrcCodec.Parse(text).Lines.Single().StartMs, Is.EqualTo(expected));
    }

    [Test]
    public void MetadataTagsAreNotLyricsAndMultipleTimestampsExpand()
    {
        var result = LrcCodec.Parse("[ar:Artist]\n[ti:Title]\n[al:Album]\n[00:10.000][00:20.000]Same text", 30_000);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.HasTimedLyrics, Is.True);
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(result.Lines.Select(line => (line.StartMs, line.Text)),
                Is.EqualTo(new[] { (10_000L, "Same text"), (20_000L, "Same text") }));
        });
    }

    [Test]
    public void BadPhysicalLineDoesNotDestroyValidLines()
    {
        var result = LrcCodec.Parse("not a cue\n[00:01.000]valid", 2_000);
        Assert.Multiple(() => { Assert.That(result.Success, Is.True); Assert.That(result.SkippedPhysicalLines, Is.EqualTo(1)); });
    }

    [Test]
    public void EndTimesComeFromNextStartAndFinalDuration()
    {
        var result = LrcCodec.Parse("[00:01.000]one\n[00:03.000]two", 8_000);
        Assert.That(result.Lines, Is.EqualTo(new[]
        {
            new LyricsLine(1_000, 3_000, "one"), new LyricsLine(3_000, 8_000, "two")
        }));
    }

    [Test]
    public void FinalLineFallsBackToItsStartWithoutReliableDuration()
    {
        Assert.That(LrcCodec.Parse("[00:03.000]last", 2_000).Lines.Single().EndMs, Is.EqualTo(3_000));
        var untimed = LrcCodec.Parse("metadata only");
        Assert.Multiple(() =>
        {
            Assert.That(untimed.Success, Is.True);
            Assert.That(untimed.HasTimedLyrics, Is.False);
            Assert.That(untimed.Diagnostics, Is.Empty);
        });
    }

    [Test]
    public void OrdinaryUntimedTextIsValidButHasNoTimedLines()
    {
        var result = LrcCodec.Parse("Line one\nLine two\n");
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.HasTimedLyrics, Is.False);
            Assert.That(result.Lines, Is.Empty);
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(result.UntimedLines, Is.EqualTo(new[] { "Line one", "Line two" }));
        });
    }

    [Test]
    public void SupportedAndUnknownMetadataRemainHarmlessUntimedContent()
    {
        var result = LrcCodec.Parse("[ar:Artist]\n[ti:Title]\n[unknown:kept harmless]\n[1.0]\nLyrics without timestamps");
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.HasTimedLyrics, Is.False);
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(result.UntimedLines, Does.Contain("Lyrics without timestamps"));
        });
    }

    [Test]
    public void MixedTimedAndUntimedTextUsesExistingAnyTimestampMeansTimedRuleWithoutDroppingText()
    {
        var result = LrcCodec.Parse("[00:10.000]Timed line\nUntimed line\n[ar:Artist]");
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.HasTimedLyrics, Is.True);
            Assert.That(result.Lines.Select(line => line.Text), Is.EqualTo(new[] { "Timed line" }));
            Assert.That(result.UntimedLines, Is.EqualTo(new[] { "Untimed line" }));
        });
    }

    [TestCase("[00:20.000Hello", "timestamp tag is missing its closing bracket.")]
    [TestCase("[00:20.000Hello]", "timestamp tag is malformed.")]
    [TestCase("[0055.000]B", "timestamp tag is malformed.")]
    [TestCase("[00:xx.000]B", "timestamp tag is malformed.")]
    [TestCase("[00:20.xxx]B", "timestamp tag is malformed.")]
    [TestCase("[00::20.000]B", "timestamp tag is malformed.")]
    [TestCase("[00:20..000]B", "timestamp tag is malformed.")]
    public void MalformedTimestampLikeSyntaxProducesFatalLineDiagnostic(string line, string reason)
    {
        var result = LrcCodec.Parse($"[00:10.000]A\n{line}\n[00:30.000]C");
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Lines, Is.Empty, "Fatal syntax must not expose a partial document.");
            Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
            Assert.That(result.Diagnostics[0].PhysicalLineNumber, Is.EqualTo(2));
            Assert.That(result.Diagnostics[0].Message, Does.Contain(reason));
        });
    }

    [Test]
    public void MalformedTokenInMultiTimestampSequenceRejectsWholeDocument()
    {
        var result = LrcCodec.Parse("[01:00.000][0200.000]Chorus\n[03:00.000]Next");
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Lines, Is.Empty);
            Assert.That(result.Diagnostics.Single().PhysicalLineNumber, Is.EqualTo(1));
        });
    }

    [TestCase("[00:60.000]B")]
    [TestCase("[00:80.000]B")]
    public void OutOfRangeTimestampFieldsAreFatalDiagnostics(string line)
    {
        var result = LrcCodec.Parse($"[00:10.000]A\n{line}\n[00:30.000]C");
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Lines, Is.Empty);
            Assert.That(result.Diagnostics.Single().PhysicalLineNumber, Is.EqualTo(2));
            Assert.That(result.Diagnostics.Single().Message, Does.Contain("supported LRC format"));
        });
    }

    [Test]
    public void IntentionalLineDeletionTextChangesTimestampChangesAndValidMultiTimestampRemainValid()
    {
        var result = LrcCodec.Parse("[00:10.000]A\n[00:31.000]Corrected C\n[01:00.000][02:00.000]Chorus");
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(result.Lines.Select(line => (line.StartMs, line.Text)), Is.EqualTo(new[]
            {
                (10_000L, "A"), (31_000L, "Corrected C"), (60_000L, "Chorus"), (120_000L, "Chorus")
            }));
        });
    }
}
