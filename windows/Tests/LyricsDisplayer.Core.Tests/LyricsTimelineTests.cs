using LyricsDisplayer.Core.Protocol;
using LyricsDisplayer.Core.Timeline;

namespace LyricsDisplayer.Core.Tests;

[TestFixture]
public sealed class LyricsTimelineTests
{
    private static readonly IReadOnlyList<LyricsLine> StandardLines =
    [
        Line(10_000, "A"), Line(15_000, "B"), Line(20_000, "C")
    ];

    [TestCase(-1, null, 0)]
    [TestCase(0, null, 0)]
    [TestCase(9_999, null, 0)]
    [TestCase(10_000, 0, 1)]
    [TestCase(10_001, 0, 1)]
    [TestCase(14_999, 0, 1)]
    [TestCase(15_000, 1, 2)]
    [TestCase(19_999, 1, 2)]
    [TestCase(20_000, 2, null)]
    [TestCase(999_999, 2, null)]
    public void BoundariesSelectGreatestStartNotAfterPosition(long position, int? current, int? next)
    {
        var result = new LyricsTimeline(StandardLines).Evaluate(position);
        Assert.Multiple(() =>
        {
            Assert.That(result.CurrentIndex, Is.EqualTo(current));
            Assert.That(result.NextIndex, Is.EqualTo(next));
            Assert.That(result.CurrentLine, Is.EqualTo(current is null ? null : StandardLines[current.Value]));
            Assert.That(result.NextLine, Is.EqualTo(next is null ? null : StandardLines[next.Value]));
        });
    }

    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(long.MaxValue)]
    public void EmptyTimelineIsSafeAtAnyPosition(long position)
    {
        var result = LyricsTimeline.Empty.Evaluate(position);
        Assert.Multiple(() =>
        {
            Assert.That(result.HasLyrics, Is.False);
            Assert.That(result.CurrentIndex, Is.Null);
            Assert.That(result.CurrentLine, Is.Null);
            Assert.That(result.NextIndex, Is.Null);
            Assert.That(result.NextLine, Is.Null);
        });
    }

    [TestCase(9_999, null, 0)]
    [TestCase(10_000, 0, null)]
    [TestCase(50_000, 0, null)]
    public void SingleLineHasExpectedBeforeAndAfterStates(long position, int? current, int? next)
    {
        var result = new LyricsTimeline([Line(10_000, "only")]).Evaluate(position);
        Assert.Multiple(() =>
        {
            Assert.That(result.CurrentIndex, Is.EqualTo(current));
            Assert.That(result.NextIndex, Is.EqualTo(next));
        });
    }

    [Test]
    public void EvaluationsAreIndependentAcrossForwardAndBackwardSeeks()
    {
        var timeline = new LyricsTimeline(
            [Line(10_000, "A"), Line(15_000, "B"), Line(60_000, "C"), Line(90_000, "D")]);
        Assert.That(new[] { 10_000L, 60_000, 15_000, 90_000 }
            .Select(position => timeline.Evaluate(position).CurrentLine!.Text),
            Is.EqualTo(new[] { "A", "C", "B", "D" }));
    }

    [Test]
    public void EqualStartsSelectLastLineInStableDocumentOrder()
    {
        var timeline = new LyricsTimeline([Line(10_000, "A"), Line(10_000, "B"), Line(15_000, "C")]);
        var before = timeline.Evaluate(9_999);
        var atDuplicate = timeline.Evaluate(10_000);
        Assert.Multiple(() =>
        {
            Assert.That(before.NextLine!.Text, Is.EqualTo("A"));
            Assert.That(atDuplicate.CurrentIndex, Is.EqualTo(1));
            Assert.That(atDuplicate.CurrentLine!.Text, Is.EqualTo("B"));
            Assert.That(atDuplicate.NextLine!.Text, Is.EqualTo("C"));
        });
    }

    [Test]
    public void ConstructionStableSortsUnsortedInputOnce()
    {
        var timeline = new LyricsTimeline([Line(20_000, "C"), Line(10_000, "A"), Line(15_000, "B")]);
        Assert.Multiple(() =>
        {
            Assert.That(timeline.OrderedLines.Select(line => line.Text), Is.EqualTo(new[] { "A", "B", "C" }));
            Assert.That(timeline.Evaluate(10_000).CurrentLine!.Text, Is.EqualTo("A"));
            Assert.That(timeline.Evaluate(15_000).CurrentLine!.Text, Is.EqualTo("B"));
            Assert.That(timeline.Evaluate(20_000).CurrentLine!.Text, Is.EqualTo("C"));
        });
    }

    [Test]
    public void EmptyTextAndLineEndDoNotAffectSelection()
    {
        var line = new LyricsLine(10_000, 10_001, string.Empty);
        var result = new LyricsTimeline([line]).Evaluate(50_000);
        Assert.Multiple(() =>
        {
            Assert.That(result.CurrentLine, Is.SameAs(line));
            Assert.That(result.CurrentLine!.Text, Is.Empty);
            Assert.That(result.NextLine, Is.Null);
        });
    }

    [Test]
    public void LongMaxValueSelectsFinalLineWithoutOverflow()
    {
        var final = Line(long.MaxValue - 1, "final");
        Assert.That(new LyricsTimeline([Line(1, "first"), final]).Evaluate(long.MaxValue).CurrentLine,
            Is.SameAs(final));
    }

    private static LyricsLine Line(long startMs, string text) => new(startMs, startMs, text);
}
