using LyricsDisplayer.Infrastructure.Commands;

namespace LyricsDisplayer;

public interface IOverlayLyricsSurfaceActionsViewModel : ILyricsSurfaceActionsViewModel
{
    IRelayCommand OpenLyricsWindowCommand { get; }
    IRelayCommand SetOneLineCommand { get; }
    IRelayCommand SetTwoLinesCommand { get; }
    IRelayCommand SetAllLyricsCommand { get; }
    IRelayCommand ToggleLockedCommand { get; }
    IRelayCommand ToggleClickThroughCommand { get; }
    IRelayCommand HideCommand { get; }
    OverlayInteractionState Interaction { get; }
}
