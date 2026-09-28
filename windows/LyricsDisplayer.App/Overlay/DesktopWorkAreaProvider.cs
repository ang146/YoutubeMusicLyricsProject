using System.Runtime.InteropServices;

namespace LyricsDisplayer;

public static class DesktopWorkAreaProvider
{
    private const uint MonitorDefaultToNearest = 2;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    public static IReadOnlyList<OverlayMonitorDescriptor> GetMonitors()
    {
        var monitors = new List<OverlayMonitorDescriptor>();
        NativeMethods.EnumDisplayMonitors(0, 0, (monitor, _, _, _) =>
        {
            if (TryReadMonitor(monitor, out var descriptor)) monitors.Add(descriptor);
            return true;
        }, 0);

        return monitors;
    }

    public static IReadOnlyList<OverlayWorkArea> GetVisibleWorkAreas() =>
        GetMonitors().Select(monitor => monitor.WorkAreaDips).ToArray();

    public static OverlayMonitorDescriptor? GetMonitorAt(OverlayPixelPoint point)
    {
        var monitor = NativeMethods.MonitorFromPoint(new((int)Math.Round(point.X), (int)Math.Round(point.Y)),
            MonitorDefaultToNearest);
        return monitor != 0 && TryReadMonitor(monitor, out var descriptor) ? descriptor : null;
    }

    public static OverlayMonitorDescriptor? GetMonitorForWindow(nint hwnd)
    {
        var monitor = NativeMethods.MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        return monitor != 0 && TryReadMonitor(monitor, out var descriptor) ? descriptor : null;
    }

    public static bool TryGetCursorPosition(out OverlayPixelPoint position)
    {
        if (!NativeMethods.GetCursorPos(out var point))
        {
            position = default;
            return false;
        }
        position = new(point.X, point.Y);
        return true;
    }

    public static bool TryGetWindowRectangle(nint hwnd, out OverlayPixelRectangle rectangle)
    {
        if (!NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            rectangle = default;
            return false;
        }
        rectangle = new(rect.Left, rect.Top, rect.Right, rect.Bottom);
        return true;
    }

    public static void SetWindowScreenPosition(nint hwnd, OverlayPixelPoint position) =>
        NativeMethods.SetWindowPos(hwnd, 0, (int)Math.Round(position.X), (int)Math.Round(position.Y), 0, 0,
            SwpNoSize | SwpNoZOrder | SwpNoActivate);

    private static bool TryReadMonitor(nint handle, out OverlayMonitorDescriptor descriptor)
    {
        var info = new NativeMonitorInfo { Size = (uint)Marshal.SizeOf<NativeMonitorInfo>() };
        if (!NativeMethods.GetMonitorInfo(handle, ref info))
        {
            descriptor = null!;
            return false;
        }

        var scale = 100u;
        if (NativeMethods.GetScaleFactorForMonitor(handle, out var reportedScale) >= 0 && reportedScale > 0)
            scale = reportedScale;
        var factor = scale / 100d;
        descriptor = new(
            handle.ToInt64().ToString("X"),
            new(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom),
            factor,
            factor,
            (info.Flags & 1) != 0,
            handle);
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMonitorInfo
    {
        public uint Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool MonitorEnumProc(nint monitor, nint hdc, nint rect, nint data);

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumDisplayMonitors(nint hdc, nint clipRect, MonitorEnumProc callback, nint data);

        [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetMonitorInfo(nint monitor, ref NativeMonitorInfo info);

        [DllImport("user32.dll")]
        internal static extern nint MonitorFromPoint(NativePoint point, uint flags);

        [DllImport("user32.dll")]
        internal static extern nint MonitorFromWindow(nint hwnd, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetCursorPos(out NativePoint point);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(nint hwnd, out NativeRect rectangle);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);

        [DllImport("shcore.dll")]
        internal static extern int GetScaleFactorForMonitor(nint monitor, out uint scale);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;

        public NativePoint(int x, int y) { X = x; Y = y; }
    }
}
