using LyricsDisplayer.Core.Protocol;
using LyricsDisplayer.Core.Timeline;

namespace LyricsDisplayer.Core.Tests;

[TestFixture]
public sealed class LyricsTimingAdjustmentTests
{
    private static readonly LyricsTimeline Timeline = new(
    [
        new LyricsLine(10_000, 20_000, "A"),
        new LyricsLine(20_000, 30_000, "B"),
        new LyricsLine(30_000, 30_000, "C")
    ]);

    [TestCase(9_999, null, "A")]
    [TestCase(10_000, "A", "B")]
    [TestCase(20_000, "B", "C")]
    public void ZeroOffsetPreservesMilestoneSixSemantics(long playbackMs, string? current, string? next) =>
        AssertPosition(playbackMs, 0, current, next);

    [TestCase(10_499, null, "A")]
    [TestCase(10_500, "A", "B")]
    [TestCase(20_499, "A", "B")]
    [TestCase(20_500, "B", "C")]
    public void PositiveFiveHundredMillisecondsMakesLyricsLater(
        long playbackMs, string? current, string? next) =>
        AssertPosition(playbackMs, 500, current, next);

    [TestCase(9_499, null, "A")]
    [TestCase(9_500, "A", "B")]
    [TestCase(19_499, "A", "B")]
    [TestCase(19_500, "B", "C")]
    [TestCase(29_500, "C", null)]
    public void NegativeFiveHundredMillisecondsMakesLyricsEarlier(
        long playbackMs, string? current, string? next) =>
        AssertPosition(playbackMs, -500, current, next);

    [Test]
    public void IncrementButtonsUseExactIntegerArithmetic()
    {
        long offset = 0;
        foreach (var delta in new long[] { 500, 100, -500, -100 })
        {
            Assert.That(LyricsTimingAdjustment.TryAdjustOffset(offset, delta, out offset), Is.True);
        }
        Assert.That(offset, Is.Zero);
    }

    [Test]
    public void OffsetArithmeticOverflowIsRejectedWithoutChangingValue()
    {
        Assert.That(LyricsTimingAdjustment.TryAdjustOffset(long.MaxValue, 1, out var result), Is.False);
        Assert.That(result, Is.EqualTo(long.MaxValue));
    }

    private static void AssertPosition(long playbackMs, long offsetMs, string? current, string? next)
    {
        var position = Timeline.Evaluate(LyricsTimingAdjustment.GetEvaluationPosition(playbackMs, offsetMs));
        Assert.Multiple(() =>
        {
            Assert.That(position.CurrentLine?.Text, Is.EqualTo(current));
            Assert.That(position.NextLine?.Text, Is.EqualTo(next));
        });
    }
}
