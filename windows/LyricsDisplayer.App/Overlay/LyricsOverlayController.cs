using System.IO;
using LyricsDisplayer.Core.Settings;
using LyricsDisplayer.Core.Protocol;
using LyricsDisplayer.Core.Timeline;

namespace LyricsDisplayer;

public interface ILyricsOverlayView
{
    bool IsVisible { get; }
    double Left { get; }
    double Top { get; }
    double OverlayWidth { get; }
    double OverlayHeight { get; }
    event Action? CloseRequested;
    event Action<OverlayPosition>? DragCompleted;
    event Action<OverlayCommand>? CommandRequested;
    event Action<double, double>? OverlaySizeChanged;
    event Action<OverlayPosition, double, double>? GeometryChangeCompleted;
    void SetPosition(OverlayPosition position);
    void SetLyrics(LyricsOverlayPresentationState state);
    void ApplyInteractionState(OverlayInteractionState state);
    void ApplyTimingState(bool currentLineEnabled, bool globalTimingEnabled, long globalOffsetMs);
    void ShowWithoutActivation();
    void Hide();
    void CloseForApplicationShutdown();
}

public sealed class LyricsOverlayController
{
    private readonly Func<ILyricsOverlayView> _viewFactory;
    private readonly IOverlaySettingsStore _settingsStore;
    private readonly Func<IReadOnlyList<OverlayWorkArea>> _workAreas;
    private readonly Action<string, string, string>? _log;
    private ILyricsOverlayView? _view;
    private LyricsOverlayPresentationState _presentation = LyricsOverlayPresentationState.Empty;
    private LyricsSnapshotPayload? _lyrics;
    private IReadOnlyList<LyricsLine>? _sourceLines;
    private IReadOnlyList<LyricsLine>? _normalizedLines;
    private LyricsTimelinePosition _timeline = LyricsTimeline.Empty.Evaluate(0);
    private OverlayInteractionState _interaction;
    private bool _currentLineTimingEnabled;
    private bool _globalTimingEnabled;
    private long _globalOffsetMs;
    private bool _reportedVisible;
    private bool _shuttingDown;

    public LyricsOverlayController(
        Func<ILyricsOverlayView> viewFactory,
        IOverlaySettingsStore settingsStore,
        Func<IReadOnlyList<OverlayWorkArea>> workAreas,
        Action<string, string, string>? log = null)
    {
        _viewFactory = viewFactory;
        _settingsStore = settingsStore;
        _workAreas = workAreas;
        _log = log;
        _interaction = LoadInteractionState();
    }

    public event Action<bool>? VisibilityChanged;
    public event Action<OverlayInteractionState>? InteractionStateChanged;
    public event Action? OpenControlPanelRequested;
    public event Action<OverlayCommand>? TimingCommandRequested;

    public bool IsVisible => _reportedVisible;
    public bool HasCreatedWindow => _view is not null;
    public LyricsOverlayPresentationState Presentation => _presentation;
    public OverlayInteractionState Interaction => _interaction;

    public void Update(LyricsSnapshotPayload? lyrics, LyricsTimelinePosition timeline)
    {
        if (!ReferenceEquals(_sourceLines, lyrics?.Lines))
        {
            _sourceLines = lyrics?.Lines;
            _normalizedLines = _sourceLines is null ? null : LyricsOverlayPresentationState.NormalizeLines(_sourceLines);
        }
        _lyrics = lyrics;
        _timeline = timeline;
        var next = CreatePresentation(lyrics, timeline);
        if (_presentation.EquivalentTo(next)) return;
        _presentation = next;
        _view?.SetLyrics(next);
    }

    public void ToggleVisibility()
    {
        if (_reportedVisible) Hide();
        else Show();
    }

    public void SetLocked(bool value) =>
        ChangeInteraction(_interaction with { Locked = value }, $"lock {(value ? "enabled" : "disabled")}");

    public void SetClickThrough(bool value) =>
        ChangeInteraction(_interaction with { ClickThrough = value },
            $"click-through {(value ? "enabled" : "disabled")}");

    public void SetTopmost(bool value) =>
        ChangeInteraction(_interaction with { Topmost = value }, $"topmost {(value ? "enabled" : "disabled")}");

    public void SetContentMode(LyricsContentMode value) =>
        ChangeInteraction(_interaction with
        {
            ContentMode = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value))
        }, $"content mode changed to {value}");

    public void SetTimingAvailability(bool currentLineEnabled, bool globalTimingEnabled, long globalOffsetMs)
    {
        _currentLineTimingEnabled = currentLineEnabled;
        _globalTimingEnabled = globalTimingEnabled;
        _globalOffsetMs = globalOffsetMs;
        _view?.ApplyTimingState(currentLineEnabled, globalTimingEnabled, globalOffsetMs);
    }

    public void SetWidth(double value)
    {
        if (!double.IsFinite(value) || value < OverlayPreferences.MinimumWidth)
            throw new ArgumentOutOfRangeException(nameof(value));
        ChangeInteraction(_interaction with { Width = value }, $"width changed to {value:0}");
    }

    public void SetHeight(double value)
    {
        if (!double.IsFinite(value) || value < OverlayPreferences.MinimumHeight)
            throw new ArgumentOutOfRangeException(nameof(value));
        ChangeInteraction(_interaction with { Height = value }, $"height changed to {value:0}");
    }

    public void Show()
    {
        if (_shuttingDown) return;
        EnsureView();
        if (_reportedVisible) return;
        _view!.ShowWithoutActivation();
        _reportedVisible = true;
        _log?.Invoke("Information", "Overlay", "Desktop lyrics overlay shown.");
        VisibilityChanged?.Invoke(true);
    }

    public void Hide()
    {
        if (_view is null || !_reportedVisible) return;
        _view.Hide();
        ReportHidden();
    }

    public void Shutdown()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        if (_view is null) return;
        _view.CloseRequested -= OnCloseRequested;
        _view.DragCompleted -= OnDragCompleted;
        _view.CommandRequested -= OnCommandRequested;
        _view.OverlaySizeChanged -= OnViewSizeChanged;
        _view.GeometryChangeCompleted -= OnGeometryChangeCompleted;
        _view.CloseForApplicationShutdown();
        _view = null;
        _reportedVisible = false;
    }

    private void EnsureView()
    {
        if (_view is not null) return;
        _view = _viewFactory();
        _view.CloseRequested += OnCloseRequested;
        _view.DragCompleted += OnDragCompleted;
        _view.CommandRequested += OnCommandRequested;
        _view.OverlaySizeChanged += OnViewSizeChanged;
        _view.GeometryChangeCompleted += OnGeometryChangeCompleted;
        _view.ApplyInteractionState(_interaction);
        _view.ApplyTimingState(_currentLineTimingEnabled, _globalTimingEnabled, _globalOffsetMs);
        _presentation = CreatePresentation(_lyrics, _timeline);
        _view.SetLyrics(_presentation);

        var saved = _settingsStore.LoadOverlayPosition();
        var placement = OverlayPositionResolver.Resolve(saved, _view.OverlayWidth, _view.OverlayHeight, _workAreas());
        _view.SetPosition(placement.Position);
        if (saved is not null && placement.UsedFallback)
            _log?.Invoke("Warning", "Overlay", "Saved overlay position was not visible; using fallback position.");
        else if (saved is not null)
            _log?.Invoke("Information", "Overlay", "Desktop lyrics overlay position restored.");
    }

    private void OnCloseRequested()
    {
        if (_shuttingDown) return;
        if (_view?.IsVisible == true) _view.Hide();
        ReportHidden();
    }

    private void ReportHidden()
    {
        if (!_reportedVisible) return;
        _reportedVisible = false;
        _log?.Invoke("Information", "Overlay", "Desktop lyrics overlay hidden.");
        VisibilityChanged?.Invoke(false);
    }

    private void OnDragCompleted(OverlayPosition position)
    {
        if (!_interaction.CanDragOnLyrics) return;
        try
        {
            _settingsStore.SaveOverlayGeometry(position, _interaction.ToPreferences());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _log?.Invoke("Warning", "Overlay", $"Desktop lyrics overlay position could not be saved ({exception.GetType().Name}).");
        }
    }

    private OverlayInteractionState LoadInteractionState()
    {
        try
        {
            return OverlayInteractionState.FromPreferences(_settingsStore.LoadOverlayPreferences());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _log?.Invoke("Warning", "Overlay",
                $"Overlay preferences could not be loaded ({exception.GetType().Name}); defaults are in use.");
            return OverlayInteractionState.FromPreferences(OverlayPreferences.Default);
        }
    }

    private void ChangeInteraction(OverlayInteractionState next, string settingName)
    {
        if (next == _interaction) return;
        var sizeChanged = next.Width != _interaction.Width || next.Height != _interaction.Height;
        var contentModeChanged = next.ContentMode != _interaction.ContentMode;
        OverlayPosition? recoveredPosition = null;
        _interaction = next;
        _view?.ApplyInteractionState(next);

        if (contentModeChanged)
        {
            var presentation = CreatePresentation(_lyrics, _timeline);
            if (!_presentation.EquivalentTo(presentation))
            {
                _presentation = presentation;
                _view?.SetLyrics(presentation);
            }
        }

        if (sizeChanged && _view is not null)
        {
            var placement = OverlayPositionResolver.Resolve(
                new OverlayPosition(_view.Left, _view.Top),
                _view.OverlayWidth,
                _view.OverlayHeight,
                _workAreas());
            if (placement.UsedFallback)
            {
                _view.SetPosition(placement.Position);
                recoveredPosition = placement.Position;
            }
        }

        try
        {
            if (recoveredPosition is { } position)
                _settingsStore.SaveOverlayGeometry(position, next.ToPreferences());
            else
                _settingsStore.SaveOverlayPreferences(next.ToPreferences());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _log?.Invoke("Warning", "Overlay",
                $"Overlay preferences could not be saved ({exception.GetType().Name}).");
        }
        _log?.Invoke("Information", "Overlay", $"Overlay {settingName}.");
        InteractionStateChanged?.Invoke(next);
    }

    private void OnCommandRequested(OverlayCommand command)
    {
        switch (command)
        {
            case OverlayCommand.OpenControlPanel:
                OpenControlPanelRequested?.Invoke();
                break;
            case OverlayCommand.ToggleLocked:
                SetLocked(!_interaction.Locked);
                break;
            case OverlayCommand.ToggleClickThrough:
                SetClickThrough(!_interaction.ClickThrough);
                break;
            case OverlayCommand.ToggleTopmost:
                SetTopmost(!_interaction.Topmost);
                break;
            case OverlayCommand.SetOneLine:
                SetContentMode(LyricsContentMode.OneLine);
                break;
            case OverlayCommand.SetTwoLines:
                SetContentMode(LyricsContentMode.TwoLines);
                break;
            case OverlayCommand.SetAllLyrics:
                SetContentMode(LyricsContentMode.AllLyrics);
                break;
            case OverlayCommand.Hide:
                Hide();
                break;
            case OverlayCommand.AdjustCurrentLineMinus500:
            case OverlayCommand.AdjustCurrentLineMinus100:
            case OverlayCommand.AdjustCurrentLinePlus100:
            case OverlayCommand.AdjustCurrentLinePlus500:
            case OverlayCommand.AdjustGlobalMinus500:
            case OverlayCommand.AdjustGlobalMinus100:
            case OverlayCommand.ResetGlobalTiming:
            case OverlayCommand.AdjustGlobalPlus100:
            case OverlayCommand.AdjustGlobalPlus500:
                TimingCommandRequested?.Invoke(command);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command));
        }
    }

    private LyricsOverlayPresentationState CreatePresentation(
        LyricsSnapshotPayload? lyrics,
        LyricsTimelinePosition timeline) =>
        LyricsOverlayPresentationState.FromLyrics(lyrics, timeline, _interaction.ContentMode,
            _view?.OverlayWidth ?? _interaction.Width,
            _view?.OverlayHeight ?? _interaction.Height,
            _normalizedLines);

    private void OnViewSizeChanged(double width, double height)
    {
        var next = LyricsOverlayPresentationState.FromLyrics(_lyrics, _timeline, _interaction.ContentMode,
            width, height, _normalizedLines);
        if (_presentation.EquivalentTo(next)) return;
        _presentation = next;
        _view?.SetLyrics(next);
    }

    private void OnGeometryChangeCompleted(OverlayPosition position, double width, double height)
    {
        if (!position.IsFinite || !double.IsFinite(width) || width < OverlayPreferences.MinimumWidth ||
            !double.IsFinite(height) || height < OverlayPreferences.MinimumHeight) return;

        _interaction = _interaction with { Width = width, Height = height };
        var placement = OverlayPositionResolver.Resolve(position, width, height, _workAreas());
        if (_view is not null && placement.UsedFallback)
        {
            _view.SetPosition(placement.Position);
            position = placement.Position;
        }
        try
        {
            _settingsStore.SaveOverlayGeometry(position, _interaction.ToPreferences());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _log?.Invoke("Warning", "Overlay", $"Desktop lyrics geometry could not be saved ({exception.GetType().Name}).");
        }
    }
}
