using System.IO;
using System.Windows;
using LyricsDisplayer.Core.Settings;
using LyricsDisplayer.Core.Protocol;
using LyricsDisplayer.Core.Timeline;
using Microsoft.Extensions.Logging;

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
    event Action<double, double>? OverlaySizeChanged;
    event Action<OverlayPosition, double, double>? GeometryChangeCompleted;
    void SetPosition(OverlayPosition position);
    void SetLyricsSurfaceActions(IOverlayLyricsSurfaceActionsViewModel actions);
    void SetLyrics(LyricsOverlayPresentationState state);
    void ApplyInteractionState(OverlayInteractionState state, bool effectiveTopmost);
    void NormalizeWindowState();
    void CompleteGeometryRecovery();
    void ShowWithoutActivation();
    void Hide();
    void CloseForApplicationShutdown();
}

public sealed class LyricsOverlayController : ILyricsOverlayInteractionService
{
    private readonly Func<ILyricsOverlayView> _viewFactory;
    private readonly IOverlaySettingsStore _settingsStore;
    private readonly Func<IReadOnlyList<OverlayWorkArea>> _workAreas;
    private readonly ILogger<LyricsOverlayController> _logger;
    private ILyricsOverlayView? _view;
    private LyricsOverlayPresentationState _presentation = LyricsOverlayPresentationState.Empty;
    private LyricsSnapshotPayload? _lyrics;
    private IReadOnlyList<LyricsLine> _timelineOrderedLines = Array.Empty<LyricsLine>();
    private LyricsTimelinePosition _timeline = LyricsTimeline.Empty.Evaluate(0);
    private LyricsPresentationState _sharedPresentation = LyricsPresentationState.Pending;
    private LyricsSnapshotPayload? _mappedLyrics;
    private IReadOnlyList<LyricsLine>? _mappedTimelineOrderedLines;
    private int? _mappedCurrentIndex;
    private int? _mappedNextIndex;
    private bool _mappedLocalFileMissing;
    private bool _hasMappedPresentation;
    private OverlayInteractionState _interaction;
    private bool _reportedVisible;
    private bool _shuttingDown;
    private bool _editorModalTopmostSuppressed;
    private IOverlayLyricsSurfaceActionsViewModel? _lyricsActions;
    private bool _localFileMissing;

    public LyricsOverlayController(
        ILogger<LyricsOverlayController> logger,
        Func<ILyricsOverlayView> viewFactory,
        IOverlaySettingsStore settingsStore,
        Func<IReadOnlyList<OverlayWorkArea>> workAreas)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _viewFactory = viewFactory;
        _settingsStore = settingsStore;
        _workAreas = workAreas;
        _interaction = LoadInteractionState();
    }

    public event Action<bool>? VisibilityChanged;
    public event Action<OverlayInteractionState>? InteractionStateChanged;
    public event Action? OpenControlPanelRequested;

    public bool IsVisible => _reportedVisible;
    public bool HasCreatedWindow => _view is not null;
    public LyricsOverlayPresentationState Presentation => _presentation;
    public OverlayInteractionState Interaction => _interaction;
    public bool EffectiveTopmost => !_editorModalTopmostSuppressed;

    public void Update(LyricsSnapshotPayload? lyrics, LyricsTimelinePosition timeline,
        bool localFileMissing = false, IReadOnlyList<LyricsLine>? timelineOrderedLines = null)
    {
        _lyrics = lyrics;
        _timeline = timeline;
        _timelineOrderedLines = timelineOrderedLines ?? lyrics?.Lines ?? Array.Empty<LyricsLine>();
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

    public void SetEditorModalTopmostSuppressed(bool suppressed)
    {
        if (_editorModalTopmostSuppressed == suppressed) return;
        _editorModalTopmostSuppressed = suppressed;
        ApplyInteractionState();
    }

    public void SetContentMode(LyricsContentMode value) =>
        ChangeInteraction(_interaction with
        {
            ContentMode = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value))
        }, $"content mode changed to {value}");

    public void SetLyricsSurfaceActions(IOverlayLyricsSurfaceActionsViewModel actions)
    {
        _lyricsActions = actions ?? throw new ArgumentNullException(nameof(actions));
        _view?.SetLyricsSurfaceActions(actions);
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
        ApplyInteractionState();
        _view!.ShowWithoutActivation();
        _reportedVisible = true;
        _logger.LogInformation("Desktop lyrics overlay shown.");
        VisibilityChanged?.Invoke(true);
    }

    public void Hide()
    {
        if (_view is null || !_reportedVisible) return;
        _view.Hide();
        ReportHidden();
    }

    public void RequestOpenLyricsWindow() => OpenControlPanelRequested?.Invoke();

    public void Shutdown()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        if (_view is null) return;
        _view.CloseRequested -= OnCloseRequested;
        _view.DragCompleted -= OnDragCompleted;
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
        _view.OverlaySizeChanged += OnViewSizeChanged;
        _view.GeometryChangeCompleted += OnGeometryChangeCompleted;
        ApplyInteractionState();
        if (_lyricsActions is not null) _view.SetLyricsSurfaceActions(_lyricsActions);
        _presentation = CreatePresentation(_lyrics, _timeline);
        _view.SetLyrics(_presentation);

        var saved = _settingsStore.LoadOverlayPosition();
        var placement = OverlayPositionResolver.Resolve(saved, _view.OverlayWidth, _view.OverlayHeight, _workAreas());
        ApplyResolvedGeometry(placement);
        _view.SetPosition(placement.Position);
        if (saved is not null && placement.UsedFallback)
            _logger.LogWarning("Saved overlay position was not visible; using fallback position.");
        else if (saved is not null)
            _logger.LogInformation("Desktop lyrics overlay position restored.");
    }

    private void RecoverAbnormalWindowStateIfNeeded()
    {
        if (_view is null ||
            (_view.WindowState == WindowState.Normal && !_view.GeometryRecoveryRequired)) return;

        _view.NormalizeWindowState();
        ApplyInteractionState();
        var savedPosition = _settingsStore.LoadOverlayPosition();
        var placement = OverlayPositionResolver.Resolve(
            savedPosition,
            _interaction.Width,
            _interaction.Height,
            _workAreas());
        ApplyResolvedGeometry(placement);
        _view.SetPosition(placement.Position);
        _view.CompleteGeometryRecovery();
        _logger.LogWarning("Abnormal overlay window state was restored to normal geometry.");
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
            _logger.LogWarning(exception, "Desktop lyrics geometry recovery could not be saved.");
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
        _logger.LogInformation("Desktop lyrics overlay hidden.");
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
            _logger.LogWarning(exception, "Desktop lyrics overlay position could not be saved.");
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
            _logger.LogWarning(exception, "Overlay preferences could not be loaded; defaults are in use.");
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
        ApplyInteractionState();

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
            _logger.LogWarning(exception, "Overlay preferences could not be saved.");
        }
        _logger.LogInformation("Overlay {SettingChange}.", settingName);
        InteractionStateChanged?.Invoke(next);
    }

    private LyricsOverlayPresentationState CreatePresentation(
        LyricsSnapshotPayload? lyrics,
        LyricsTimelinePosition timeline) =>
        LyricsOverlayPresentationState.FromPresentation(GetSharedPresentation(lyrics, timeline), _interaction.ContentMode,
            _view?.OverlayWidth ?? _interaction.Width,
            _view?.OverlayHeight ?? _interaction.Height);

    private LyricsPresentationState GetSharedPresentation(
        LyricsSnapshotPayload? lyrics,
        LyricsTimelinePosition timeline)
    {
        if (_hasMappedPresentation && ReferenceEquals(_mappedLyrics, lyrics) &&
            ReferenceEquals(_mappedTimelineOrderedLines, _timelineOrderedLines) &&
            _mappedCurrentIndex == timeline.CurrentIndex && _mappedNextIndex == timeline.NextIndex &&
            _mappedLocalFileMissing == _localFileMissing)
            return _sharedPresentation;

        _sharedPresentation = LyricsPresentationMapper.FromResolvedTimeline(
            lyrics, timeline, _timelineOrderedLines, _localFileMissing);
        _mappedLyrics = lyrics;
        _mappedTimelineOrderedLines = _timelineOrderedLines;
        _mappedCurrentIndex = timeline.CurrentIndex;
        _mappedNextIndex = timeline.NextIndex;
        _mappedLocalFileMissing = _localFileMissing;
        _hasMappedPresentation = true;
        return _sharedPresentation;
    }

    private void OnViewSizeChanged(double width, double height)
    {
        if (_view is null || _view.WindowState != WindowState.Normal || _view.GeometryRecoveryRequired) return;
        var next = LyricsOverlayPresentationState.FromPresentation(
            GetSharedPresentation(_lyrics, _timeline), _interaction.ContentMode, width, height);
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
            ApplyInteractionState();
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
            _logger.LogWarning(exception, "Desktop lyrics geometry could not be saved.");
        }
    }

    private void ApplyResolvedGeometry(OverlayPlacement placement)
    {
        if (_view is null) return;
        if (Math.Abs(placement.Width - _interaction.Width) < 0.1 &&
            Math.Abs(placement.Height - _interaction.Height) < 0.1) return;
        _interaction = _interaction with { Width = placement.Width, Height = placement.Height };
        ApplyInteractionState();
    }

    private void ApplyInteractionState() => _view?.ApplyInteractionState(_interaction, EffectiveTopmost);
}
