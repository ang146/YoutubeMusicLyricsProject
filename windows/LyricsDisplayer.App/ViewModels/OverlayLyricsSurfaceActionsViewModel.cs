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
        OpenLyricsWindowCommand = commandFactory.Create(ApplicationCommandIds.Overlay.OpenLyricsWindow,
            overlay.RequestOpenLyricsWindow, scope: CommandScope.Application);
        SetOneLineCommand = commandFactory.Create(ApplicationCommandIds.Overlay.SetOneLine,
            () => overlay.SetContentMode(LyricsContentMode.OneLine), scope: CommandScope.Application);
        SetTwoLinesCommand = commandFactory.Create(ApplicationCommandIds.Overlay.SetTwoLines,
            () => overlay.SetContentMode(LyricsContentMode.TwoLines), scope: CommandScope.Application);
        SetAllLyricsCommand = commandFactory.Create(ApplicationCommandIds.Overlay.SetAllLyrics,
            () => overlay.SetContentMode(LyricsContentMode.AllLyrics), scope: CommandScope.Application);
        ToggleLockedCommand = commandFactory.Create(ApplicationCommandIds.Overlay.ToggleLocked,
            () => overlay.SetLocked(!Interaction.Locked), scope: CommandScope.Application);
        ToggleClickThroughCommand = commandFactory.Create(ApplicationCommandIds.Overlay.ToggleClickThrough,
            () => overlay.SetClickThrough(!Interaction.ClickThrough), scope: CommandScope.Application);
        HideCommand = commandFactory.Create(ApplicationCommandIds.Overlay.Hide, overlay.Hide,
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
