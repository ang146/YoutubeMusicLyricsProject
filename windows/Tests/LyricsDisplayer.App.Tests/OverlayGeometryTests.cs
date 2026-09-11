using LyricsDisplayer.Core.Settings;

namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class OverlayGeometryTests
{
    private static readonly OverlayWorkArea[] WorkAreas =
    [
        new(0, 0, 1920, 1040),
        new(1920, -200, 2560, 1400)
    ];

    [Test]
    public void VisibleSavedPositionIsRestoredUnchanged()
    {
        var saved = new OverlayPosition(2200, 200);
        var placement = OverlayPositionResolver.Resolve(saved, 900, 190, WorkAreas);
        Assert.Multiple(() =>
        {
            Assert.That(placement.Position, Is.EqualTo(saved));
            Assert.That(placement.UsedFallback, Is.False);
        });
    }

    [Test]
    public void CompletelyOffscreenPositionUsesLowerCenteredPrimaryFallback()
    {
        var placement = OverlayPositionResolver.Resolve(new(9000, 9000), 900, 190, WorkAreas);
        Assert.Multiple(() =>
        {
            Assert.That(placement.UsedFallback, Is.True);
            Assert.That(placement.Position.Left, Is.EqualTo(510));
            Assert.That(placement.Position.Top, Is.GreaterThan(500));
            Assert.That(placement.Position.Top + 190, Is.LessThanOrEqualTo(1040));
        });
    }

    [Test]
    public void TinyInaccessibleSliverUsesFallback()
    {
        var placement = OverlayPositionResolver.Resolve(new(-880, 20), 900, 190, WorkAreas);
        Assert.That(placement.UsedFallback, Is.True);
    }

    [Test]
    public void MissingSavedPositionFallsBackInsideSmallWorkArea()
    {
        var placement = OverlayPositionResolver.Resolve(null, 900, 190, [new(100, 50, 640, 160)]);
        Assert.That(placement.Position, Is.EqualTo(new OverlayPosition(100, 50)));
    }
}
