using LyricsDisplayer.Infrastructure.Commands;
using Microsoft.Extensions.Logging;

namespace LyricsDisplayer;

public sealed class MainLyricsSurfaceActionsViewModel : LyricsSurfaceActionsViewModel,
    IMainLyricsSurfaceActionsViewModel
{
    public IRelayCommand OpenSettingsCommand { get; }

    public MainLyricsSurfaceActionsViewModel(ICommandFactory commandFactory,
        ICurrentLyricsTimingService timing, ICurrentLyricsFileService lyricsFile,
        IBuiltInLyricsEditorService editor, SettingsWindowService settings,
        ILogger<MainLyricsSurfaceActionsViewModel> logger)
        : base(commandFactory, timing, lyricsFile, editor, logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        OpenSettingsCommand = commandFactory.Create(ApplicationCommandIds.MainWindow.OpenSettings, settings.Open,
            scope: CommandScope.Application);
    }
}
