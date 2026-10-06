using LyricsDisplayer.Core.Settings;
using LyricsDisplayer.Infrastructure.Commands;
using Microsoft.Extensions.Logging;

namespace LyricsDisplayer;

public sealed class OverlayLyricsSurfaceActionsViewModel : LyricsSurfaceActionsViewModel,
    IOverlayLyricsSurfaceActionsViewModel
{
    private readonly ILyricsOverlayInteractionService _overlay;
    private OverlayInteractionState _interaction;

    public IRelayCommand OpenLyricsWindowCommand { get; }
    public IRelayCommand SetOneLineCommand { get; }
    public IRelayCommand SetTwoLinesCommand { get; }
    public IRelayCommand SetAllLyricsCommand { get; }
    public IRelayCommand ToggleLockedCommand { get; }
    public IRelayCommand ToggleClickThroughCommand { get; }
    public IRelayCommand HideCommand { get; }
    public OverlayInteractionState Interaction => _interaction;

    public OverlayLyricsSurfaceActionsViewModel(ICommandFactory commandFactory,
        ICurrentLyricsTimingService timing, ICurrentLyricsFileService lyricsFile,
        IBuiltInLyricsEditorService editor, ILyricsOverlayInteractionService overlay,
        ILogger<OverlayLyricsSurfaceActionsViewModel> logger)
        : base(commandFactory, timing, lyricsFile, editor, logger)
    {
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        _interaction = overlay.Interaction;
        OpenLyricsWindowCommand = commandFactory.Create("application.overlay.open-lyrics-window",
            overlay.RequestOpenLyricsWindow, scope: CommandScope.Application);
        SetOneLineCommand = commandFactory.Create("application.overlay.display.one-line",
            () => overlay.SetContentMode(LyricsContentMode.OneLine), scope: CommandScope.Application);
        SetTwoLinesCommand = commandFactory.Create("application.overlay.display.two-lines",
            () => overlay.SetContentMode(LyricsContentMode.TwoLines), scope: CommandScope.Application);
        SetAllLyricsCommand = commandFactory.Create("application.overlay.display.all-lyrics",
            () => overlay.SetContentMode(LyricsContentMode.AllLyrics), scope: CommandScope.Application);
        ToggleLockedCommand = commandFactory.Create("application.overlay.toggle-lock",
            () => overlay.SetLocked(!Interaction.Locked), scope: CommandScope.Application);
        ToggleClickThroughCommand = commandFactory.Create("application.overlay.toggle-click-through",
            () => overlay.SetClickThrough(!Interaction.ClickThrough), scope: CommandScope.Application);
        HideCommand = commandFactory.Create("application.overlay.hide", overlay.Hide,
            scope: CommandScope.Application);
        _overlay.InteractionStateChanged += OnInteractionStateChanged;
    }

    private void OnInteractionStateChanged(OverlayInteractionState state)
    {
        if (_interaction == state) return;
        _interaction = state;
        OnPropertyChanged(nameof(Interaction));
    }
}
