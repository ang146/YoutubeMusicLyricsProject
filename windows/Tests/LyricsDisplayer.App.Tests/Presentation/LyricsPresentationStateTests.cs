using LyricsDisplayer.Core.Protocol;
using LyricsDisplayer.Core.Timeline;

namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class LyricsPresentationStateTests
{
    [Test]
    public void BeforeFirstTimestampHasNoCurrentAndMarksFirstLineUpcoming()
    {
        var sourceLines = new[] { Line(1_000, "First"), Line(2_000, "Second") };
        var timeline = new LyricsTimeline(sourceLines);
        var position = timeline.Evaluate(0);

        var state = LyricsPresentationMapper.FromResolvedTimeline(
            TimedLyrics(sourceLines), position, timeline.OrderedLines);

        Assert.Multiple(() =>
        {
            Assert.That(state.Status, Is.EqualTo(LyricsPresentationStatus.Timed));
            Assert.That(state.CurrentLine, Is.Null);
            Assert.That(state.NextLine?.Text, Is.EqualTo("First"));
            Assert.That(state.Lines.Select(line => line.Role),
                Is.EqualTo(new[] { LyricLineRole.Upcoming, LyricLineRole.Upcoming }));
        });
    }

    [Test]
    public void ResolvedCurrentClassifiesFullDocumentAsPastCurrentAndUpcoming()
    {
        var sourceLines = new[] { Line(1_000, "Past"), Line(2_000, "Current"), Line(3_000, "Upcoming") };
        var timeline = new LyricsTimeline(sourceLines);
        var state = LyricsPresentationMapper.FromResolvedTimeline(
            TimedLyrics(sourceLines), timeline.Evaluate(2_000), timeline.OrderedLines);

        Assert.Multiple(() =>
        {
            Assert.That(state.CurrentIndex, Is.EqualTo(1));
            Assert.That(state.CurrentLine?.Text, Is.EqualTo("Current"));
            Assert.That(state.Lines.Select(line => line.Role), Is.EqualTo(new[]
            {
                LyricLineRole.Past, LyricLineRole.Current, LyricLineRole.Upcoming
            }));
            Assert.That(state.Lines.Select(line => line.Distance), Is.EqualTo(new[] { 1, 0, 1 }));
        });
    }

    [Test]
    public void FullPresentationPreservesProvidedOrderAndEveryLineWithoutViewportTruncation()
    {
        var orderedLines = new[]
        {
            Line(2_000, "source-order first"),
            Line(1_000, "source-order second"),
            Line(3_000, string.Empty)
        };
        var timeline = new LyricsTimeline(orderedLines);
        var resolvedPosition = timeline.Evaluate(2_000);

        var state = LyricsPresentationMapper.FromResolvedTimeline(
            TimedLyrics(orderedLines), resolvedPosition, timeline.OrderedLines);

        Assert.Multiple(() =>
        {
            Assert.That(state.Lines.Count, Is.EqualTo(3));
            Assert.That(state.Lines.Select(line => line.Text),
                Is.EqualTo(new[] { "source-order first", "source-order second", string.Empty }));
            Assert.That(state.Lines.Select(line => line.Index), Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(state.Lines[^1].StartMs, Is.EqualTo(3_000));
            Assert.That(state.CurrentLine?.Text, Is.EqualTo("source-order first"));
            Assert.That(state.CurrentIndex, Is.EqualTo(0));
            Assert.That(state.CurrentTimelineIndex, Is.EqualTo(1));
            Assert.That(state.NextLine?.Text, Is.Empty);
            Assert.That(state.Lines.Select(line => line.Role), Is.EqualTo(new[]
            {
                LyricLineRole.Current, LyricLineRole.Past, LyricLineRole.Upcoming
            }));
        });
    }

    [Test]
    public void UntimedPresentationPreservesAvailableTextAndBlankRowsWithoutInventingCurrentLine()
    {
        var lyrics = new LyricsSnapshotPayload("track", true, false, "local", [], null,
            ["First", string.Empty, "Last"]);

        var state = LyricsPresentationMapper.FromResolvedTimeline(
            lyrics, LyricsTimeline.Empty.Evaluate(0), []);

        Assert.Multiple(() =>
        {
            Assert.That(state.Status, Is.EqualTo(LyricsPresentationStatus.Untimed));
            Assert.That(state.Lines.Select(line => line.Text), Is.EqualTo(new[] { "First", string.Empty, "Last" }));
            Assert.That(state.Lines.All(line => line.Role == LyricLineRole.Neutral), Is.True);
            Assert.That(state.CurrentLine, Is.Null);
            Assert.That(state.NextLine, Is.Null);
        });
    }

    [Test]
    public void PendingUnavailableUntimedMissingAndTimedRemainDistinctStatuses()
    {
        var timedLines = new[] { Line(1_000, "Timed") };
        var position = new LyricsTimeline(timedLines).Evaluate(1_000);

        var pending = LyricsPresentationMapper.FromResolvedTimeline(null, LyricsTimeline.Empty.Evaluate(0), []);
        var unavailable = LyricsPresentationMapper.FromResolvedTimeline(
            new("track", false, false, null, [], null), LyricsTimeline.Empty.Evaluate(0), []);
        var untimed = LyricsPresentationMapper.FromResolvedTimeline(
            new("track", true, false, "local", [], null, ["text"]), LyricsTimeline.Empty.Evaluate(0), []);
        var missing = LyricsPresentationMapper.FromResolvedTimeline(
            null, LyricsTimeline.Empty.Evaluate(0), [], localFileMissing: true);
        var timed = LyricsPresentationMapper.FromResolvedTimeline(
            TimedLyrics(timedLines), position, timedLines);

        Assert.That(new[] { pending.Status, unavailable.Status, untimed.Status, missing.Status, timed.Status },
            Is.EqualTo(new[]
            {
                LyricsPresentationStatus.Pending,
                LyricsPresentationStatus.Unavailable,
                LyricsPresentationStatus.Untimed,
                LyricsPresentationStatus.LocalFileMissing,
                LyricsPresentationStatus.Timed
            }));
    }

    [Test]
    public void DuplicateTimestampCurrentKeepsExactResolvedOccurrenceIdentity()
    {
        var duplicate = Line(1_000, "same");
        var sourceLines = new[] { duplicate, Line(1_000, "same"), Line(2_000, "later") };
        var timeline = new LyricsTimeline(sourceLines);

        var state = LyricsPresentationMapper.FromResolvedTimeline(
            TimedLyrics(sourceLines), timeline.Evaluate(1_000), timeline.OrderedLines);

        Assert.Multiple(() =>
        {
            Assert.That(state.CurrentIndex, Is.EqualTo(1));
            Assert.That(state.Lines[0].Role, Is.EqualTo(LyricLineRole.Past));
            Assert.That(state.Lines[1].Role, Is.EqualTo(LyricLineRole.Current));
            Assert.That(state.CurrentLine?.Index, Is.EqualTo(1));
        });
    }

    private static LyricsSnapshotPayload TimedLyrics(IReadOnlyList<LyricsLine> lines) =>
        new("track", true, true, "local", lines, null);

    private static LyricsLine Line(long startMs, string text) => new(startMs, startMs, text);
}
