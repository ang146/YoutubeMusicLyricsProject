using System.Windows;

namespace LyricsDisplayer;

public static class DesktopWorkAreaProvider
{
    public static IReadOnlyList<OverlayWorkArea> GetVisibleWorkAreas()
    {
        var primary = SystemParameters.WorkArea;
        var areas = new List<OverlayWorkArea>
        {
            new(primary.Left, primary.Top, primary.Width, primary.Height)
        };

        // WPF exposes the virtual desktop in device-independent units. Keeping it as a
        // second area permits basic secondary-monitor restore without mixing physical pixels into Window.Left/Top.
        var virtualArea = new OverlayWorkArea(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        if (virtualArea != areas[0]) areas.Add(virtualArea);
        return areas;
    }
}
