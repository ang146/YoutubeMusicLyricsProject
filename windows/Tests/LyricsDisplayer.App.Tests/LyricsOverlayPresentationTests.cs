using LyricsDisplayer.Core.Protocol;
using LyricsDisplayer.Core.Settings;
using LyricsDisplayer.Core.Timeline;

namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class LyricsOverlayPresentationTests
{
    [Test]
    public void CurrentAndNextMapToPrimaryAndSecondary()
    {
        var state = LyricsOverlayPresentationState.FromLyrics(TimedLyrics(), Position(Line("A"), Line("B")));
        Assert.Multiple(() =>
        {
            Assert.That(state.PrimaryText, Is.EqualTo("A"));
            Assert.That(state.SecondaryText, Is.EqualTo("B"));
        });
    }

    [Test]
    public void BeforeFirstLineLeavesPrimaryEmpty()
    {
        var state = LyricsOverlayPresentationState.FromLyrics(TimedLyrics(), Position(null, Line("第一行 ♪")));
        Assert.Multiple(() =>
        {
            Assert.That(state.PrimaryText, Is.Empty);
            Assert.That(state.SecondaryText, Is.EqualTo("第一行 ♪"));
        });
    }

    [Test]
    public void FinalLineLeavesSecondaryEmpty()
    {
        var state = LyricsOverlayPresentationState.FromLyrics(TimedLyrics(), Position(Line("最後一行"), null));
        Assert.Multiple(() =>
        {
            Assert.That(state.PrimaryText, Is.EqualTo("最後一行"));
            Assert.That(state.SecondaryText, Is.Empty);
        });
    }

    [Test]
    public void PendingUnknownClearsBothFields()
    {
        var state = LyricsOverlayPresentationState.FromLyrics(null, EmptyTimeline());
        Assert.That(state, Is.EqualTo(LyricsOverlayPresentationState.Empty));
    }

    [Test]
    public void ConfirmedUnavailableDisplaysRestrainedStatus()
    {
        var state = LyricsOverlayPresentationState.FromLyrics(Lyrics(available: false, timed: false), EmptyTimeline());
        Assert.That(state, Is.EqualTo(new LyricsOverlayPresentationState(
            LyricsOverlayPresentationState.NoLyricsText, string.Empty)));
    }

    [Test]
    public void AvailableUntimedDisplaysDistinctStatus()
    {
        var state = LyricsOverlayPresentationState.FromLyrics(Lyrics(available: true, timed: false), EmptyTimeline());
        Assert.That(state, Is.EqualTo(new LyricsOverlayPresentationState(
            LyricsOverlayPresentationState.UntimedLyricsText, string.Empty)));
    }

    [Test]
    public void TimedPresentationContainsNoPriorStatusText()
    {
        var unavailable = LyricsOverlayPresentationState.FromLyrics(Lyrics(false, false), EmptyTimeline());
        var timed = LyricsOverlayPresentationState.FromLyrics(TimedLyrics(), Position(Line("A"), Line("B")));

        Assert.Multiple(() =>
        {
            Assert.That(unavailable.PrimaryText, Is.EqualTo(LyricsOverlayPresentationState.NoLyricsText));
            Assert.That(timed.PrimaryText, Is.EqualTo("A"));
            Assert.That(timed.SecondaryText, Is.EqualTo("B"));
            Assert.That(timed.PrimaryText, Is.Not.EqualTo(LyricsOverlayPresentationState.NoLyricsText));
            Assert.That(timed.PrimaryText, Is.Not.EqualTo(LyricsOverlayPresentationState.UntimedLyricsText));
        });
    }

    [Test]
    public void OneLineModeUsesUpcomingLineBeforeFirstTimestamp()
    {
        var state = LyricsOverlayPresentationState.FromLyrics(
            TimedLyrics(), Position(null, Line("First")), LyricsContentMode.OneLine);
        Assert.Multiple(() =>
        {
            Assert.That(state.PrimaryText, Is.EqualTo("First"));
            Assert.That(state.SecondaryText, Is.Empty);
            Assert.That(state.ContentMode, Is.EqualTo(LyricsContentMode.OneLine));
        });
    }

    [TestCase(false, false, LyricsOverlayPresentationState.NoLyricsText)]
    [TestCase(true, false, LyricsOverlayPresentationState.UntimedLyricsText)]
    public void OneLineModePreservesStatusPresentation(bool available, bool timed, string expected)
    {
        var state = LyricsOverlayPresentationState.FromLyrics(
            Lyrics(available, timed), EmptyTimeline(), LyricsContentMode.OneLine);
        Assert.Multiple(() =>
        {
            Assert.That(state.PrimaryText, Is.EqualTo(expected));
            Assert.That(state.SecondaryText, Is.Empty);
        });
    }

    [Test]
    public void AllLyricsModeUsesNearbyContextWithPastAndUpcomingRoles()
    {
        var lyrics = new LyricsSnapshotPayload("track", true, true, "local",
            [Line("A"), Line("B"), Line("C"), Line("D"), Line("E")], null);
        var state = LyricsOverlayPresentationState.FromLyrics(lyrics,
            new(2, Line("C"), 3, Line("D")), LyricsContentMode.AllLyrics);

        Assert.Multiple(() =>
        {
            Assert.That(state.ContentMode, Is.EqualTo(LyricsContentMode.AllLyrics));
            Assert.That(state.AllLines.Select(line => line.Text), Does.Contain("C"));
            Assert.That(state.AllLines.Single(line => line.Index == 2).Role, Is.EqualTo(LyricLineRole.Current));
            Assert.That(state.AllLines.Where(line => line.Index < 2).All(line => line.Role == LyricLineRole.Past), Is.True);
            Assert.That(state.AllLines.Where(line => line.Index > 2).All(line => line.Role == LyricLineRole.Upcoming), Is.True);
        });
    }

    [TestCase(false, false, LyricsOverlayPresentationState.NoLyricsText)]
    [TestCase(true, false, LyricsOverlayPresentationState.UntimedLyricsText)]
    public void AllLyricsModeUsesStatusInsteadOfAnEmptyLyricsList(bool available, bool timed, string expected)
    {
        var state = LyricsOverlayPresentationState.FromLyrics(
            Lyrics(available, timed), EmptyTimeline(), LyricsContentMode.AllLyrics);
        Assert.Multiple(() =>
        {
            Assert.That(state.PrimaryText, Is.EqualTo(expected));
            Assert.That(state.AllLines, Is.Empty);
        });
    }

    [Test]
    public void AllLyricsViewportIsBoundedAndGrowsWithHeight()
    {
        var lines = Enumerable.Range(0, 200).Select(index => Line($"line {index}")).ToArray();
        var small = LyricsOverlayPresentationState.FromLyrics(
            LyricsWith(lines), new(100, lines[100], 101, lines[101]), LyricsContentMode.AllLyrics, 900, 150);
        var large = LyricsOverlayPresentationState.FromLyrics(
            LyricsWith(lines), new(100, lines[100], 101, lines[101]), LyricsContentMode.AllLyrics, 900, 500);

        Assert.Multiple(() =>
        {
            Assert.That(small.AllLines.Count, Is.LessThan(200));
            Assert.That(small.AllLines.Select(line => line.Index), Does.Contain(100));
            Assert.That(large.AllLines.Count, Is.GreaterThanOrEqualTo(small.AllLines.Count));
            Assert.That(large.AllLines.Select(line => line.Index).Max() - large.AllLines.Select(line => line.Index).Min(),
                Is.LessThan(200));
        });
    }

    [Test]
    public void WiderViewportAccountsForLongWrappedContextLines()
    {
        var lines = Enumerable.Range(0, 20)
            .Select(index => new LyricsLine(index * 1000, index * 1000 + 999,
                $"line {index} with a deliberately long lyric phrase that wraps when the window is narrow"))
            .ToArray();
        var narrow = LyricsContextWindow.Select(lines, 10, 11, 420, 420);
        var wide = LyricsContextWindow.Select(lines, 10, 11, 1200, 420);
        Assert.That(wide.Count, Is.GreaterThanOrEqualTo(narrow.Count));
    }

    [Test]
    public void DuplicateTextAndTimestampsKeepTheTimelineSelectedOccurrence()
    {
        var lines = new[] { new LyricsLine(1000, 2000, "same"), new LyricsLine(1000, 2000, "same"),
            new LyricsLine(2000, 3000, "later") };
        var timeline = new LyricsTimeline(lines).Evaluate(1000);
        var state = LyricsOverlayPresentationState.FromLyrics(LyricsWith(lines), timeline,
            LyricsContentMode.AllLyrics, 900, 240);

        Assert.Multiple(() =>
        {
            Assert.That(timeline.CurrentIndex, Is.EqualTo(1));
            Assert.That(state.AllLines.Single(line => line.Role == LyricLineRole.Current).Index, Is.EqualTo(1));
            Assert.That(state.AllLines.Single(line => line.Index == 0).Role, Is.EqualTo(LyricLineRole.Past));
        });
    }

    [Test]
    public void SeekImmediatelyMovesContextAndBeforeFirstShowsUpcomingOnly()
    {
        var lines = Enumerable.Range(0, 100).Select(index => Line($"line {index}")).ToArray();
        var lyrics = LyricsWith(lines);
        var beforeSeek = LyricsOverlayPresentationState.FromLyrics(lyrics,
            new(10, lines[10], 11, lines[11]), LyricsContentMode.AllLyrics, 900, 240);
        var afterSeek = LyricsOverlayPresentationState.FromLyrics(lyrics,
            new(80, lines[80], 81, lines[81]), LyricsContentMode.AllLyrics, 900, 240);
        var beforeFirst = LyricsOverlayPresentationState.FromLyrics(lyrics,
            new(null, null, 0, lines[0]), LyricsContentMode.AllLyrics, 900, 240);

        Assert.Multiple(() =>
        {
            Assert.That(afterSeek.AllLines.Single(line => line.Role == LyricLineRole.Current).Index, Is.EqualTo(80));
            Assert.That(afterSeek.AllLines.Any(line => line.Index < 70), Is.False);
            Assert.That(beforeSeek.AllLines.Any(line => line.Index >= 70), Is.False);
            Assert.That(beforeFirst.AllLines.All(line => line.Role == LyricLineRole.Upcoming), Is.True);
            Assert.That(beforeFirst.AllLines.Any(line => line.Index < 0), Is.False);
        });
    }

    [Test]
    public void EndOfLyricsUsesAvailablePastContextWithoutBlankRows()
    {
        var lines = Enumerable.Range(0, 6).Select(index => Line($"line {index}")).ToArray();
        var state = LyricsOverlayPresentationState.FromLyrics(LyricsWith(lines),
            new(5, lines[5], null, null), LyricsContentMode.AllLyrics, 900, 400);
        Assert.Multiple(() =>
        {
            Assert.That(state.AllLines.Select(line => line.Index), Is.Ordered);
            Assert.That(state.AllLines.Any(line => line.Role == LyricLineRole.Upcoming), Is.False);
            Assert.That(state.AllLines.Any(line => string.IsNullOrWhiteSpace(line.Text)), Is.False);
        });
    }

    [Test]
    public void DistanceFalloffIsMonotonicAndHasReadableLowerBounds()
    {
        var current = LyricsPresentationDefaults.ForDistance(0);
        var one = LyricsPresentationDefaults.ForDistance(1);
        var two = LyricsPresentationDefaults.ForDistance(2);
        var distant = LyricsPresentationDefaults.ForDistance(100);
        Assert.Multiple(() =>
        {
            Assert.That(current.Scale, Is.GreaterThan(one.Scale));
            Assert.That(one.Scale, Is.GreaterThanOrEqualTo(two.Scale));
            Assert.That(current.Opacity, Is.GreaterThan(one.Opacity));
            Assert.That(one.Opacity, Is.GreaterThanOrEqualTo(two.Opacity));
            Assert.That(distant.Scale, Is.GreaterThanOrEqualTo(LyricsPresentationDefaults.ContextMinimumScale));
            Assert.That(distant.Opacity, Is.GreaterThanOrEqualTo(LyricsPresentationDefaults.ContextMinimumOpacity));
        });
    }

    private static LyricsTimelinePosition Position(LyricsLine? current, LyricsLine? next) =>
        new(current is null ? null : 0, current, next is null ? null : 1, next);

    private static LyricsTimelinePosition EmptyTimeline() => new(null, null, null, null);
    private static LyricsSnapshotPayload TimedLyrics() => Lyrics(available: true, timed: true);
    private static LyricsSnapshotPayload LyricsWith(IReadOnlyList<LyricsLine> lines) =>
        new("track", true, true, "local", lines, null);
    private static LyricsSnapshotPayload Lyrics(bool available, bool timed) =>
        new("track", available, timed, available ? "youtubeMusic" : null, [], null);
    private static LyricsLine Line(string text) => new(0, 1, text);
}
