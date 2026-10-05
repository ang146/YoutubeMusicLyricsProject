namespace LyricsDisplayer;

using Microsoft.Extensions.Logging;

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

public sealed class SettingsViewModel : ViewModelBase, ISettingsViewModel
{
    private ISettingsPageViewModel _selectedPage = null!;

    public IDebugSettingsPageViewModel DebugPage { get; }
    public IReadOnlyList<ISettingsPageViewModel> Pages { get; }

    public ISettingsPageViewModel SelectedPage
    {
        get => _selectedPage;
        set => SetProperty(ref _selectedPage, value ?? throw new ArgumentNullException(nameof(value)));
    }

    public SettingsViewModel(ILogger<SettingsViewModel> logger, IDebugSettingsPageViewModel debugPage)
        : base(logger)
    {
        DebugPage = debugPage ?? throw new ArgumentNullException(nameof(debugPage));
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
        Logger.LogDebug("Settings ViewModel initialized with {PageCount} pages.", Pages.Count);
    }
}
