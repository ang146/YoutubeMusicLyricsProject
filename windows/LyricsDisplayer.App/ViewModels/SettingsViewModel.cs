namespace LyricsDisplayer;

using Microsoft.Extensions.Logging;
using LyricsDisplayer.Resources;

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
            new SettingsPageViewModel(SettingsPageId.General, Strings.SettingsGeneral),
            new SettingsPageViewModel(SettingsPageId.LyricsOverlay, Strings.SettingsLyricsOverlay),
            new SettingsPageViewModel(SettingsPageId.LyricsWindow, Strings.SettingsLyricsWindow),
            new SettingsPageViewModel(SettingsPageId.MediaController, Strings.SettingsMediaController),
            new SettingsPageViewModel(SettingsPageId.Editor, Strings.SettingsEditor),
            DebugPage
        ];
        SelectedPage = Pages[0];
        Logger.LogDebug("Settings ViewModel initialized with {PageCount} pages.", Pages.Count);
    }
}
