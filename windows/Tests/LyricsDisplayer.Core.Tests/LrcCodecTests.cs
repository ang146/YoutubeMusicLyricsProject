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
        Assert.That(result.Lines.Select(line => (line.StartMs, line.Text)),
            Is.EqualTo(new[] { (10_000L, "Same text"), (20_000L, "Same text") }));
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
        Assert.That(LrcCodec.Parse("metadata only").Success, Is.False);
    }
}
