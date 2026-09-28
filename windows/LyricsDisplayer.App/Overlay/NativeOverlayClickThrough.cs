namespace LyricsDisplayer;

// Click-through is decided per WM_NCHITTEST location in LyricsOverlayWindow.
// No whole-window WS_EX_TRANSPARENT flag is applied.
internal static class NativeOverlayClickThrough
{
    internal const int WmNcHitTest = 0x0084;
    internal const int HtClient = 1;
    internal const int HtTransparent = -1;
    internal const int HtLeft = 10;
    internal const int HtRight = 11;
    internal const int HtTop = 12;
    internal const int HtTopLeft = 13;
    internal const int HtTopRight = 14;
    internal const int HtBottom = 15;
    internal const int HtBottomLeft = 16;
    internal const int HtBottomRight = 17;
}
