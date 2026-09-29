using System.IO;
using System.Windows;
using LyricsDisplayer.Core.Settings;
using LyricsDisplayer.Core.Protocol;
using LyricsDisplayer.Core.Timeline;

namespace LyricsDisplayer;

public interface ILyricsOverlayView
{
    bool IsVisible { get; }
    WindowState WindowState { get; }
    bool GeometryRecoveryRequired { get; }
    double Left { get; }
    double Top { get; }
    double OverlayWidth { get; }
    double OverlayHeight { get; }
    event Action? CloseRequested;
    event Action<OverlayPosition, double, double>? DragCompleted;
    event Action<OverlayCommand>? CommandRequested;
    event Action<double, double>? OverlaySizeChanged;
    event Action<OverlayPosition, double, double>? GeometryChangeCompleted;
    void SetPosition(OverlayPosition position);
    void SetLyrics(LyricsOverlayPresentationState state);
    void ApplyInteractionState(OverlayInteractionState state);
    void ApplyExternalLyricsAvailability(bool canOpen);
    void ApplyBuiltInEditorAvailability(bool canOpen) { }
    void ApplyTimingState(bool currentLineEnabled, bool globalTimingEnabled, long globalOffsetMs);
    void NormalizeWindowState();
    void CompleteGeometryRecovery();
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
    private bool _canOpenExternalLyrics;
    private bool _canOpenBuiltInEditor;
    private bool _localFileMissing;

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
    public event Action? OpenExternalLyricsRequested;
    public event Action? OpenBuiltInEditorRequested;

    public bool IsVisible => _reportedVisible;
    public bool HasCreatedWindow => _view is not null;
    public LyricsOverlayPresentationState Presentation => _presentation;
    public OverlayInteractionState Interaction => _interaction;

    public void Update(LyricsSnapshotPayload? lyrics, LyricsTimelinePosition timeline, bool localFileMissing = false)
    {
        if (!ReferenceEquals(_sourceLines, lyrics?.Lines))
        {
            _sourceLines = lyrics?.Lines;
            _normalizedLines = _sourceLines is null ? null : LyricsOverlayPresentationState.NormalizeLines(_sourceLines);
        }
        _lyrics = lyrics;
        _timeline = timeline;
        _localFileMissing = localFileMissing;
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

    public void SetExternalLyricsAvailability(bool canOpen)
    {
        _canOpenExternalLyrics = canOpen;
        _view?.ApplyExternalLyricsAvailability(canOpen);
    }

    public void SetBuiltInEditorAvailability(bool canOpen)
    {
        _canOpenBuiltInEditor = canOpen;
        _view?.ApplyBuiltInEditorAvailability(canOpen);
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
        RecoverAbnormalWindowStateIfNeeded();
        RecoverGeometryForCurrentWorkAreas();
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
        _view.ApplyExternalLyricsAvailability(_canOpenExternalLyrics);
        _view.ApplyBuiltInEditorAvailability(_canOpenBuiltInEditor);
        _view.ApplyTimingState(_currentLineTimingEnabled, _globalTimingEnabled, _globalOffsetMs);
        _presentation = CreatePresentation(_lyrics, _timeline);
        _view.SetLyrics(_presentation);

        var saved = _settingsStore.LoadOverlayPosition();
        var placement = OverlayPositionResolver.Resolve(saved, _view.OverlayWidth, _view.OverlayHeight, _workAreas());
        ApplyResolvedGeometry(placement);
        _view.SetPosition(placement.Position);
        if (saved is not null && placement.UsedFallback)
            _log?.Invoke("Warning", "Overlay", "Saved overlay position was not visible; using fallback position.");
        else if (saved is not null)
            _log?.Invoke("Information", "Overlay", "Desktop lyrics overlay position restored.");
    }

    private void RecoverAbnormalWindowStateIfNeeded()
    {
        if (_view is null ||
            (_view.WindowState == WindowState.Normal && !_view.GeometryRecoveryRequired)) return;

        _view.NormalizeWindowState();
        _view.ApplyInteractionState(_interaction);
        var savedPosition = _settingsStore.LoadOverlayPosition();
        var placement = OverlayPositionResolver.Resolve(
            savedPosition,
            _interaction.Width,
            _interaction.Height,
            _workAreas());
        ApplyResolvedGeometry(placement);
        _view.SetPosition(placement.Position);
        _view.CompleteGeometryRecovery();
        _log?.Invoke("Warning", "Overlay", "Abnormal overlay window state was restored to normal geometry.");
    }

    private void RecoverGeometryForCurrentWorkAreas()
    {
        if (_view is null || _view.WindowState != WindowState.Normal || _view.GeometryRecoveryRequired) return;
        var placement = OverlayPositionResolver.Resolve(
            new OverlayPosition(_view.Left, _view.Top), _view.OverlayWidth, _view.OverlayHeight, _workAreas());
        if (!placement.UsedFallback) return;

        ApplyResolvedGeometry(placement);
        _view.SetPosition(placement.Position);
        try
        {
            _settingsStore.SaveOverlayGeometry(placement.Position, _interaction.ToPreferences());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _log?.Invoke("Warning", "Overlay", $"Desktop lyrics geometry recovery could not be saved ({exception.GetType().Name}).");
        }
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

    private void OnDragCompleted(OverlayPosition position, double width, double height)
    {
        if (!_interaction.CanDragOnLyrics || _view is null ||
            _view.WindowState != WindowState.Normal || _view.GeometryRecoveryRequired) return;
        if (!position.IsFinite || !double.IsFinite(width) || width <= 0 ||
            !double.IsFinite(height) || height <= 0) return;
        _interaction = _interaction with { Width = width, Height = height };
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
            ApplyResolvedGeometry(placement);
            if (placement.UsedFallback)
            {
                _view.SetPosition(placement.Position);
                recoveredPosition = placement.Position;
            }
        }

        try
        {
            if (recoveredPosition is { } position)
                _settingsStore.SaveOverlayGeometry(position, _interaction.ToPreferences());
            else
                _settingsStore.SaveOverlayPreferences(_interaction.ToPreferences());
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
            case OverlayCommand.OpenLrcExternally:
                if (_canOpenExternalLyrics) OpenExternalLyricsRequested?.Invoke();
                break;
            case OverlayCommand.OpenBuiltInEditor:
                if (_canOpenBuiltInEditor) OpenBuiltInEditorRequested?.Invoke();
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
            _normalizedLines,
            _localFileMissing);

    private void OnViewSizeChanged(double width, double height)
    {
        if (_view is null || _view.WindowState != WindowState.Normal || _view.GeometryRecoveryRequired) return;
        var next = LyricsOverlayPresentationState.FromLyrics(_lyrics, _timeline, _interaction.ContentMode,
            width, height, _normalizedLines, _localFileMissing);
        if (_presentation.EquivalentTo(next)) return;
        _presentation = next;
        _view?.SetLyrics(next);
    }

    private void OnGeometryChangeCompleted(OverlayPosition position, double width, double height)
    {
        if (_view is null || _view.WindowState != WindowState.Normal || _view.GeometryRecoveryRequired) return;
        if (!position.IsFinite || !double.IsFinite(width) || width <= 0 ||
            !double.IsFinite(height) || height <= 0) return;

        _interaction = _interaction with { Width = width, Height = height };
        var placement = OverlayPositionResolver.Resolve(position, width, height, _workAreas());
        _interaction = _interaction with { Width = placement.Width, Height = placement.Height };
        if (Math.Abs(width - placement.Width) > 0.1 || Math.Abs(height - placement.Height) > 0.1)
            _view.ApplyInteractionState(_interaction);
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

    private void ApplyResolvedGeometry(OverlayPlacement placement)
    {
        if (_view is null) return;
        if (Math.Abs(placement.Width - _interaction.Width) < 0.1 &&
            Math.Abs(placement.Height - _interaction.Height) < 0.1) return;
        _interaction = _interaction with { Width = placement.Width, Height = placement.Height };
        _view.ApplyInteractionState(_interaction);
    }
}
