using LyricsDisplayer.Core.Settings;

namespace LyricsDisplayer;

public sealed record OverlayInteractionState(
    bool Locked,
    bool ClickThrough,
    bool Topmost,
    OverlayDisplayMode DisplayMode,
    double Width)
{
    public static OverlayInteractionState FromPreferences(OverlayPreferences preferences) =>
        new(preferences.Locked, preferences.ClickThrough, preferences.Topmost,
            preferences.DisplayMode, preferences.Width);

    public OverlayPreferences ToPreferences() =>
        new(Locked, ClickThrough, Topmost, DisplayMode, Width);

    public bool CanDrag => !Locked && !ClickThrough;
}

public enum OverlayCommand
{
    OpenControlPanel,
    ToggleLocked,
    ToggleClickThrough,
    ToggleTopmost,
    UseOneLine,
    UseTwoLines,
    Hide
}
