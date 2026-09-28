namespace LyricsDisplayer;

// Click-through is decided per WM_NCHITTEST location in LyricsOverlayWindow.
// No whole-window WS_EX_TRANSPARENT flag is applied.
internal static class NativeOverlayClickThrough
{
    internal const int WmNcHitTest = 0x0084;
    internal const int HtClient = 1;
    internal const int HtTransparent = -1;
}
