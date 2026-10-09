namespace LyricsDisplayer;

public interface ICurrentLyricsFileService
{
    bool CanOpen { get; }
    string Status { get; }
    event Action? AvailabilityChanged;
    event Action? StatusChanged;
    void RefreshAvailability();
    void SetStatus(string status);
    void OpenExternally();
}
