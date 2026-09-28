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
    event Action<double>? WidthChangeCompleted;
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
        _lyrics = lyrics;
        _timeline = timeline;
        var next = LyricsOverlayPresentationState.FromLyrics(lyrics, timeline, _interaction.ContentMode);
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
        if (!double.IsFinite(value) || value < OverlayPreferences.MinimumWidth ||
            value > OverlayPreferences.MaximumWidth)
            throw new ArgumentOutOfRangeException(nameof(value));
        ChangeInteraction(_interaction with { Width = value }, $"width changed to {value:0}");
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
        _view.WidthChangeCompleted -= OnWidthChangeCompleted;
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
        _view.WidthChangeCompleted += OnWidthChangeCompleted;
        _view.ApplyInteractionState(_interaction);
        _view.ApplyTimingState(_currentLineTimingEnabled, _globalTimingEnabled, _globalOffsetMs);
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
            _settingsStore.SaveOverlayPosition(position);
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
        var sizeChanged = next.Width != _interaction.Width || next.ContentMode != _interaction.ContentMode;
        var contentModeChanged = next.ContentMode != _interaction.ContentMode;
        _interaction = next;
        _view?.ApplyInteractionState(next);

        if (contentModeChanged)
        {
            var presentation = LyricsOverlayPresentationState.FromLyrics(_lyrics, _timeline, next.ContentMode);
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
            if (placement.UsedFallback) _view.SetPosition(placement.Position);
        }

        try
        {
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

    private void OnWidthChangeCompleted(double width)
    {
        if (double.IsFinite(width) && width >= OverlayPreferences.MinimumWidth &&
            width <= OverlayPreferences.MaximumWidth)
            SetWidth(width);
    }
}
