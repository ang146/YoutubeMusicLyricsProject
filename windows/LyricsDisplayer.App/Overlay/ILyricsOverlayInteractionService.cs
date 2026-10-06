using LyricsDisplayer.Core.Settings;

namespace LyricsDisplayer;

public interface ILyricsOverlayInteractionService
{
    OverlayInteractionState Interaction { get; }
    event Action<OverlayInteractionState>? InteractionStateChanged;
    void RequestOpenLyricsWindow();
    void SetContentMode(LyricsContentMode mode);
    void SetLocked(bool value);
    void SetClickThrough(bool value);
    void Hide();
}
