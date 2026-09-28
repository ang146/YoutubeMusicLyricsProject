namespace LyricsDisplayer.App.Tests;

using LyricsDisplayer.Core.Settings;

[TestFixture]
public sealed class NativeOverlayClickThroughTests
{
    [TestCase(false, false, false, OverlayHitTestResult.Client)]
    [TestCase(false, true, false, OverlayHitTestResult.Client)]
    [TestCase(true, false, false, OverlayHitTestResult.Transparent)]
    [TestCase(true, true, false, OverlayHitTestResult.Transparent)]
    [TestCase(true, false, true, OverlayHitTestResult.Client)]
    [TestCase(true, true, true, OverlayHitTestResult.Client)]
    [TestCase(true, true, true, OverlayHitTestResult.Client)]
    public void OnlyEmptyRegionsPassThroughWhenClickThroughIsEnabled(
        bool clickThrough, bool locked, bool overLyrics, OverlayHitTestResult expected)
    {
        Assert.That(OverlayHitTestPolicy.Decide(clickThrough, overLyrics), Is.EqualTo(expected));
        Assert.That(new OverlayInteractionState(locked, clickThrough, true, LyricsContentMode.TwoLines, 900)
            .CanDragOnLyrics, Is.EqualTo(!locked));
    }
}
