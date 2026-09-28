using LyricsDisplayer.Core.Settings;

namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class OverlayInteractionStateTests
{
    [TestCase(false, false, true)]
    [TestCase(true, false, false)]
    [TestCase(false, true, false)]
    [TestCase(true, true, false)]
    public void DragCapabilityMapsLockAndClickThroughIndependently(
        bool locked, bool clickThrough, bool expectedCanDrag)
    {
        var state = new OverlayInteractionState(
            locked, clickThrough, true, OverlayDisplayMode.TwoLines, 900);
        Assert.That(state.CanDrag, Is.EqualTo(expectedCanDrag));
    }

    [Test]
    public void PreferencesMappingPreservesEverySetting()
    {
        var preferences = new OverlayPreferences(true, false, false, OverlayDisplayMode.OneLine, 1230);
        Assert.That(OverlayInteractionState.FromPreferences(preferences).ToPreferences(),
            Is.EqualTo(preferences));
    }
}
