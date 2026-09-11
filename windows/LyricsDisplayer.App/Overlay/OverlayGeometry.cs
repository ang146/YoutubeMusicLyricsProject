using LyricsDisplayer.Core.Settings;

namespace LyricsDisplayer;

public readonly record struct OverlayRectangle(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
}

public readonly record struct OverlayWorkArea(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
}

public sealed record OverlayPlacement(OverlayPosition Position, bool UsedFallback);

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

        if (savedPosition is { IsFinite: true } saved)
        {
            var rectangle = new OverlayRectangle(saved.Left, saved.Top, overlayWidth, overlayHeight);
            if (visibleWorkAreas.Any(area => MeaningfullyIntersects(rectangle, area)))
                return new(saved, false);
        }

        var primary = visibleWorkAreas.FirstOrDefault(area => area.Width > 0 && area.Height > 0);
        if (primary.Width <= 0 || primary.Height <= 0)
            return new(new(0, 0), true);

        var left = primary.Left + Math.Max(0, (primary.Width - overlayWidth) / 2);
        var desiredTop = primary.Top + primary.Height * 0.72 - overlayHeight / 2;
        var maximumTop = primary.Top + Math.Max(0, primary.Height - overlayHeight);
        var top = Math.Clamp(desiredTop, primary.Top, maximumTop);
        return new(new(left, top), true);
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
