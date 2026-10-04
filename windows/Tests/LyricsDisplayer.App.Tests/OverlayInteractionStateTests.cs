using LyricsDisplayer.Core.Settings;

namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class OverlayInteractionStateTests
{
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void LockControlsMovementWithoutChangingTheSavedClickThroughValue(bool locked, bool clickThrough)
    {
        var state = new OverlayInteractionState(
            locked, clickThrough, LyricsContentMode.TwoLines, 900, 220);
        Assert.That(state.CanDragOnLyrics, Is.EqualTo(!locked));
        Assert.That(state.CanResize, Is.EqualTo(!clickThrough));
        Assert.That(state.ClickThrough, Is.EqualTo(clickThrough));
    }

    [Test]
    public void PreferencesMappingPreservesEverySetting()
    {
        var preferences = new OverlayPreferences(true, false, LyricsContentMode.OneLine, 1230, 377);
        Assert.That(OverlayInteractionState.FromPreferences(preferences).ToPreferences(),
            Is.EqualTo(preferences));
    }
}
