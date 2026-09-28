using LyricsDisplayer.Core.Settings;

namespace LyricsDisplayer;

public readonly record struct OverlayRectangle(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
}

public readonly record struct OverlayWorkArea(double Left, double Top, double Width, double Height, bool IsPrimary = false)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
}

public sealed record OverlayPlacement(OverlayPosition Position, double Width, double Height, bool UsedFallback);

public static class OverlayPositionResolver
{
    private const double MinimumVisibleWidth = 48;
    private const double MinimumVisibleHeight = 32;

    public static OverlayPlacement Resolve(
        OverlayPosition? savedPosition,
        double overlayWidth,
        double overlayHeight,
        IReadOnlyList<OverlayWorkArea> visibleWorkAreas)
    {
        ArgumentNullException.ThrowIfNull(visibleWorkAreas);
        if (!double.IsFinite(overlayWidth) || overlayWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(overlayWidth));
        if (!double.IsFinite(overlayHeight) || overlayHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(overlayHeight));

        var validAreas = visibleWorkAreas.Where(area => area.Width > 0 && area.Height > 0).ToArray();
        if (validAreas.Length == 0) return new(new(0, 0), overlayWidth, overlayHeight, true);

        if (savedPosition is { IsFinite: true } saved)
        {
            var savedRectangle = new OverlayRectangle(saved.Left, saved.Top, overlayWidth, overlayHeight);
            var containingArea = validAreas.FirstOrDefault(area => FullyContains(savedRectangle, area));
            if (containingArea.Width > 0)
                return new(saved, overlayWidth, overlayHeight, false);

            var nearest = validAreas.OrderBy(area => DistanceSquared(savedRectangle, area)).First();
            return Constrain(saved, overlayWidth, overlayHeight, nearest, true);
        }

        var primary = validAreas.FirstOrDefault(area => area.IsPrimary);
        if (primary.Width <= 0) primary = validAreas[0];
        var width = Math.Min(overlayWidth, primary.Width);
        var height = Math.Min(overlayHeight, primary.Height);
        var left = primary.Left + Math.Max(0, (primary.Width - width) / 2);
        var desiredTop = primary.Top + primary.Height * 0.72 - height / 2;
        var top = Math.Clamp(desiredTop, primary.Top, primary.Bottom - height);
        return new(new(left, top), width, height, true);
    }

    private static OverlayPlacement Constrain(
        OverlayPosition desired,
        double width,
        double height,
        OverlayWorkArea area,
        bool usedFallback)
    {
        width = Math.Min(width, area.Width);
        height = Math.Min(height, area.Height);
        var left = Math.Clamp(desired.Left, area.Left, area.Right - width);
        var top = Math.Clamp(desired.Top, area.Top, area.Bottom - height);
        return new(new(left, top), width, height, usedFallback || left != desired.Left || top != desired.Top);
    }

    public static bool FullyContains(OverlayRectangle rectangle, OverlayWorkArea area) =>
        rectangle.Left >= area.Left && rectangle.Top >= area.Top &&
        rectangle.Right <= area.Right && rectangle.Bottom <= area.Bottom;

    private static double DistanceSquared(OverlayRectangle rectangle, OverlayWorkArea area)
    {
        var dx = Math.Max(Math.Max(area.Left - rectangle.Right, 0), rectangle.Left - area.Right);
        var dy = Math.Max(Math.Max(area.Top - rectangle.Bottom, 0), rectangle.Top - area.Bottom);
        return dx * dx + dy * dy;
    }

    public static bool MeaningfullyIntersects(OverlayRectangle rectangle, OverlayWorkArea workArea)
    {
        if (rectangle.Width <= 0 || rectangle.Height <= 0 || workArea.Width <= 0 || workArea.Height <= 0)
            return false;
        var intersectionWidth = Math.Min(rectangle.Right, workArea.Right) - Math.Max(rectangle.Left, workArea.Left);
        var intersectionHeight = Math.Min(rectangle.Bottom, workArea.Bottom) - Math.Max(rectangle.Top, workArea.Top);
        return intersectionWidth >= Math.Min(MinimumVisibleWidth, rectangle.Width) &&
               intersectionHeight >= Math.Min(MinimumVisibleHeight, rectangle.Height);
    }
}
