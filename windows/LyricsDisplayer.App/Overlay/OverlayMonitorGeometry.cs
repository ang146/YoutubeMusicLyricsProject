namespace LyricsDisplayer;

public readonly record struct OverlayPixelPoint(double X, double Y);

public readonly record struct OverlayPixelRectangle(double Left, double Top, double Right, double Bottom)
{
    public double Width => Right - Left;
    public double Height => Bottom - Top;

    public bool Contains(OverlayPixelPoint point) =>
        point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;
}

public sealed record OverlayMonitorDescriptor(
    string Id,
    OverlayPixelRectangle WorkArea,
    double ScaleX,
    double ScaleY,
    bool IsPrimary = false,
    nint NativeHandle = default)
{
    public OverlayWorkArea WorkAreaDips => new(
        WorkArea.Left / ScaleX,
        WorkArea.Top / ScaleY,
        WorkArea.Width / ScaleX,
        WorkArea.Height / ScaleY,
        IsPrimary);
}

public enum OverlayResizeEdge
{
    Left,
    Right,
    Top,
    Bottom,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

public sealed record OverlayMoveResult(
    OverlayPixelPoint Position,
    double WidthDips,
    double HeightDips,
    OverlayMonitorDescriptor Monitor);

public static class OverlayMonitorGeometry
{
    public static OverlayMonitorDescriptor? SelectMonitor(
        OverlayPixelPoint cursor,
        IReadOnlyList<OverlayMonitorDescriptor> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        if (monitors.Count == 0) return null;
        return monitors.FirstOrDefault(monitor => monitor.WorkArea.Contains(cursor)) ??
               monitors.OrderBy(monitor => SquaredDistance(cursor, monitor.WorkArea))
                   .ThenByDescending(monitor => monitor.IsPrimary)
                   .First();
    }

    public static OverlayMoveResult ConstrainMove(
        OverlayPixelPoint cursor,
        OverlayPixelPoint grabOffsetDips,
        double widthDips,
        double heightDips,
        OverlayMonitorDescriptor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ValidateScale(monitor.ScaleX, nameof(monitor.ScaleX));
        ValidateScale(monitor.ScaleY, nameof(monitor.ScaleY));
        if (!double.IsFinite(widthDips) || widthDips <= 0) throw new ArgumentOutOfRangeException(nameof(widthDips));
        if (!double.IsFinite(heightDips) || heightDips <= 0) throw new ArgumentOutOfRangeException(nameof(heightDips));

        var width = Math.Min(widthDips, monitor.WorkArea.Width / monitor.ScaleX);
        var height = Math.Min(heightDips, monitor.WorkArea.Height / monitor.ScaleY);
        var physicalWidth = width * monitor.ScaleX;
        var physicalHeight = height * monitor.ScaleY;
        var grabX = Math.Clamp(grabOffsetDips.X, 0, width) * monitor.ScaleX;
        var grabY = Math.Clamp(grabOffsetDips.Y, 0, height) * monitor.ScaleY;
        var left = Math.Clamp(cursor.X - grabX, monitor.WorkArea.Left, monitor.WorkArea.Right - physicalWidth);
        var top = Math.Clamp(cursor.Y - grabY, monitor.WorkArea.Top, monitor.WorkArea.Bottom - physicalHeight);

        return new(new(left, top), width, height, monitor);
    }

    public static OverlayPixelRectangle ConstrainResize(
        OverlayPixelRectangle requested,
        OverlayPixelRectangle workArea,
        OverlayResizeEdge edge,
        double minimumWidth,
        double minimumHeight)
    {
        if (workArea.Width <= 0 || workArea.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(workArea));
        if (!double.IsFinite(minimumWidth) || minimumWidth < 0)
            throw new ArgumentOutOfRangeException(nameof(minimumWidth));
        if (!double.IsFinite(minimumHeight) || minimumHeight < 0)
            throw new ArgumentOutOfRangeException(nameof(minimumHeight));

        minimumWidth = Math.Min(minimumWidth, workArea.Width);
        minimumHeight = Math.Min(minimumHeight, workArea.Height);
        var resizeLeft = edge is OverlayResizeEdge.Left or OverlayResizeEdge.TopLeft or OverlayResizeEdge.BottomLeft;
        var resizeRight = edge is OverlayResizeEdge.Right or OverlayResizeEdge.TopRight or OverlayResizeEdge.BottomRight;
        var resizeTop = edge is OverlayResizeEdge.Top or OverlayResizeEdge.TopLeft or OverlayResizeEdge.TopRight;
        var resizeBottom = edge is OverlayResizeEdge.Bottom or OverlayResizeEdge.BottomLeft or OverlayResizeEdge.BottomRight;

        double left;
        double right;
        if (resizeLeft)
        {
            right = Math.Clamp(requested.Right, workArea.Left + minimumWidth, workArea.Right);
            left = Math.Clamp(requested.Left, workArea.Left, right - minimumWidth);
        }
        else if (resizeRight)
        {
            left = Math.Clamp(requested.Left, workArea.Left, workArea.Right - minimumWidth);
            right = Math.Clamp(requested.Right, left + minimumWidth, workArea.Right);
        }
        else
        {
            var width = Math.Min(requested.Width, workArea.Width);
            left = Math.Clamp(requested.Left, workArea.Left, workArea.Right - width);
            right = left + width;
        }

        double top;
        double bottom;
        if (resizeTop)
        {
            bottom = Math.Clamp(requested.Bottom, workArea.Top + minimumHeight, workArea.Bottom);
            top = Math.Clamp(requested.Top, workArea.Top, bottom - minimumHeight);
        }
        else if (resizeBottom)
        {
            top = Math.Clamp(requested.Top, workArea.Top, workArea.Bottom - minimumHeight);
            bottom = Math.Clamp(requested.Bottom, top + minimumHeight, workArea.Bottom);
        }
        else
        {
            var height = Math.Min(requested.Height, workArea.Height);
            top = Math.Clamp(requested.Top, workArea.Top, workArea.Bottom - height);
            bottom = top + height;
        }

        return new(left, top, right, bottom);
    }

    public static double PixelsToDips(double pixels, double scale) =>
        pixels / ValidateScale(scale, nameof(scale));

    public static double DipsToPixels(double dips, double scale) =>
        dips * ValidateScale(scale, nameof(scale));

    private static double SquaredDistance(OverlayPixelPoint point, OverlayPixelRectangle rectangle)
    {
        var dx = Math.Max(Math.Max(rectangle.Left - point.X, 0), point.X - rectangle.Right);
        var dy = Math.Max(Math.Max(rectangle.Top - point.Y, 0), point.Y - rectangle.Bottom);
        return dx * dx + dy * dy;
    }

    private static double ValidateScale(double scale, string parameterName)
    {
        if (!double.IsFinite(scale) || scale <= 0) throw new ArgumentOutOfRangeException(parameterName);
        return scale;
    }
}
