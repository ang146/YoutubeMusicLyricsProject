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
            locked, clickThrough, true, LyricsContentMode.TwoLines, 900);
        Assert.That(state.CanDragOnLyrics, Is.EqualTo(!locked));
        Assert.That(state.ClickThrough, Is.EqualTo(clickThrough));
    }

    [Test]
    public void PreferencesMappingPreservesEverySetting()
    {
        var preferences = new OverlayPreferences(true, false, false, LyricsContentMode.OneLine, 1230);
        Assert.That(OverlayInteractionState.FromPreferences(preferences).ToPreferences(),
            Is.EqualTo(preferences));
    }
}
