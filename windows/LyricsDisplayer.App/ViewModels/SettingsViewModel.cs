namespace LyricsDisplayer;

public enum SettingsPageId
{
    General,
    LyricsOverlay,
    LyricsWindow,
    MediaController,
    Editor,
    Debug
}

public sealed record SettingsPageViewModel(SettingsPageId Id, string Title);

public sealed class SettingsViewModel : ViewModelBase
{
    private SettingsPageViewModel _selectedPage = null!;

    public IReadOnlyList<SettingsPageViewModel> Pages { get; } =
    [
        new(SettingsPageId.General, "General"),
        new(SettingsPageId.LyricsOverlay, "Lyrics Overlay"),
        new(SettingsPageId.LyricsWindow, "Lyrics Window"),
        new(SettingsPageId.MediaController, "Media Controller"),
        new(SettingsPageId.Editor, "Editor"),
        new(SettingsPageId.Debug, "Debug")
    ];

    public SettingsPageViewModel SelectedPage
    {
        get => _selectedPage;
        set => SetProperty(ref _selectedPage, value ?? throw new ArgumentNullException(nameof(value)));
    }

    public SettingsViewModel() => SelectedPage = Pages[0];
}
