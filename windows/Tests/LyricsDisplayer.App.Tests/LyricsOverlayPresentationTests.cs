using LyricsDisplayer.Core.Protocol;
using LyricsDisplayer.Core.Timeline;

namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class LyricsOverlayPresentationTests
{
    [Test]
    public void CurrentAndNextMapToPrimaryAndSecondary()
    {
        var state = LyricsOverlayPresentationState.FromTimeline(Position(Line("A"), Line("B")));
        Assert.Multiple(() =>
        {
            Assert.That(state.PrimaryText, Is.EqualTo("A"));
            Assert.That(state.SecondaryText, Is.EqualTo("B"));
        });
    }

    [Test]
    public void BeforeFirstLineLeavesPrimaryEmpty()
    {
        var state = LyricsOverlayPresentationState.FromTimeline(Position(null, Line("第一行 ♪")));
        Assert.Multiple(() =>
        {
            Assert.That(state.PrimaryText, Is.Empty);
            Assert.That(state.SecondaryText, Is.EqualTo("第一行 ♪"));
        });
    }

    [Test]
    public void FinalLineLeavesSecondaryEmpty()
    {
        var state = LyricsOverlayPresentationState.FromTimeline(Position(Line("最後一行"), null));
        Assert.Multiple(() =>
        {
            Assert.That(state.PrimaryText, Is.EqualTo("最後一行"));
            Assert.That(state.SecondaryText, Is.Empty);
        });
    }

    [Test]
    public void EmptyTimelineClearsBothFields()
    {
        var state = LyricsOverlayPresentationState.FromTimeline(new LyricsTimelinePosition(null, null, null, null));
        Assert.That(state, Is.EqualTo(LyricsOverlayPresentationState.Empty));
    }

    private static LyricsTimelinePosition Position(LyricsLine? current, LyricsLine? next) =>
        new(current is null ? null : 0, current, next is null ? null : 1, next);

    private static LyricsLine Line(string text) => new(0, 1, text);
}
