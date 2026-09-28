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
    public void CompletelyOffscreenPositionIsRecoveredToNearestWorkArea()
    {
        var placement = OverlayPositionResolver.Resolve(new(9000, 9000), 900, 190, WorkAreas);
        Assert.Multiple(() =>
        {
            Assert.That(placement.UsedFallback, Is.True);
            Assert.That(placement.Position.Left, Is.EqualTo(3580));
            Assert.That(placement.Position.Top, Is.EqualTo(1010));
            Assert.That(OverlayPositionResolver.FullyContains(
                new(placement.Position.Left, placement.Position.Top, placement.Width, placement.Height),
                WorkAreas[1]), Is.True);
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
        Assert.Multiple(() =>
        {
            Assert.That(placement.Position, Is.EqualTo(new OverlayPosition(100, 50)));
            Assert.That(placement.Width, Is.EqualTo(640));
            Assert.That(placement.Height, Is.EqualTo(160));
        });
    }

    [TestCase(-100, 0)]
    [TestCase(1500, 1020)]
    public void ResolverClampsHorizontalPositionToKeepWholeOverlayVisible(double desiredLeft, double expectedLeft)
    {
        var placement = OverlayPositionResolver.Resolve(new(desiredLeft, 100), 900, 300, [new(0, 0, 1920, 1040)]);

        Assert.Multiple(() =>
        {
            Assert.That(placement.Position.Left, Is.EqualTo(expectedLeft));
            Assert.That(placement.Position.Top, Is.EqualTo(100));
            Assert.That(OverlayPositionResolver.FullyContains(
                new(placement.Position.Left, placement.Position.Top, placement.Width, placement.Height),
                new(0, 0, 1920, 1040)), Is.True);
        });
    }

    [TestCase(-100, 0)]
    [TestCase(900, 740)]
    public void ResolverClampsVerticalPositionToKeepWholeOverlayVisible(double desiredTop, double expectedTop)
    {
        var placement = OverlayPositionResolver.Resolve(new(100, desiredTop), 900, 300, [new(0, 0, 1920, 1040)]);
        Assert.That(placement.Position.Top, Is.EqualTo(expectedTop));
    }

    [Test]
    public void MonitorSelectionFollowsCursorAcrossMonitors()
    {
        var monitorA = Monitor("A", new(0, 0, 1920, 1040), 1, primary: true);
        var monitorB = Monitor("B", new(1920, -200, 4480, 1200), 1.5);

        Assert.Multiple(() =>
        {
            Assert.That(OverlayMonitorGeometry.SelectMonitor(new(500, 500), [monitorA, monitorB])?.Id, Is.EqualTo("A"));
            Assert.That(OverlayMonitorGeometry.SelectMonitor(new(2200, 100), [monitorA, monitorB])?.Id, Is.EqualTo("B"));
        });
    }

    [Test]
    public void MoveIntoDifferentMonitorUsesItsWorkAreaAndPreservesGrabAnchor()
    {
        var monitorA = Monitor("A", new(0, 0, 1920, 1040), 1, primary: true);
        var monitorB = Monitor("B", new(1920, -200, 4480, 1200), 1.5);

        var moved = OverlayMonitorGeometry.ConstrainMove(new(2000, -150), new(100, 80), 900, 300, monitorB);

        Assert.Multiple(() =>
        {
            Assert.That(moved.Monitor.Id, Is.EqualTo("B"));
            Assert.That(moved.Position, Is.EqualTo(new OverlayPixelPoint(1920, -200)));
            Assert.That(moved.WidthDips, Is.EqualTo(900));
            Assert.That(moved.HeightDips, Is.EqualTo(300));
            Assert.That(moved.Position.X, Is.GreaterThanOrEqualTo(monitorB.WorkArea.Left));
            Assert.That(moved.Position.Y, Is.GreaterThanOrEqualTo(monitorB.WorkArea.Top));
            Assert.That(moved.Position.X + moved.WidthDips * monitorB.ScaleX, Is.LessThanOrEqualTo(monitorB.WorkArea.Right));
            Assert.That(moved.Position.Y + moved.HeightDips * monitorB.ScaleY, Is.LessThanOrEqualTo(monitorB.WorkArea.Bottom));
            Assert.That(monitorA.Id, Is.Not.EqualTo(moved.Monitor.Id));
        });
    }

    [Test]
    public void MoveShrinksOnlyWhenTargetWorkAreaRequiresIt()
    {
        var small = Monitor("small", new(0, 0, 640, 160), 1);
        var large = Monitor("large", new(0, 0, 1920, 1040), 1);

        var shrunk = OverlayMonitorGeometry.ConstrainMove(new(300, 80), new(20, 20), 900, 300, small);
        var unchanged = OverlayMonitorGeometry.ConstrainMove(new(300, 80), new(20, 20), 900, 300, large);

        Assert.Multiple(() =>
        {
            Assert.That(shrunk.WidthDips, Is.EqualTo(640));
            Assert.That(shrunk.HeightDips, Is.EqualTo(160));
            Assert.That(unchanged.WidthDips, Is.EqualTo(900));
            Assert.That(unchanged.HeightDips, Is.EqualTo(300));
        });
    }

    [TestCase(-50, 100, 0, 20)]
    [TestCase(2000, 100, 1020, 20)]
    [TestCase(100, -50, 0, 0)]
    [TestCase(100, 1200, 0, 740)]
    public void ManagedMoveClampsEveryEdge(double cursorX, double cursorY, double expectedLeft, double expectedTop)
    {
        var monitor = Monitor("primary", new(0, 0, 1920, 1040), 1, primary: true);
        var moved = OverlayMonitorGeometry.ConstrainMove(
            new(cursorX, cursorY), new(100, 80), 900, 300, monitor);

        Assert.Multiple(() =>
        {
            Assert.That(moved.Position.X, Is.EqualTo(expectedLeft));
            Assert.That(moved.Position.Y, Is.EqualTo(expectedTop));
            Assert.That(moved.Position.X + 900, Is.LessThanOrEqualTo(monitor.WorkArea.Right));
            Assert.That(moved.Position.Y + 300, Is.LessThanOrEqualTo(monitor.WorkArea.Bottom));
        });
    }

    [Test]
    public void VerySmallDestinationMayFallBelowInteractiveMinimum()
    {
        var monitor = Monitor("compact", new(0, 0, 318, 96), 1);
        var moved = OverlayMonitorGeometry.ConstrainMove(new(200, 60), new(20, 20), 900, 300, monitor);

        Assert.Multiple(() =>
        {
            Assert.That(moved.WidthDips, Is.EqualTo(318));
            Assert.That(moved.HeightDips, Is.EqualTo(96));
            Assert.That(moved.Position, Is.EqualTo(new OverlayPixelPoint(0, 0)));
        });
    }

    [TestCase(OverlayResizeEdge.Right, 1000, 2000, 1920)]
    [TestCase(OverlayResizeEdge.Bottom, 500, 1200, 1040)]
    [TestCase(OverlayResizeEdge.Left, -200, 900, 0)]
    [TestCase(OverlayResizeEdge.Top, -100, 700, 0)]
    public void ResizeIsClampedToWorkArea(
        OverlayResizeEdge edge, double requestedLeading, double requestedTrailing, double expectedBoundary)
    {
        var requested = edge switch
        {
            OverlayResizeEdge.Right => new OverlayPixelRectangle(requestedLeading, 100, requestedTrailing, 400),
            OverlayResizeEdge.Bottom => new OverlayPixelRectangle(100, requestedLeading, 600, requestedTrailing),
            OverlayResizeEdge.Left => new OverlayPixelRectangle(requestedLeading, 100, requestedTrailing, 400),
            OverlayResizeEdge.Top => new OverlayPixelRectangle(100, requestedLeading, 600, requestedTrailing),
            _ => throw new ArgumentOutOfRangeException(nameof(edge))
        };
        var result = OverlayMonitorGeometry.ConstrainResize(requested, new(0, 0, 1920, 1040), edge, 420, 120);
        var actualBoundary = edge switch
        {
            OverlayResizeEdge.Right => result.Right,
            OverlayResizeEdge.Bottom => result.Bottom,
            OverlayResizeEdge.Left => result.Left,
            OverlayResizeEdge.Top => result.Top,
            _ => 0
        };
        Assert.That(actualBoundary, Is.EqualTo(expectedBoundary));
    }

    [Test]
    public void BottomResizeStopsAtWorkAreaAboveTaskbar()
    {
        // The monitor extends to y=1080; its usable work area excludes the bottom taskbar.
        var workArea = new OverlayPixelRectangle(0, 0, 1920, 1040);
        var result = OverlayMonitorGeometry.ConstrainResize(
            new(100, 900, 600, 1120), workArea, OverlayResizeEdge.Bottom, 420, 120);

        Assert.That(result.Bottom, Is.EqualTo(1040));
    }

    [TestCase(1)]
    [TestCase(1.25)]
    [TestCase(1.5)]
    [TestCase(2)]
    public void DipPixelConversionsRoundTripAtCommonMonitorScales(double scale)
    {
        const double dips = 913.25;
        Assert.That(OverlayMonitorGeometry.PixelsToDips(
            OverlayMonitorGeometry.DipsToPixels(dips, scale), scale), Is.EqualTo(dips));
    }

    private static OverlayMonitorDescriptor Monitor(
        string id, OverlayPixelRectangle workArea, double scale, bool primary = false) =>
        new(id, workArea, scale, scale, primary);
}
