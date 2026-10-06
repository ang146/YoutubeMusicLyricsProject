using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Infrastructure.Commands;
using Microsoft.Extensions.Logging;

namespace LyricsDisplayer;

/// <summary>Constructs shared lyrics-surface commands against application services, not window callbacks.</summary>
public abstract class LyricsSurfaceActionsViewModel : ViewModelBase, ILyricsSurfaceActionsViewModel
{
    private readonly ICurrentLyricsTimingService _timing;
    private readonly ICurrentLyricsFileService _lyricsFile;
    private readonly IBuiltInLyricsEditorService _editor;
    private bool _canOpenExternalLyrics;
    private string _externalLyricsStatus;
    private bool _canAdjustCurrentLine;
    private bool _canShiftAll;

    public LyricsSurfaceActionDefinition OpenLrcExternally { get; }
    public LyricsSurfaceActionDefinition OpenBuiltInEditor { get; }
    public LyricsSurfaceActionDefinition AdjustCurrentLineMinus500 { get; }
    public LyricsSurfaceActionDefinition AdjustCurrentLineMinus100 { get; }
    public LyricsSurfaceActionDefinition AdjustCurrentLinePlus100 { get; }
    public LyricsSurfaceActionDefinition AdjustCurrentLinePlus500 { get; }
    public LyricsSurfaceActionDefinition ShiftAllMinus500 { get; }
    public LyricsSurfaceActionDefinition ShiftAllMinus100 { get; }
    public LyricsSurfaceActionDefinition ShiftAllPlus100 { get; }
    public LyricsSurfaceActionDefinition ShiftAllPlus500 { get; }
    public bool CanOpenExternalLyrics => _canOpenExternalLyrics;
    public string ExternalLyricsStatus => _externalLyricsStatus;
    public bool CanAdjustCurrentLine => _canAdjustCurrentLine;
    public bool CanShiftAll => _canShiftAll;

    protected LyricsSurfaceActionsViewModel(ICommandFactory commandFactory,
        ICurrentLyricsTimingService timing, ICurrentLyricsFileService lyricsFile,
        IBuiltInLyricsEditorService editor, ILogger logger) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(commandFactory);
        _timing = timing ?? throw new ArgumentNullException(nameof(timing));
        _lyricsFile = lyricsFile ?? throw new ArgumentNullException(nameof(lyricsFile));
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _canOpenExternalLyrics = lyricsFile.CanOpen;
        _externalLyricsStatus = lyricsFile.Status;
        _canAdjustCurrentLine = timing.CanAdjustCurrentLine;
        _canShiftAll = timing.CanShiftAll;

        OpenLrcExternally = new("Open LRC Externally", commandFactory.Create(
            "application.open-lrc-externally", lyricsFile.OpenExternally,
            () => CanOpenExternalLyrics, CommandScope.Application));
        OpenBuiltInEditor = new("Open Built-in Editor", commandFactory.Create(
            "application.open-built-in-editor", editor.Open,
            () => editor.CanOpen, CommandScope.Application));
        AdjustCurrentLineMinus500 = CreateTimingAction(commandFactory,
            "application.lyrics.current-line.minus-500", "-0.5s (Earlier)", -500,
            timing.AdjustCurrentLine, () => CanAdjustCurrentLine);
        AdjustCurrentLineMinus100 = CreateTimingAction(commandFactory,
            "application.lyrics.current-line.minus-100", "-0.1s (Earlier)", -100,
            timing.AdjustCurrentLine, () => CanAdjustCurrentLine);
        AdjustCurrentLinePlus100 = CreateTimingAction(commandFactory,
            "application.lyrics.current-line.plus-100", "+0.1s (Later)", 100,
            timing.AdjustCurrentLine, () => CanAdjustCurrentLine);
        AdjustCurrentLinePlus500 = CreateTimingAction(commandFactory,
            "application.lyrics.current-line.plus-500", "+0.5s (Later)", 500,
            timing.AdjustCurrentLine, () => CanAdjustCurrentLine);
        ShiftAllMinus500 = CreateTimingAction(commandFactory,
            "application.lyrics.all.minus-500", "-0.5s (Earlier)", -500,
            timing.ShiftAll, () => CanShiftAll);
        ShiftAllMinus100 = CreateTimingAction(commandFactory,
            "application.lyrics.all.minus-100", "-0.1s (Earlier)", -100,
            timing.ShiftAll, () => CanShiftAll);
        ShiftAllPlus100 = CreateTimingAction(commandFactory,
            "application.lyrics.all.plus-100", "+0.1s (Later)", 100,
            timing.ShiftAll, () => CanShiftAll);
        ShiftAllPlus500 = CreateTimingAction(commandFactory,
            "application.lyrics.all.plus-500", "+0.5s (Later)", 500,
            timing.ShiftAll, () => CanShiftAll);

        _timing.AvailabilityChanged += OnTimingAvailabilityChanged;
        _lyricsFile.AvailabilityChanged += OnExternalLyricsAvailabilityChanged;
        _lyricsFile.StatusChanged += OnExternalLyricsStatusChanged;
        _editor.AvailabilityChanged += OnEditorAvailabilityChanged;
        Logger.LogDebug("Lyrics-surface actions initialized for {ViewModelType}.", GetType().Name);
    }

    private LyricsSurfaceActionDefinition CreateTimingAction(ICommandFactory commandFactory,
        string name, string header, long deltaMs, Func<long, TimingAdjustmentResult> adjust,
        Func<bool> canExecute) => new(header, commandFactory.Create(name, () =>
        {
            var result = adjust(deltaMs);
            if (!result.Succeeded)
                Logger.LogWarning("Timing adjustment failed: {Error}", result.Error ?? result.Status.ToString());
        }, canExecute, CommandScope.Application));

    private void OnTimingAvailabilityChanged()
    {
        var canAdjustCurrent = _timing.CanAdjustCurrentLine;
        var canShiftAll = _timing.CanShiftAll;
        if (_canAdjustCurrentLine != canAdjustCurrent)
        {
            _canAdjustCurrentLine = canAdjustCurrent;
            OnPropertyChanged(nameof(CanAdjustCurrentLine));
            Raise(AdjustCurrentLineMinus500, AdjustCurrentLineMinus100,
                AdjustCurrentLinePlus100, AdjustCurrentLinePlus500);
        }
        if (_canShiftAll != canShiftAll)
        {
            _canShiftAll = canShiftAll;
            OnPropertyChanged(nameof(CanShiftAll));
            Raise(ShiftAllMinus500, ShiftAllMinus100, ShiftAllPlus100, ShiftAllPlus500);
        }
    }

    private void OnExternalLyricsAvailabilityChanged()
    {
        var canOpen = _lyricsFile.CanOpen;
        if (_canOpenExternalLyrics == canOpen) return;
        _canOpenExternalLyrics = canOpen;
        OnPropertyChanged(nameof(CanOpenExternalLyrics));
        OpenLrcExternally.Command.RaiseCanExecuteChanged();
    }

    private void OnExternalLyricsStatusChanged()
    {
        var status = _lyricsFile.Status;
        if (string.Equals(_externalLyricsStatus, status, StringComparison.Ordinal)) return;
        _externalLyricsStatus = status;
        OnPropertyChanged(nameof(ExternalLyricsStatus));
    }

    private void OnEditorAvailabilityChanged() => OpenBuiltInEditor.Command.RaiseCanExecuteChanged();

    private static void Raise(params LyricsSurfaceActionDefinition[] actions)
    {
        foreach (var action in actions) action.Command.RaiseCanExecuteChanged();
    }
}
