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

    [TestCase("")]
    [TestCase("   \t")]
    [TestCase("♪")]
    [TestCase("  ♪\t")]
    public void NegativeBakePreservesExactZeroForBlankOrBreakAnchorWithoutTrimmingText(string lyricsText)
    {
        var original = $"[00:00.000]{lyricsText}\r\n[00:13.854]Line A\r\n";

        var result = LrcTimestampRewriter.Rewrite(original, -100);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(result.Content, Is.EqualTo($"[00:00.000]{lyricsText}\r\n[00:13.754]Line A\r\n"));
        });
    }

    [Test]
    public void NegativeBakeProtectsOnlyTheZeroOccurrenceOnAMultiTimestampBreakRow()
    {
        const string original = "[00:00.000][02:00.000] ♪\n";

        var result = LrcTimestampRewriter.Rewrite(original, -100);

        Assert.That(result.Content, Is.EqualTo("[00:00.000][01:59.900] ♪\n"));
    }

    [TestCase("[00:00.000]Hello", -100)]
    [TestCase("[00:00.001]", -100)]
    [TestCase("[00:00.050] ♪", -100)]
    [TestCase("[00:00.099]   ", -100)]
    public void NegativeBakeStillRejectsOrdinaryOrNearZeroTimestamps(string original, long deltaMs)
    {
        var result = LrcTimestampRewriter.Rewrite(original, deltaMs);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ContainsNegativeTimestamp, Is.True);
            Assert.That(result.Content, Is.Null);
        });
    }

    [Test]
    public void ProtectedZeroDoesNotPermitAnotherOccurrenceToBecomeNegative()
    {
        const string original = "[00:00.000]♪\n[00:00.050]Line\n";

        var result = LrcTimestampRewriter.Rewrite(original, -100);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ContainsNegativeTimestamp, Is.True);
            Assert.That(result.Content, Is.Null, "The rewriter returns no partial content on failure.");
        });
    }

    [Test]
    public void MultipleProtectedAnchorsStayZeroButPositiveBakeMovesThemNormally()
    {
        const string original = "[00:00.000]\n[00:00.000] ♪\n[00:10.000]Line\n";

        var negative = LrcTimestampRewriter.Rewrite(original, -100);
        var positive = LrcTimestampRewriter.Rewrite(original, 100);

        Assert.Multiple(() =>
        {
            Assert.That(negative.Content, Is.EqualTo(
                "[00:00.000]\n[00:00.000] ♪\n[00:09.900]Line\n"));
            Assert.That(positive.Content, Is.EqualTo(
                "[00:00.100]\n[00:00.100] ♪\n[00:10.100]Line\n"));
        });
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
