namespace LyricsDisplayer;

public interface IBuiltInLyricsEditorService
{
    bool CanOpen { get; }
    event Action? AvailabilityChanged;
    void RefreshAvailability();
    void Open();
    bool TryCloseForOwnerClosing(bool applicationExitPending);
    void CancelOwnerClose();
}
