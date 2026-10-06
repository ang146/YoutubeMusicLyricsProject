using LyricsDisplayer.Infrastructure.Commands;

namespace LyricsDisplayer;

public interface IMainLyricsSurfaceActionsViewModel : ILyricsSurfaceActionsViewModel
{
    IRelayCommand OpenSettingsCommand { get; }
}
