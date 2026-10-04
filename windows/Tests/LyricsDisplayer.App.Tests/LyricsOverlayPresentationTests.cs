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
        var state = LyricsOverlayPresentationState.FromLyrics(
            LyricsWith([Line("A"), Line("B")]), Position(Line("A"), Line("B")));
        Assert.Multiple(() =>
        {
            Assert.That(state.PrimaryText, Is.EqualTo("A"));
            Assert.That(state.SecondaryText, Is.EqualTo("B"));
        });
    }

    [Test]
    public void BeforeFirstLineLeavesPrimaryEmpty()
    {
        var state = LyricsOverlayPresentationState.FromLyrics(
            LyricsWith([Line("第一行 ♪")]), Position(null, Line("第一行 ♪")));
        Assert.Multiple(() =>
        {
            Assert.That(state.PrimaryText, Is.Empty);
            Assert.That(state.SecondaryText, Is.EqualTo("第一行 ♪"));
        });
    }

    [Test]
    public void FinalLineLeavesSecondaryEmpty()
    {
        var state = LyricsOverlayPresentationState.FromLyrics(
            LyricsWith([Line("最後一行")]), Position(Line("最後一行"), null));
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

    [TestCase(LyricsContentMode.OneLine)]
    [TestCase(LyricsContentMode.TwoLines)]
    [TestCase(LyricsContentMode.AllLyrics)]
    public void LocalFileMissingHasDistinctStatusInEveryContentMode(LyricsContentMode mode)
    {
        var state = LyricsOverlayPresentationState.FromLyrics(null, EmptyTimeline(), mode, localFileMissing: true);
        Assert.Multiple(() =>
        {
            Assert.That(state.PrimaryText, Is.EqualTo(LyricsOverlayPresentationState.LocalFileMissingText));
            Assert.That(state.SecondaryText, Is.Empty);
            Assert.That(state.ContentMode, Is.EqualTo(mode));
            Assert.That(state.IsLocalFileMissing, Is.True);
            Assert.That(state.AllLines, Is.Empty, "All Lyrics must not retain stale contextual rows.");
        });
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
        var timed = LyricsOverlayPresentationState.FromLyrics(
            LyricsWith([Line("A"), Line("B")]), Position(Line("A"), Line("B")));

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
            LyricsWith([Line("First")]), Position(null, Line("First")), LyricsContentMode.OneLine);
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

    [TestCase(1, new[] { 3 })]
    [TestCase(2, new[] { 3, 4 })]
    [TestCase(3, new[] { 2, 3, 4 })]
    [TestCase(4, new[] { 2, 3, 4, 5 })]
    [TestCase(5, new[] { 1, 2, 3, 4, 5 })]
    [TestCase(6, new[] { 1, 2, 3, 4, 5, 6 })]
    public void AllLyricsCapacityPrefersUpcomingThenAlternatesPast(int capacity, int[] expectedIndexes)
    {
        var lines = "ABCDEFG".Select(character => Line(character.ToString())).ToArray();
        var window = LyricsContextWindow.Select(Shared(lines, 3, 4), 1200, HeightForCapacity(capacity));

        Assert.That(window.Select(line => line.Index), Is.EqualTo(expectedIndexes));
        Assert.That(window.Single(line => line.Role == LyricLineRole.Current).Text, Is.EqualTo("D"));
        Assert.That(window.Where(line => line.Index > 3).All(line => line.Role == LyricLineRole.Upcoming), Is.True);
        Assert.That(window.Where(line => line.Index < 3).All(line => line.Role == LyricLineRole.Past), Is.True);
    }

    [Test]
    public void PrioritySequenceIsCurrentThenAlternatingUpcomingAndPreviousOccurrences()
    {
        var lines = "ABCDEFG".Select(character => Line(character.ToString())).ToArray();
        var priority = LyricsContextWindow.BuildPrioritySequence(Shared(lines, 3, 4));

        Assert.That(priority.Select(line => (line.Index, line.Role)), Is.EqualTo(new[]
        {
            (3, LyricLineRole.Current),
            (4, LyricLineRole.Upcoming),
            (2, LyricLineRole.Past),
            (5, LyricLineRole.Upcoming),
            (1, LyricLineRole.Past),
            (6, LyricLineRole.Upcoming),
            (0, LyricLineRole.Past)
        }));
    }

    [Test]
    public void FittingStopsAtFirstNonFittingPriorityCandidate()
    {
        var lines = "ABCDEFG".Select(character => Line(character.ToString())).ToArray();
        var priority = LyricsContextWindow.BuildPrioritySequence(Shared(lines, 3, 4));
        var heights = new Dictionary<int, double>
        {
            [3] = 20, // Current
            [4] = 100, // Upcoming 1 does not fit
            [2] = 20, // Previous 1 must not replace it
            [5] = 20 // Upcoming 2 must not replace it
        };

        var selected = LyricsContextWindow.FitPriorityPrefix(priority, 80, line => heights[line.Index]);

        Assert.That(selected.Select(line => line.Index), Is.EqualTo(new[] { 3 }));
    }

    [Test]
    public void TallPreviousStopsPrefixBeforeUpcomingTwo()
    {
        var lines = "ABCDEFG".Select(character => Line(character.ToString())).ToArray();
        var priority = LyricsContextWindow.BuildPrioritySequence(Shared(lines, 3, 4));
        var heights = new Dictionary<int, double>
        {
            [3] = 20, // Current
            [4] = 20, // Upcoming 1
            [2] = 100, // Previous 1 is next in priority, but cannot fit
            [5] = 20 // Upcoming 2 must not skip ahead
        };

        var selected = LyricsContextWindow.FitPriorityPrefix(priority, 50, line => heights[line.Index]);

        Assert.That(selected.Select(line => line.Index), Is.EqualTo(new[] { 3, 4 }));
    }

    [Test]
    public void WrappedUpcomingOneCannotBeSkippedForShorterUpcomingTwo()
    {
        var lines = new[]
        {
            Line("previous"), Line("current"),
            Line(new string('漢', 36)), Line("short next")
        };
        var window = LyricsContextWindow.Select(Shared(lines, 1, 2), overlayWidth: 160, overlayHeight: 150);

        Assert.That(window.Select(line => line.Index), Is.EqualTo(new[] { 1 }));
    }

    [Test]
    public void WidthRebuildDropsOrAddsOnlyTheTailOfTheSemanticPrioritySequence()
    {
        var lines = new[]
        {
            Line("previous"), Line("current"),
            Line(new string('漢', 36)), Line("short next")
        };
        var narrow = LyricsContextWindow.Select(Shared(lines, 1, 2), overlayWidth: 160, overlayHeight: 220);
        var wide = LyricsContextWindow.Select(Shared(lines, 1, 2), overlayWidth: 1200, overlayHeight: 220);

        Assert.Multiple(() =>
        {
            Assert.That(narrow.Select(line => line.Index), Is.EqualTo(new[] { 1 }));
            Assert.That(wide.Select(line => line.Index), Is.EqualTo(new[] { 0, 1, 2, 3 }));
        });
    }

    [Test]
    public void AllLyricsAtBeginningUsesUpcomingContextWithoutPastPlaceholders()
    {
        var lines = "ABCDE".Select(character => Line(character.ToString())).ToArray();
        var window = LyricsContextWindow.Select(Shared(lines, 0, 1), 1200, HeightForCapacity(4));

        Assert.That(window.Select(line => line.Index), Is.EqualTo(new[] { 0, 1, 2, 3 }));
        Assert.That(window[0].Role, Is.EqualTo(LyricLineRole.Current));
        Assert.That(window.Skip(1).All(line => line.Role == LyricLineRole.Upcoming), Is.True);
    }

    [Test]
    public void BeforeFirstTimestampSelectsUpcomingOccurrencesInOrder()
    {
        var lines = "ABCDE".Select(character => Line(character.ToString())).ToArray();
        var priority = LyricsContextWindow.BuildPrioritySequence(Shared(lines, null, 0));

        Assert.That(priority.Select(line => (line.Index, line.Role)), Is.EqualTo(new[]
        {
            (0, LyricLineRole.Upcoming), (1, LyricLineRole.Upcoming), (2, LyricLineRole.Upcoming),
            (3, LyricLineRole.Upcoming), (4, LyricLineRole.Upcoming)
        }));
    }

    [Test]
    public void AllLyricsAtFinalLineFillsCapacityWithPastContext()
    {
        var lines = "ABCDE".Select(character => Line(character.ToString())).ToArray();
        var window = LyricsContextWindow.Select(Shared(lines, 4, null), 1200, HeightForCapacity(4));

        Assert.That(window.Select(line => line.Index), Is.EqualTo(new[] { 1, 2, 3, 4 }));
        Assert.That(window[^1].Role, Is.EqualTo(LyricLineRole.Current));
        Assert.That(window.Take(3).All(line => line.Role == LyricLineRole.Past), Is.True);
    }

    [Test]
    public void ResizingOnlyShrinksOrGrowsAtTheEndOfThePriorityPrefix()
    {
        var lines = "ABCDEFG".Select(character => Line(character.ToString())).ToArray();
        var sequence = LyricsContextWindow.BuildPrioritySequence(Shared(lines, 3, 4));
        var capacities = new[] { 6, 5, 4, 3, 2, 1, 2, 3, 4, 5, 6 };
        var expectedIndexes = new Dictionary<int, int[]>
        {
            [1] = [3],
            [2] = [3, 4],
            [3] = [2, 3, 4],
            [4] = [2, 3, 4, 5],
            [5] = [1, 2, 3, 4, 5],
            [6] = [1, 2, 3, 4, 5, 6]
        };

        foreach (var capacity in capacities)
        {
            var prefix = LyricsContextWindow.FitPriorityPrefix(sequence, capacity * 30, _ => 30);
            var displayOrder = prefix.OrderBy(line => line.Index).Select(line => line.Index);
            Assert.That(displayOrder, Is.EqualTo(expectedIndexes[capacity]), $"Capacity {capacity}");
        }
    }

    [Test]
    public void WiderViewportAccountsForLongWrappedContextLines()
    {
        var lines = Enumerable.Range(0, 20)
            .Select(index => new LyricsLine(index * 1000, index * 1000 + 999,
                $"line {index} with a deliberately long lyric phrase that wraps when the window is narrow"))
            .ToArray();
        var narrow = LyricsContextWindow.Select(Shared(lines, 10, 11), 420, 420);
        var wide = LyricsContextWindow.Select(Shared(lines, 10, 11), 1200, 420);
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
    public void AllLyricsSelectsByTimelinePriorityThenRendersInDocumentOrder()
    {
        var documentLines = new[]
        {
            new LyricsLine(2_000, 2_500, "previous"),
            new LyricsLine(3_000, 3_500, "current"),
            new LyricsLine(4_000, 4_500, "next"),
            new LyricsLine(1_000, 1_500, "far previous"),
            new LyricsLine(5_000, 5_500, "far next")
        };
        var timeline = new LyricsTimeline(documentLines);
        var state = LyricsOverlayPresentationState.FromLyrics(LyricsWith(documentLines),
            timeline.Evaluate(3_000), LyricsContentMode.AllLyrics, 1200, HeightForCapacity(3),
            timelineOrderedLines: timeline.OrderedLines);

        Assert.Multiple(() =>
        {
            Assert.That(state.AllLines.Select(line => line.Text),
                Is.EqualTo(new[] { "previous", "current", "next" }));
            Assert.That(state.AllLines.Select(line => line.Role), Is.EqualTo(new[]
            {
                LyricLineRole.Past, LyricLineRole.Current, LyricLineRole.Upcoming
            }));
            Assert.That(state.AllLines.Select(line => line.Index), Is.EqualTo(new[] { 0, 1, 2 }));
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
    public void CurrentHasModestSizeIncreaseAndHighestOpacity()
    {
        var current = LyricsOverlayAppearanceDefaults.ForRoleAndDistance(LyricLineRole.Current, 0);
        var past = LyricsOverlayAppearanceDefaults.ForRoleAndDistance(LyricLineRole.Past, 1);
        var upcoming = LyricsOverlayAppearanceDefaults.ForRoleAndDistance(LyricLineRole.Upcoming, 1);
        var distantPast = LyricsOverlayAppearanceDefaults.ForRoleAndDistance(LyricLineRole.Past, 100);
        var distantUpcoming = LyricsOverlayAppearanceDefaults.ForRoleAndDistance(LyricLineRole.Upcoming, 100);
        Assert.Multiple(() =>
        {
            Assert.That(LyricsOverlayAppearanceDefaults.BaseContextFontSize * current.Scale,
                Is.GreaterThan(LyricsOverlayAppearanceDefaults.BaseContextFontSize));
            Assert.That(current.Scale, Is.InRange(1.05, 1.08));
            Assert.That(current.Opacity, Is.GreaterThanOrEqualTo(past.Opacity));
            Assert.That(current.Opacity, Is.GreaterThanOrEqualTo(upcoming.Opacity));
            Assert.That(current.Opacity, Is.GreaterThanOrEqualTo(distantPast.Opacity));
            Assert.That(current.Opacity, Is.GreaterThanOrEqualTo(distantUpcoming.Opacity));
        });
    }

    [Test]
    public void UpcomingIsStrongerThanPastAtEqualDistanceAndDistanceFalloffRemainsReadable()
    {
        var pastOne = LyricsOverlayAppearanceDefaults.ForRoleAndDistance(LyricLineRole.Past, 1);
        var upcomingOne = LyricsOverlayAppearanceDefaults.ForRoleAndDistance(LyricLineRole.Upcoming, 1);
        var pastTwo = LyricsOverlayAppearanceDefaults.ForRoleAndDistance(LyricLineRole.Past, 2);
        var upcomingTwo = LyricsOverlayAppearanceDefaults.ForRoleAndDistance(LyricLineRole.Upcoming, 2);
        var pastFar = LyricsOverlayAppearanceDefaults.ForRoleAndDistance(LyricLineRole.Past, 100);
        var upcomingFar = LyricsOverlayAppearanceDefaults.ForRoleAndDistance(LyricLineRole.Upcoming, 100);

        Assert.Multiple(() =>
        {
            Assert.That(upcomingOne.Opacity, Is.GreaterThan(pastOne.Opacity));
            Assert.That(upcomingOne.Scale, Is.GreaterThan(pastOne.Scale));
            Assert.That(upcomingOne.Opacity, Is.GreaterThan(upcomingTwo.Opacity));
            Assert.That(upcomingOne.Scale, Is.GreaterThan(upcomingTwo.Scale));
            Assert.That(pastOne.Opacity, Is.GreaterThan(pastTwo.Opacity));
            Assert.That(pastOne.Scale, Is.GreaterThan(pastTwo.Scale));
            Assert.That(upcomingFar.Scale, Is.GreaterThanOrEqualTo(LyricsOverlayAppearanceDefaults.ContextMinimumScale));
            Assert.That(upcomingFar.Opacity, Is.GreaterThanOrEqualTo(LyricsOverlayAppearanceDefaults.ContextMinimumOpacity));
            Assert.That(pastFar.Scale, Is.GreaterThanOrEqualTo(LyricsOverlayAppearanceDefaults.ContextMinimumScale));
            Assert.That(pastFar.Opacity, Is.GreaterThanOrEqualTo(LyricsOverlayAppearanceDefaults.ContextMinimumOpacity));
        });
    }

    private static double HeightForCapacity(int capacity) => 38 + (capacity switch
    {
        1 => 45,
        2 => 90,
        3 => 130,
        4 => 170,
        5 => 210,
        6 => 250,
        _ => throw new ArgumentOutOfRangeException(nameof(capacity))
    });

    private static LyricsTimelinePosition Position(LyricsLine? current, LyricsLine? next) =>
        new(current is null ? null : 0, current, next is null ? null : current is null ? 0 : 1, next);

    private static LyricsTimelinePosition EmptyTimeline() => new(null, null, null, null);
    private static LyricsSnapshotPayload TimedLyrics() => Lyrics(available: true, timed: true);
    private static LyricsSnapshotPayload LyricsWith(IReadOnlyList<LyricsLine> lines) =>
        new("track", true, true, "local", lines, null);
    private static LyricsPresentationState Shared(IReadOnlyList<LyricsLine> lines, int? currentIndex, int? nextIndex)
    {
        var timeline = new LyricsTimelinePosition(
            currentIndex, currentIndex is { } current ? lines[current] : null,
            nextIndex, nextIndex is { } next ? lines[next] : null);
        return LyricsPresentationMapper.FromResolvedTimeline(LyricsWith(lines), timeline, lines);
    }
    private static LyricsSnapshotPayload Lyrics(bool available, bool timed) =>
        new("track", available, timed, available ? "youtubeMusic" : null, [], null);
    private static LyricsLine Line(string text) => new(0, 1, text);
}
