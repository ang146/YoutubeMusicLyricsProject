namespace LyricsDisplayer;

internal static class OverlayTimingCommandRouter
{
    public static bool Route(
        OverlayCommand command,
        Action<long> adjustCurrentLine,
        Action<long> adjustGlobal,
        Action resetGlobal)
    {
        ArgumentNullException.ThrowIfNull(adjustCurrentLine);
        ArgumentNullException.ThrowIfNull(adjustGlobal);
        ArgumentNullException.ThrowIfNull(resetGlobal);

        switch (command)
        {
            case OverlayCommand.AdjustCurrentLineMinus500: adjustCurrentLine(-500); return true;
            case OverlayCommand.AdjustCurrentLineMinus100: adjustCurrentLine(-100); return true;
            case OverlayCommand.AdjustCurrentLinePlus100: adjustCurrentLine(100); return true;
            case OverlayCommand.AdjustCurrentLinePlus500: adjustCurrentLine(500); return true;
            case OverlayCommand.AdjustGlobalMinus500: adjustGlobal(-500); return true;
            case OverlayCommand.AdjustGlobalMinus100: adjustGlobal(-100); return true;
            case OverlayCommand.ResetGlobalTiming: resetGlobal(); return true;
            case OverlayCommand.AdjustGlobalPlus100: adjustGlobal(100); return true;
            case OverlayCommand.AdjustGlobalPlus500: adjustGlobal(500); return true;
            default: return false;
        }
    }
}
