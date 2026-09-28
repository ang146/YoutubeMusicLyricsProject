using LyricsDisplayer.Core.Settings;

namespace LyricsDisplayer;

public sealed record OverlayInteractionState(
    bool Locked,
    bool ClickThrough,
    bool Topmost,
    LyricsContentMode ContentMode,
    double Width,
    double Height)
{
    public static OverlayInteractionState FromPreferences(OverlayPreferences preferences) =>
        new(preferences.Locked, preferences.ClickThrough, preferences.Topmost,
            preferences.ContentMode, preferences.Width, preferences.Height);

    public OverlayPreferences ToPreferences() =>
        new(Locked, ClickThrough, Topmost, ContentMode, Width, Height);

    public bool CanDragOnLyrics => !Locked;
    public bool CanResize => !ClickThrough;
}

public enum OverlayCommand
{
    OpenControlPanel,
    ToggleLocked,
    ToggleClickThrough,
    ToggleTopmost,
    SetOneLine,
    SetTwoLines,
    SetAllLyrics,
    AdjustCurrentLineMinus500,
    AdjustCurrentLineMinus100,
    AdjustCurrentLinePlus100,
    AdjustCurrentLinePlus500,
    AdjustGlobalMinus500,
    AdjustGlobalMinus100,
    ResetGlobalTiming,
    AdjustGlobalPlus100,
    AdjustGlobalPlus500,
    Hide
}
