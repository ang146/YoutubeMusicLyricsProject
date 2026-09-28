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
    public void AllLyricsModeAssignsPastCurrentUpcomingRolesInDocumentOrder()
    {
        var lyrics = new LyricsSnapshotPayload("track", true, true, "local",
            [Line("A"), Line("B"), Line("C"), Line("D"), Line("E")], null);
        var state = LyricsOverlayPresentationState.FromLyrics(lyrics,
            new(2, Line("C"), 3, Line("D")), LyricsContentMode.AllLyrics);

        Assert.Multiple(() =>
        {
            Assert.That(state.ContentMode, Is.EqualTo(LyricsContentMode.AllLyrics));
            Assert.That(state.AllLines.Select(line => line.Text), Is.EqualTo(new[] { "A", "B", "C", "D", "E" }));
            Assert.That(state.AllLines.Select(line => line.Role), Is.EqualTo(new[]
            {
                LyricLineRole.Past, LyricLineRole.Past, LyricLineRole.Current,
                LyricLineRole.Upcoming, LyricLineRole.Upcoming
            }));
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

    private static LyricsTimelinePosition Position(LyricsLine? current, LyricsLine? next) =>
        new(current is null ? null : 0, current, next is null ? null : 1, next);

    private static LyricsTimelinePosition EmptyTimeline() => new(null, null, null, null);
    private static LyricsSnapshotPayload TimedLyrics() => Lyrics(available: true, timed: true);
    private static LyricsSnapshotPayload Lyrics(bool available, bool timed) =>
        new("track", available, timed, available ? "youtubeMusic" : null, [], null);
    private static LyricsLine Line(string text) => new(0, 1, text);
}
