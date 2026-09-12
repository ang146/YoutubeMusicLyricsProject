using LyricsDisplayer.Core.Library;

namespace LyricsDisplayer.Core.Tests;

[TestFixture]
public sealed class LrcTimestampRewriterTests
{
    [Test]
    public void PositiveAndNegativeOffsetsUseTheDocumentedSign()
    {
        Assert.Multiple(() =>
        {
            Assert.That(LrcTimestampRewriter.Rewrite("[00:10.000]A", 500).Content,
                Is.EqualTo("[00:10.500]A"));
            Assert.That(LrcTimestampRewriter.Rewrite("[00:10.000]A", -500).Content,
                Is.EqualTo("[00:09.500]A"));
        });
    }

    [Test]
    public void MetadataBlankLinesMultipleTimestampsAndUnicodeArePreserved()
    {
        const string original = "[ar:Artist]\r\n[ti:Title]\r\n[al:Album]\r\n\r\n" +
                                "[00:10.00][00:20.000]繁體 简体 日本語 한국어 ♪\r\n";
        var result = LrcTimestampRewriter.Rewrite(original, 500);
        Assert.That(result.Content, Is.EqualTo("[ar:Artist]\r\n[ti:Title]\r\n[al:Album]\r\n\r\n" +
                                               "[00:10.500][00:20.500]繁體 简体 日本語 한국어 ♪\r\n"));
    }

    [Test]
    public void ExplicitBreakMarkerIsRetimedLikeAnyOtherLine()
    {
        const string original = "[00:10.000]xxxx\n[00:13.000]♪\n[00:20.000]yyyy\n";
        Assert.That(LrcTimestampRewriter.Rewrite(original, 500).Content,
            Is.EqualTo("[00:10.500]xxxx\n[00:13.500]♪\n[00:20.500]yyyy\n"));
    }

    [Test]
    public void NegativeResultIsRejectedWithoutProducingContent()
    {
        var result = LrcTimestampRewriter.Rewrite("[00:00.200]A", -500);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ContainsNegativeTimestamp, Is.True);
            Assert.That(result.Content, Is.Null);
        });
    }

    [Test]
    public void OverflowIsRejectedWithoutProducingContent()
    {
        var timestamp = $"[{long.MaxValue / 60_000:00}:00.000]A";
        var result = LrcTimestampRewriter.Rewrite(timestamp, long.MaxValue);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ContainsOverflow, Is.True);
            Assert.That(result.Content, Is.Null);
        });
    }

    [Test]
    public void OverflowAmongValidTimestampsRejectsTheWholeRewrite()
    {
        var result = LrcTimestampRewriter.Rewrite(
            "[00:10.000]A\n[999999999999999999999:00.000]B\n", 500);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ContainsOverflow, Is.True);
            Assert.That(result.Content, Is.Null);
        });
    }
}
