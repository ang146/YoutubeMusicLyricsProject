namespace LyricsDisplayer.App.Tests;

using LyricsDisplayer.Core.Settings;

[TestFixture]
public sealed class NativeOverlayClickThroughTests
{
    [TestCase(false, false, OverlayHitTestResult.Client)]
    [TestCase(false, true, OverlayHitTestResult.ResizeLeft)]
    [TestCase(true, false, OverlayHitTestResult.Transparent)]
    [TestCase(true, true, OverlayHitTestResult.Transparent)]
    public void EmptyPointUsesClientResizeOrPassThroughByMode(
        bool clickThrough, bool overResizeEdge, OverlayHitTestResult expected)
    {
        var region = overResizeEdge ? OverlayHitTestResult.ResizeLeft : OverlayHitTestResult.Client;
        Assert.That(OverlayHitTestPolicy.Decide(clickThrough, false, region), Is.EqualTo(expected));
    }

    [TestCase(OverlayHitTestResult.ResizeLeft)]
    [TestCase(OverlayHitTestResult.ResizeRight)]
    [TestCase(OverlayHitTestResult.ResizeTop)]
    [TestCase(OverlayHitTestResult.ResizeBottom)]
    [TestCase(OverlayHitTestResult.ResizeTopLeft)]
    [TestCase(OverlayHitTestResult.ResizeTopRight)]
    [TestCase(OverlayHitTestResult.ResizeBottomLeft)]
    [TestCase(OverlayHitTestResult.ResizeBottomRight)]
    public void BorderlessResizePolicyPreservesNativeEdgeResult(OverlayHitTestResult edge)
    {
        Assert.That(OverlayHitTestPolicy.Decide(false, false, edge), Is.EqualTo(edge));
        Assert.That(OverlayHitTestPolicy.Decide(true, false, edge), Is.EqualTo(OverlayHitTestResult.Transparent));
    }

    [TestCase(false, OverlayHitTestResult.Client)]
    [TestCase(true, OverlayHitTestResult.Client)]
    public void LyricRegionRetainsInteractionEvenAtAnEdge(bool clickThrough, OverlayHitTestResult expected)
    {
        Assert.That(OverlayHitTestPolicy.Decide(clickThrough, true, OverlayHitTestResult.ResizeTopLeft),
            Is.EqualTo(expected));
    }

    [TestCase(true, false)]
    [TestCase(true, true)]
    public void LockAndResizeCapabilitiesRemainSeparate(bool canResize, bool locked)
    {
        var state = new OverlayInteractionState(locked, !canResize,
            LyricsContentMode.TwoLines, 900, OverlayPreferences.DefaultHeight);
        Assert.That(state.CanResize, Is.EqualTo(canResize));
        Assert.That(state.CanDragOnLyrics, Is.EqualTo(!locked));
    }

    [TestCase(0xF020)] // SC_MINIMIZE
    [TestCase(0xF030)] // SC_MAXIMIZE
    [TestCase(0xF023)] // command parameters can carry low-order flags
    public void OverlayBlocksNativeMinimizeAndMaximizeSystemCommands(int command)
    {
        Assert.That(OverlayWindowStatePolicy.ShouldBlockSystemCommand(new nint(command)), Is.True);
    }

    [TestCase(0xF010)] // SC_MOVE
    [TestCase(0xF120)] // SC_RESTORE
    [TestCase(0xF000)] // SC_SIZE
    public void OverlayLeavesMoveRestoreAndResizeSystemCommandsUnchanged(int command)
    {
        Assert.That(OverlayWindowStatePolicy.ShouldBlockSystemCommand(new nint(command)), Is.False);
    }

}
