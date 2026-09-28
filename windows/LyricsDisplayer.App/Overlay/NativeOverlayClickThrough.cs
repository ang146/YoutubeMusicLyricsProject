using System.Runtime.InteropServices;

namespace LyricsDisplayer;

internal interface IOverlayClickThroughAdapter
{
    void SetClickThrough(nint windowHandle, bool enabled);
}

internal sealed class NativeOverlayClickThrough : IOverlayClickThroughAdapter
{
    private const int ExtendedStyleIndex = -20;
    internal const long TransparentStyle = 0x00000020L;
    internal const long NoActivateStyle = 0x08000000L;

    public void SetClickThrough(nint windowHandle, bool enabled)
    {
        if (windowHandle == nint.Zero) return;
        var current = GetWindowLongPtr(windowHandle, ExtendedStyleIndex).ToInt64();
        var desired = ApplyToStyle(current, enabled);
        if (desired != current) SetWindowLongPtr(windowHandle, ExtendedStyleIndex, new nint(desired));
    }

    internal static long ApplyToStyle(long style, bool enabled) => enabled
        ? style | TransparentStyle | NoActivateStyle
        : style & ~TransparentStyle;

    internal static long GetCurrentStyle(nint windowHandle) =>
        GetWindowLongPtr(windowHandle, ExtendedStyleIndex).ToInt64();

    private static nint GetWindowLongPtr(nint windowHandle, int index) =>
        nint.Size == 8 ? GetWindowLongPtr64(windowHandle, index) : new nint(GetWindowLong32(windowHandle, index));

    private static nint SetWindowLongPtr(nint windowHandle, int index, nint value) =>
        nint.Size == 8
            ? SetWindowLongPtr64(windowHandle, index, value)
            : new nint(SetWindowLong32(windowHandle, index, value.ToInt32()));

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern nint GetWindowLongPtr64(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(nint windowHandle, int index, int value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern nint SetWindowLongPtr64(nint windowHandle, int index, nint value);
}
