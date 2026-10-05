namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class OverlayTimingCommandRouterTests
{
    [TestCase(OverlayCommand.AdjustCurrentLineMinus500, "current", -500)]
    [TestCase(OverlayCommand.AdjustCurrentLineMinus100, "current", -100)]
    [TestCase(OverlayCommand.AdjustCurrentLinePlus100, "current", 100)]
    [TestCase(OverlayCommand.AdjustCurrentLinePlus500, "current", 500)]
    [TestCase(OverlayCommand.AdjustGlobalMinus500, "global", -500)]
    [TestCase(OverlayCommand.AdjustGlobalMinus100, "global", -100)]
    [TestCase(OverlayCommand.AdjustGlobalPlus100, "global", 100)]
    [TestCase(OverlayCommand.AdjustGlobalPlus500, "global", 500)]
    public void AdjustmentCommandsRouteToExistingScopeWithExpectedMilliseconds(
        OverlayCommand command, string expectedScope, long expectedDelta)
    {
        var calls = new List<string>();
        var handled = OverlayTimingCommandRouter.Route(command,
            delta => calls.Add($"current:{delta}"),
            delta => calls.Add($"global:{delta}"),
            () => calls.Add("reset"));

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.True);
            Assert.That(calls, Is.EqualTo(new[] { $"{expectedScope}:{expectedDelta}" }));
        });
    }

    [Test]
    public void GlobalResetRoutesOnlyToExistingGlobalResetAction()
    {
        var calls = new List<string>();
        var handled = OverlayTimingCommandRouter.Route(OverlayCommand.ResetGlobalTiming,
            _ => calls.Add("current"), _ => calls.Add("global"), () => calls.Add("reset"));

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.True);
            Assert.That(calls, Is.EqualTo(new[] { "reset" }));
        });
    }

    [Test]
    public void NonTimingCommandIsNotHandled()
    {
        var calls = new List<string>();
        var handled = OverlayTimingCommandRouter.Route(OverlayCommand.Hide,
            _ => calls.Add("current"), _ => calls.Add("global"), () => calls.Add("reset"));

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.False);
            Assert.That(calls, Is.Empty);
        });
    }
}
