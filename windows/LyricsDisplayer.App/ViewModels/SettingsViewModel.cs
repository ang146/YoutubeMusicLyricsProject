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

public interface ISettingsPageViewModel
{
    SettingsPageId Id { get; }
    string Title { get; }
}

public sealed record SettingsPageViewModel(SettingsPageId Id, string Title) : ISettingsPageViewModel;

public sealed class SettingsViewModel : ViewModelBase
{
    private ISettingsPageViewModel _selectedPage = null!;

    public DebugSettingsPageViewModel DebugPage { get; }
    public IReadOnlyList<ISettingsPageViewModel> Pages { get; }

    public ISettingsPageViewModel SelectedPage
    {
        get => _selectedPage;
        set => SetProperty(ref _selectedPage, value ?? throw new ArgumentNullException(nameof(value)));
    }

    public SettingsViewModel(DebugSettingsPageViewModel? debugPage = null)
    {
        DebugPage = debugPage ?? new DebugSettingsPageViewModel();
        Pages =
        [
            new SettingsPageViewModel(SettingsPageId.General, "General"),
            new SettingsPageViewModel(SettingsPageId.LyricsOverlay, "Lyrics Overlay"),
            new SettingsPageViewModel(SettingsPageId.LyricsWindow, "Lyrics Window"),
            new SettingsPageViewModel(SettingsPageId.MediaController, "Media Controller"),
            new SettingsPageViewModel(SettingsPageId.Editor, "Editor"),
            DebugPage
        ];
        SelectedPage = Pages[0];
    }
}
