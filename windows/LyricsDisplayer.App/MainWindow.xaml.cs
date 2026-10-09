using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Core.Playback;
using LyricsDisplayer.Core.Settings;
using LyricsDisplayer.Infrastructure.Factories;
using LyricsDisplayer.Resources;
using Microsoft.Extensions.Logging;

namespace LyricsDisplayer;

public partial class MainWindow : Window
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ILogger<MainWindow> _logger;
    private readonly IApplicationLifetimeState _lifetime;
    private readonly LyricsLibrary _library;
    private readonly PlaybackStateCoordinator _playbackState;
    private readonly NamedPipeServer _server;
    private readonly DispatcherTimer _positionRefreshTimer;
    private readonly DispatcherTimer _manualScrollResumeTimer;
    private readonly LyricsOverlayController _overlay;
    private readonly IMainLyricsViewModel _viewModel;
    private readonly IOverlayLyricsSurfaceActionsViewModel _overlayActions;
    private readonly IDebugSettingsPageViewModel _debugSettingsPageViewModel;
    private readonly ICurrentLyricsFileService _currentLyricsFile;
    private readonly IBuiltInLyricsEditorService _editorService;
    private readonly SettingsWindowService _settingsWindowService;
    private readonly IActiveLrcFileWatcherFactory _lrcWatcherFactory;
    private readonly IGlobalHotkeyServiceFactory _hotkeyFactory;
    private readonly ITrayLifecycleServiceFactory _trayFactory;
    private readonly MainLyricsAutoFollowPolicy _autoFollowPolicy = new();
    private PlaybackTrackIdentity? _autoFollowTrackIdentity;
    private ScrollViewer? _lyricsScrollViewer;
    private DispatcherOperation? _pendingAutoCenterOperation;
    private Task? _serverTask;
    private GlobalHotkeyService? _globalHotkeys;
    private TrayLifecycleService? _tray;
    private ActiveLrcFileWatcher? _activeLrcWatcher;
    private string? _watchedLocalTrackId;
    private string? _watchedLrcPath;
    private int _externalLrcGeneration;
    private bool _allowApplicationExit;
    private bool _applicationExitPending;
    private readonly bool _hideToTrayOnClose;
    private bool _isProgrammaticScroll;

    private readonly record struct PlaybackTrackIdentity(string Source, string SourceTrackId);

    public MainWindow(ILogger<MainWindow> logger, IApplicationLifetimeState lifetime, LyricsLibrary library,
        IOverlaySettingsStore settingsStore, PlaybackStateCoordinator playbackState, NamedPipeServer server,
        LyricsOverlayController overlay, ICurrentLyricsFileService currentLyricsFile,
        IBuiltInLyricsEditorService editorService, IMainLyricsViewModel viewModel,
        IOverlayLyricsSurfaceActionsViewModel overlayActions,
        IDebugSettingsPageViewModel debugSettingsPageViewModel, IActiveLrcFileWatcherFactory lrcWatcherFactory,
        IGlobalHotkeyServiceFactory hotkeyFactory, ITrayLifecycleServiceFactory trayFactory,
        SettingsWindowService settingsWindowService)
    {
        InitializeComponent();
        _logger = logger;
        _lifetime = lifetime;
        _library = library;
        _debugSettingsPageViewModel = debugSettingsPageViewModel;
        _currentLyricsFile = currentLyricsFile;
        _editorService = editorService;
        _settingsWindowService = settingsWindowService;
        _playbackState = playbackState;
        _server = server;
        _overlay = overlay;
        _lrcWatcherFactory = lrcWatcherFactory;
        _hotkeyFactory = hotkeyFactory;
        _trayFactory = trayFactory;
        _overlayActions = overlayActions;
        _playbackState.LocalMetadataChanged += OnLocalMetadataChanged;
        _overlay.VisibilityChanged += OnOverlayVisibilityChanged;
        _overlay.OpenControlPanelRequested += OpenLyricsWindow;
        _viewModel = viewModel;
        _viewModel.CurrentLineChanged += OnCurrentLineChanged;
        DataContext = _viewModel;
        _overlay.SetLyricsSurfaceActions(_overlayActions);
        _server.ConnectionStatusChanged += status => Dispatcher.InvokeAsync(() =>
        {
            _viewModel.SetConnectionStatus(status);
            _debugSettingsPageViewModel.SetTransportStatus(status);
        });
        _server.SnapshotAccepted += _ => Dispatcher.InvokeAsync(DisplayLyrics);
        _server.LyricsChanged += () => Dispatcher.InvokeAsync(DisplayLyrics);
        _positionRefreshTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _positionRefreshTimer.Tick += (_, _) => RefreshLocalPosition();
        _manualScrollResumeTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = MainLyricsAutoFollowPolicy.ManualScrollResumeDelay
        };
        _manualScrollResumeTimer.Tick += OnManualScrollResumeTimerTick;
        Loaded += OnLoaded;
        Activated += OnActivated;
        Closing += OnClosing;
        SizeChanged += OnMainWindowSizeChanged;
        _hideToTrayOnClose = settingsStore.LoadCloseControlPanelToTray();
        DisplayLyrics();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _lyricsScrollViewer ??= FindVisualChild<ScrollViewer>(LyricsListBox);
        if (_globalHotkeys is not null) return;
        _serverTask = RunServerSafelyAsync();
        _positionRefreshTimer.Start();
        _globalHotkeys = _hotkeyFactory.Create(new WindowInteropHelper(this).Handle,
            _overlay.ToggleVisibility,
            () => _overlay.SetClickThrough(!_overlay.Interaction.ClickThrough));
        _globalHotkeys.Start();
        if (_globalHotkeys.RegisteredIds.Count != 2)
            _logger.LogWarning("One or more global shortcuts are already in use and could not be registered.");
        _tray = _trayFactory.Create(OpenLyricsWindow, ToggleOverlayFromTray, ExitApplication);
        _tray.Start(_overlay.IsVisible);
        QueueCurrentLineAutoCenter();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        var hideToTray = !_lifetime.IsFatalShutdown && ControlPanelClosePolicy.ShouldHideToTray(
            _hideToTrayOnClose, _allowApplicationExit);
        _applicationExitPending = _lifetime.IsFatalShutdown || !hideToTray;
        if (!_editorService.TryCloseForOwnerClosing(_applicationExitPending))
        {
            e.Cancel = true;
            _applicationExitPending = false;
            _allowApplicationExit = false;
            _editorService.CancelOwnerClose();
            return;
        }
        if (hideToTray)
        {
            e.Cancel = true;
            _applicationExitPending = false;
            ResetAutoFollowForHiddenWindow();
            Hide();
            _logger.LogInformation("Main Lyrics Window hidden to the system tray.");
            return;
        }
        _settingsWindowService.CloseForApplicationExit();
        _positionRefreshTimer.Stop();
        StopManualScrollResumeTimer();
        CancelPendingAutoCenter();
        _autoFollowPolicy.Reset();
        _activeLrcWatcher?.Dispose();
        _activeLrcWatcher = null;
        _externalLrcGeneration++;
        _globalHotkeys?.Dispose();
        _tray?.Dispose();
        _overlay.Shutdown();
        _shutdown.Cancel();
    }

    private async Task RunServerSafelyAsync()
    {
        try
        {
            await _server.RunAsync(_shutdown.Token);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Named Pipe server stopped unexpectedly.");
            await Dispatcher.InvokeAsync(() =>
            {
                var status = Strings.ConnectionErrorSeeLogs;
                _viewModel.SetConnectionStatus(status);
                _debugSettingsPageViewModel.SetTransportStatus(status);
            });
        }
    }

    private void DisplayLyrics()
    {
        SynchronizeActiveLrcWatcher();
        _currentLyricsFile.RefreshAvailability();
        RefreshLocalPosition();
        UpdateDebugDiagnostics();
    }

    private void UpdateDebugDiagnostics() => _debugSettingsPageViewModel.UpdateRuntimeState(
        _playbackState.Current,
        _playbackState.CurrentLyrics,
        _playbackState.CurrentRawLyricsSnapshot,
        _playbackState.ActiveLocalLyricsRecord,
        _playbackState.EffectiveMetadata,
        _playbackState.LyricsLoadedFrom,
        _playbackState.LocalAssociationStatus);

    private void RefreshLocalPosition()
    {
        _viewModel.SetEffectiveMetadata(_playbackState.EffectiveMetadata);
        var playback = _playbackState.Current;
        PlaybackTrackIdentity? trackIdentity = playback is null
            ? null
            : new PlaybackTrackIdentity(playback.Envelope.Source, playback.Payload.Track.SourceTrackId);
        var trackChanged = _autoFollowTrackIdentity != trackIdentity;
        if (trackChanged)
        {
            _autoFollowTrackIdentity = trackIdentity;
            ResetAutoFollowSuspension();
        }

        var timeline = _playbackState.HasClockState
            ? _playbackState.GetTimelinePosition()
            : LyricsDisplayer.Core.Timeline.LyricsTimeline.Empty.Evaluate(0);
        var lyrics = _playbackState.PresentationLyrics;
        var timelineLines = _playbackState.GetTimelineOrderedLines();
        var localFileMissing = _playbackState.IsCurrentLocalLrcMissing &&
                               !_playbackState.IsEditorPreviewActive;
        _viewModel.UpdateLyricsPresentation(lyrics, timeline, timelineLines,
            localFileMissing);
        _overlay.Update(_playbackState.HasClockState ? lyrics : null, timeline, localFileMissing, timelineLines);
        if (trackChanged) QueueCurrentLineAutoCenter();
    }

    private void OnLocalMetadataChanged() => Dispatcher.InvokeAsync(() =>
    {
        _viewModel.SetEffectiveMetadata(_playbackState.EffectiveMetadata);
        UpdateDebugDiagnostics();
    });

    private void OnOverlayVisibilityChanged(bool visible)
    {
        _tray?.SetOverlayVisible(visible);
        if (visible)
        {
            SynchronizeActiveLrcWatcher(retryUnavailable: true);
            _activeLrcWatcher?.CheckNow();
        }
    }

    private void OnShowDesktopLyrics(object sender, RoutedEventArgs e) => _overlay.Show();

    private void OnCurrentLineChanged(MainLyricsLineViewModel? line)
    {
        if (_autoFollowPolicy.ShouldCenterCurrentLine(line is not null) && line is not null)
            QueueAutoCenter(line);
    }

    private void OpenLyricsWindow()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
        ResetAutoFollowSuspension();
        QueueCurrentLineAutoCenter();
    }

    private void OnLyricsPreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        SuspendAutoFollowForManualScroll();

    private void OnLyricsPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End or Key.Space)
            SuspendAutoFollowForManualScroll();
    }

    private void SuspendAutoFollowForManualScroll()
    {
        if (_shutdown.IsCancellationRequested || !IsLoaded || !IsVisible ||
            !_autoFollowPolicy.NotifyManualScroll(DateTimeOffset.UtcNow, _isProgrammaticScroll)) return;

        CancelPendingAutoCenter();
        _manualScrollResumeTimer.Stop();
        _manualScrollResumeTimer.Interval = MainLyricsAutoFollowPolicy.ManualScrollResumeDelay;
        _manualScrollResumeTimer.Start();
    }

    private void OnManualScrollResumeTimerTick(object? sender, EventArgs e)
    {
        _manualScrollResumeTimer.Stop();
        var now = DateTimeOffset.UtcNow;
        if (!_autoFollowPolicy.TryResume(now))
        {
            if (_autoFollowPolicy.GetRemainingResumeDelay(now) is { } remaining && remaining > TimeSpan.Zero)
            {
                _manualScrollResumeTimer.Interval = remaining;
                _manualScrollResumeTimer.Start();
            }
            return;
        }

        // Read CurrentLine now, not the line that happened to be current when scrolling began.
        QueueCurrentLineAutoCenter();
    }

    private void OnMainWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_autoFollowPolicy.ShouldCenterCurrentLine(_viewModel.CurrentLine is not null))
            QueueCurrentLineAutoCenter();
    }

    private void QueueCurrentLineAutoCenter()
    {
        if (_viewModel.CurrentLine is { } currentLine) QueueAutoCenter(currentLine);
    }

    private void QueueAutoCenter(MainLyricsLineViewModel line)
    {
        if (_shutdown.IsCancellationRequested || !IsLoaded || !IsVisible ||
            !_autoFollowPolicy.ShouldCenterCurrentLine(hasCurrentLine: true)) return;

        CancelPendingAutoCenter();
        if (LyricsListBox.ItemContainerGenerator.ContainerFromItem(line) is null)
        {
            // ScrollIntoView is used only to realize a virtualized container. The final position
            // is explicitly centered after WPF has completed this layout pass.
            _isProgrammaticScroll = true;
            try { LyricsListBox.ScrollIntoView(line); }
            finally { _isProgrammaticScroll = false; }
        }

        _pendingAutoCenterOperation = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            _pendingAutoCenterOperation = null;
            CenterCurrentLine(line);
        }));
    }

    private void CenterCurrentLine(MainLyricsLineViewModel line)
    {
        if (_shutdown.IsCancellationRequested || !IsLoaded || !IsVisible ||
            !_autoFollowPolicy.ShouldCenterCurrentLine(hasCurrentLine: true) ||
            !ReferenceEquals(_viewModel.CurrentLine, line)) return;

        var container = LyricsListBox.ItemContainerGenerator.ContainerFromItem(line) as FrameworkElement;
        var scrollViewer = _lyricsScrollViewer ??= FindVisualChild<ScrollViewer>(LyricsListBox);
        if (container is null || scrollViewer is null || container.ActualHeight <= 0 ||
            scrollViewer.ViewportHeight <= 0) return;

        var centerY = container.TransformToAncestor(scrollViewer)
            .Transform(new System.Windows.Point(0, container.ActualHeight / 2)).Y;
        var desiredOffset = scrollViewer.VerticalOffset + centerY - scrollViewer.ViewportHeight / 2;
        desiredOffset = Math.Clamp(desiredOffset, 0, scrollViewer.ScrollableHeight);

        _isProgrammaticScroll = true;
        try { scrollViewer.ScrollToVerticalOffset(desiredOffset); }
        finally { _isProgrammaticScroll = false; }
    }

    private void ResetAutoFollowForHiddenWindow()
    {
        ResetAutoFollowSuspension();
    }

    private void ResetAutoFollowSuspension()
    {
        _manualScrollResumeTimer.Stop();
        _autoFollowPolicy.Reset();
        CancelPendingAutoCenter();
    }

    private void StopManualScrollResumeTimer()
    {
        _manualScrollResumeTimer.Stop();
        _manualScrollResumeTimer.Tick -= OnManualScrollResumeTimerTick;
    }

    private void CancelPendingAutoCenter()
    {
        _pendingAutoCenterOperation?.Abort();
        _pendingAutoCenterOperation = null;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) return match;
            if (FindVisualChild<T>(child) is { } descendant) return descendant;
        }

        return null;
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        SynchronizeActiveLrcWatcher(retryUnavailable: true);
        _activeLrcWatcher?.CheckNow();
    }

    private void SynchronizeActiveLrcWatcher(bool retryUnavailable = false)
    {
        var record = _playbackState.ActiveLocalLyricsRecord;
        if (record is null)
        {
            if (_watchedLocalTrackId is not null) StopWatchingActiveLrc();
            _currentLyricsFile.SetStatus(Strings.LrcNoCurrentLocalFile);
            return;
        }

        string path;
        try { path = _library.ResolveLyricsPath(record); }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            if (_watchedLocalTrackId is not null) StopWatchingActiveLrc();
            _currentLyricsFile.SetStatus(Strings.LrcPathUnavailable);
            _logger.LogWarning(exception, "Could not resolve active LRC path.");
            return;
        }

        if (_watchedLocalTrackId == record.LocalTrackId &&
            string.Equals(_watchedLrcPath, path, StringComparison.OrdinalIgnoreCase) &&
            (_activeLrcWatcher is not null || !retryUnavailable)) return;

        StopWatchingActiveLrc();
        _watchedLocalTrackId = record.LocalTrackId;
        _watchedLrcPath = path;
        _currentLyricsFile.SetStatus(Strings.LrcWatchingCurrent);
        var generation = _externalLrcGeneration;
        try
        {
            _activeLrcWatcher = _lrcWatcherFactory.Create(path,
                _playbackState.CurrentLocalLyrics?.LrcContentHash,
                observation => OnActiveLrcObservation(record.LocalTrackId, generation, observation));
            _activeLrcWatcher.CheckNow();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or
                                            NotSupportedException or System.Security.SecurityException)
        {
            _currentLyricsFile.SetStatus(Strings.LrcWatcherUnavailable);
            _logger.LogWarning(exception, "Could not watch active LRC.");
        }
    }

    private void StopWatchingActiveLrc()
    {
        _externalLrcGeneration++;
        _activeLrcWatcher?.Dispose();
        _activeLrcWatcher = null;
        _watchedLocalTrackId = null;
        _watchedLrcPath = null;
    }

    private void OnActiveLrcObservation(
        string localTrackId, int generation, ActiveLrcFileObservation observation)
    {
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (_shutdown.IsCancellationRequested || generation != _externalLrcGeneration ||
                _playbackState.ActiveLocalLyricsRecord?.LocalTrackId != localTrackId) return;

            switch (observation.Kind)
            {
                case ActiveLrcFileObservationKind.Content:
                    {
                        var result = _playbackState.ReloadExternalLocalLyrics(localTrackId, observation.Fingerprint!);
                        switch (result)
                        {
                            case ExternalLocalLyricsUpdate.Reloaded:
                                _currentLyricsFile.SetStatus(Strings.LrcExternalReloaded);
                                DisplayLyrics();
                                return;
                            case ExternalLocalLyricsUpdate.Unchanged:
                                if (_playbackState.IsCurrentLocalLrcUsable)
                                    _currentLyricsFile.SetStatus(Strings.LrcWatchingCurrent);
                                _currentLyricsFile.RefreshAvailability();
                                return;
                            case ExternalLocalLyricsUpdate.Invalid:
                                _currentLyricsFile.SetStatus(Strings.LrcExternalRejectedRetained);
                                DisplayLyrics();
                                return;
                            case ExternalLocalLyricsUpdate.Unavailable:
                                _currentLyricsFile.SetStatus(Strings.LrcExternalUnavailableRetained);
                                DisplayLyrics();
                                return;
                            case ExternalLocalLyricsUpdate.Retry:
                                _currentLyricsFile.SetStatus(Strings.LrcCheckingLatestChange);
                                _activeLrcWatcher?.CheckNow();
                                _currentLyricsFile.RefreshAvailability();
                                return;
                            case ExternalLocalLyricsUpdate.Stale:
                                return;
                        }
                        break;
                    }
                case ActiveLrcFileObservationKind.Missing:
                    _playbackState.MarkExternalLocalLyricsMissing(localTrackId);
                    _currentLyricsFile.SetStatus(Strings.LrcUnavailableReloadIfRestored);
                    DisplayLyrics();
                    return;
                case ActiveLrcFileObservationKind.Unavailable:
                    _playbackState.MarkExternalLocalLyricsUnavailable(localTrackId, observation.Error);
                    _currentLyricsFile.SetStatus(Strings.LrcCannotReadRetained);
                    DisplayLyrics();
                    return;
            }
        }));
    }

    private void ExitApplication()
    {
        _allowApplicationExit = true;
        Close();
    }

    private bool ToggleOverlayFromTray()
    {
        _overlay.ToggleVisibility();
        return _overlay.IsVisible;
    }
}
