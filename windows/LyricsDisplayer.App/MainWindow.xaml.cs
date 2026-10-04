using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Core.Playback;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace LyricsDisplayer;

public partial class MainWindow : Window
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly PlaybackStateCoordinator _playbackState;
    private readonly NamedPipeServer _server;
    private readonly DispatcherTimer _positionRefreshTimer;
    private readonly LyricsOverlayController _overlay;
    private readonly MainLyricsWindowViewModel _viewModel;
    private Task? _serverTask;
    private GlobalHotkeyService? _globalHotkeys;
    private TrayLifecycleService? _tray;
    private ActiveLrcFileWatcher? _activeLrcWatcher;
    private BuiltInLyricsEditorWindow? _editorWindow;
    private EditorCommand _openBuiltInEditorCommand = null!;
    private readonly ExternalLrcOpener _externalLrcOpener;
    private string? _watchedLocalTrackId;
    private string? _watchedLrcPath;
    private int _externalLrcGeneration;
    private bool _allowApplicationExit;
    private bool _applicationExitPending;
    private readonly bool _hideToTrayOnClose;

    public MainWindow()
    {
        InitializeComponent();
        var app = (App)Application.Current;
        var logger = app.Logger;
        _externalLrcOpener = new ExternalLrcOpener(log: logger.Write);
        _playbackState = new PlaybackStateCoordinator(new SnapshotStateTracker(), new PlaybackClock(),
            app.LyricsLibrary, logger.Write);
        _server = new NamedPipeServer(logger, _playbackState);
        _overlay = new LyricsOverlayController(
            () => new LyricsOverlayWindow(),
            app.SettingsStore,
            DesktopWorkAreaProvider.GetVisibleWorkAreas,
            logger.Write);
        _overlay.VisibilityChanged += OnOverlayVisibilityChanged;
        _overlay.OpenControlPanelRequested += OpenLyricsWindow;
        _overlay.OpenExternalLyricsRequested += OpenCurrentLrcExternally;
        _overlay.OpenBuiltInEditorRequested += ExecuteOpenBuiltInEditor;
        _overlay.TimingCommandRequested += OnOverlayTimingCommand;
        _openBuiltInEditorCommand = new("application.open-built-in-editor", _ => OpenBuiltInEditor(),
            _ => CanOpenBuiltInEditor(), EditorHotkeyScope.Application);
        _viewModel = new MainLyricsWindowViewModel(_openBuiltInEditorCommand);
        _viewModel.CurrentLineChanged += OnCurrentLineChanged;
        DataContext = _viewModel;
        _openBuiltInEditorCommand.CanExecuteChanged += (_, _) =>
            _overlay.SetBuiltInEditorAvailability(_openBuiltInEditorCommand.CanExecute(null));
        _server.ConnectionStatusChanged += status => Dispatcher.InvokeAsync(() => _viewModel.SetConnectionStatus(status));
        _server.SnapshotAccepted += _ => Dispatcher.InvokeAsync(DisplayLyrics);
        _server.LyricsChanged += () => Dispatcher.InvokeAsync(DisplayLyrics);
        _positionRefreshTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _positionRefreshTimer.Tick += (_, _) => RefreshLocalPosition();
        Loaded += OnLoaded;
        Activated += OnActivated;
        Closing += OnClosing;
        _hideToTrayOnClose = app.SettingsStore.LoadCloseControlPanelToTray();
        DisplayLyrics();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_globalHotkeys is not null) return;
        _serverTask = RunServerSafelyAsync();
        _positionRefreshTimer.Start();
        var logger = ((App)Application.Current).Logger;
        _globalHotkeys = new GlobalHotkeyService(
            new Win32GlobalHotkeyPlatform(new WindowInteropHelper(this).Handle),
            _overlay.ToggleVisibility,
            () => _overlay.SetClickThrough(!_overlay.Interaction.ClickThrough),
            logger.Write);
        _globalHotkeys.Start();
        if (_globalHotkeys.RegisteredIds.Count != 2)
            logger.Write("Warning", "Hotkeys", "One or more global shortcuts are already in use and could not be registered.");
        _tray = new TrayLifecycleService(new WindowsTrayIcon(), OpenLyricsWindow,
            ToggleOverlayFromTray, ExitApplication);
        _tray.Start(_overlay.IsVisible);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        var app = (App)Application.Current;
        var hideToTray = !app.Lifetime.IsFatalShutdown && ControlPanelClosePolicy.ShouldHideToTray(
            _hideToTrayOnClose, _allowApplicationExit);
        _applicationExitPending = app.Lifetime.IsFatalShutdown || !hideToTray;
        if (_editorWindow is { } editor && !editor.RequestCloseFromApplication())
        {
            e.Cancel = true;
            _applicationExitPending = false;
            _allowApplicationExit = false;
            return;
        }
        if (hideToTray)
        {
            e.Cancel = true;
            _applicationExitPending = false;
            Hide();
            app.Logger.Write("Information", "Application",
                "Main Lyrics Window hidden to the system tray.");
            return;
        }
        _positionRefreshTimer.Stop();
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
            ((App)Application.Current).Logger.Write("Error", "NamedPipe", $"Server stopped unexpectedly: {exception}");
            await Dispatcher.InvokeAsync(() => _viewModel.SetConnectionStatus("Connection error; see application logs"));
        }
    }

    private void DisplayLyrics()
    {
        SynchronizeActiveLrcWatcher();
        UpdateExternalLrcAvailability();
        _openBuiltInEditorCommand.Invalidate();
        RefreshLocalPosition();
        UpdateOverlayTimingAvailability();
    }

    private void RefreshLocalPosition()
    {
        UpdateOverlayTimingAvailability();
        var timeline = _playbackState.HasClockState
            ? _playbackState.GetTimelinePosition()
            : LyricsDisplayer.Core.Timeline.LyricsTimeline.Empty.Evaluate(0);
        var lyrics = _playbackState.CurrentLyrics?.Payload;
        var timelineLines = _playbackState.GetTimelineOrderedLines();
        _viewModel.UpdateLyricsPresentation(lyrics, timeline, timelineLines,
            _playbackState.IsCurrentLocalLrcMissing);
        _overlay.Update(_playbackState.HasClockState ? lyrics : null, timeline,
            _playbackState.IsCurrentLocalLrcMissing, timelineLines);
    }

    private void AdjustCurrentLineTiming(long deltaMs)
    {
        var target = _playbackState.CaptureCurrentLineTimingTarget();
        if (target is null) return;
        var result = _playbackState.AdjustCurrentLineTiming(target, deltaMs);
        if (!result.Succeeded)
            ((App)Application.Current).Logger.Write("Warning", "Timing",
                result.Error ?? "The current lyric timestamp could not be saved.");
        DisplayLyrics();
    }

    private void AdjustTiming(long deltaMs)
    {
        ShowTimingResult(_playbackState.AdjustTiming(deltaMs));
    }

    private void ResetTiming()
    {
        ShowTimingResult(_playbackState.ResetTiming());
    }

    private void ShowTimingResult(LyricsDisplayer.Core.Library.TimingAdjustmentResult result)
    {
        if (!result.Succeeded)
            ((App)Application.Current).Logger.Write("Warning", "Timing",
                result.Error ?? "Timing adjustment could not be saved.");
        DisplayLyrics();
    }

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

    private void OnOpenLrcExternally(object sender, RoutedEventArgs e) => OpenCurrentLrcExternally();

    private void OnCurrentLineChanged(MainLyricsLineViewModel? line)
    {
        if (line is null) return;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (_shutdown.IsCancellationRequested || !IsLoaded || !IsVisible ||
                !ReferenceEquals(_viewModel.CurrentLine, line)) return;
            LyricsListBox.ScrollIntoView(line);
        }));
    }

    private void OpenLyricsWindow()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
        if (_viewModel.CurrentLine is { } currentLine) OnCurrentLineChanged(currentLine);
    }

    private void OnOverlayTimingCommand(OverlayCommand command)
    {
        OverlayTimingCommandRouter.Route(command, AdjustCurrentLineTiming, AdjustTiming, ResetTiming);
        UpdateOverlayTimingAvailability();
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
            _viewModel.SetExternalLyricsStatus("No current local LRC");
            return;
        }

        string path;
        try { path = ((App)Application.Current).LyricsLibrary.ResolveLyricsPath(record); }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            if (_watchedLocalTrackId is not null) StopWatchingActiveLrc();
            _viewModel.SetExternalLyricsStatus("The current local LRC path is unavailable.");
            ((App)Application.Current).Logger.Write("Warning", "ExternalLyrics",
                $"Could not resolve active LRC path ({exception.GetType().Name}).");
            return;
        }

        if (_watchedLocalTrackId == record.LocalTrackId &&
            string.Equals(_watchedLrcPath, path, StringComparison.OrdinalIgnoreCase) &&
            (_activeLrcWatcher is not null || !retryUnavailable)) return;

        StopWatchingActiveLrc();
        _watchedLocalTrackId = record.LocalTrackId;
        _watchedLrcPath = path;
        _viewModel.SetExternalLyricsStatus("Watching current LRC");
        var generation = _externalLrcGeneration;
        try
        {
            _activeLrcWatcher = new ActiveLrcFileWatcher(path,
                _playbackState.CurrentLocalLyrics?.LrcContentHash,
                observation => OnActiveLrcObservation(record.LocalTrackId, generation, observation),
                ((App)Application.Current).Logger.Write);
            _activeLrcWatcher.CheckNow();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or
                                            NotSupportedException or System.Security.SecurityException)
        {
            _viewModel.SetExternalLyricsStatus("File watching is unavailable; the file will be checked when the app is activated.");
            ((App)Application.Current).Logger.Write("Warning", "ExternalLyrics",
                $"Could not watch active LRC ({exception.GetType().Name}).");
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
                                _viewModel.SetExternalLyricsStatus("External LRC reloaded");
                                DisplayLyrics();
                                return;
                            case ExternalLocalLyricsUpdate.Unchanged:
                                if (_playbackState.IsCurrentLocalLrcUsable)
                                    _viewModel.SetExternalLyricsStatus("Watching current LRC");
                                UpdateExternalLrcAvailability();
                                return;
                            case ExternalLocalLyricsUpdate.Invalid:
                                _viewModel.SetExternalLyricsStatus("External LRC was rejected; last valid lyrics are retained.");
                                DisplayLyrics();
                                return;
                            case ExternalLocalLyricsUpdate.Unavailable:
                                _viewModel.SetExternalLyricsStatus("External LRC is temporarily unavailable; last valid lyrics are retained.");
                                DisplayLyrics();
                                return;
                            case ExternalLocalLyricsUpdate.Retry:
                                _viewModel.SetExternalLyricsStatus("Checking the latest LRC change…");
                                _activeLrcWatcher?.CheckNow();
                                UpdateExternalLrcAvailability();
                                return;
                            case ExternalLocalLyricsUpdate.Stale:
                                return;
                        }
                        break;
                    }
                case ActiveLrcFileObservationKind.Missing:
                    _playbackState.MarkExternalLocalLyricsMissing(localTrackId);
                    _viewModel.SetExternalLyricsStatus("LRC file unavailable; it will be reloaded if restored.");
                    DisplayLyrics();
                    return;
                case ActiveLrcFileObservationKind.Unavailable:
                    _playbackState.MarkExternalLocalLyricsUnavailable(localTrackId, observation.Error);
                    _viewModel.SetExternalLyricsStatus("LRC cannot currently be read; last valid lyrics are retained.");
                    DisplayLyrics();
                    return;
            }
        }));
    }

    private void UpdateExternalLrcAvailability()
    {
        var canOpen = false;
        if (_playbackState.IsCurrentLocalLrcUsable && _playbackState.ActiveLocalLyricsRecord is { } record)
        {
            try
            {
                var path = ((App)Application.Current).LyricsLibrary.ResolveLyricsPath(record);
                canOpen = File.Exists(path);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
            {
                canOpen = false;
            }
        }
        _viewModel.SetCanOpenExternalLyrics(canOpen);
        _overlay.SetExternalLyricsAvailability(canOpen);
    }

    private void OpenCurrentLrcExternally()
    {
        if (!_playbackState.IsCurrentLocalLrcUsable || _playbackState.ActiveLocalLyricsRecord is not { } record)
        {
            UpdateExternalLrcAvailability();
            return;
        }

        string path;
        try { path = ((App)Application.Current).LyricsLibrary.ResolveLyricsPath(record); }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            _viewModel.SetExternalLyricsStatus("The current local LRC path is unavailable.");
            ((App)Application.Current).Logger.Write("Warning", "ExternalLyrics",
                $"Could not resolve active LRC path ({exception.GetType().Name}).");
            UpdateExternalLrcAvailability();
            return;
        }

        if (!_externalLrcOpener.TryOpen(path, out var error))
        {
            _viewModel.SetExternalLyricsStatus(error ?? "The current LRC could not be opened.");
            UpdateExternalLrcAvailability();
            return;
        }

        _viewModel.SetExternalLyricsStatus("Opened the current LRC externally");
        UpdateExternalLrcAvailability();
    }

    private bool CanOpenBuiltInEditor()
    {
        if (_editorWindow is not null || _playbackState.ActiveLocalLyricsRecord is not { } record) return false;
        return ((App)Application.Current).LyricsLibrary.LoadForEditing(record).Status ==
               LyricsDisplayer.Core.Library.EditorAssetStatus.Ready;
    }

    private void ExecuteOpenBuiltInEditor()
    {
        if (_openBuiltInEditorCommand.CanExecute(null)) _openBuiltInEditorCommand.Execute(null);
    }

    private void OpenBuiltInEditor()
    {
        if (_editorWindow is { IsVisible: true } existing)
        {
            existing.Activate();
            return;
        }
        if (_playbackState.ActiveLocalLyricsRecord is not { } record) return;
        var app = (App)Application.Current;
        var loaded = app.LyricsLibrary.LoadForEditing(record);
        if (loaded.Status != LyricsDisplayer.Core.Library.EditorAssetStatus.Ready || loaded.Asset is null)
        {
            MessageBox.Show(this, loaded.Error ?? "The current local LRC is not available for editing.",
                "Built-in Lyrics Editor", MessageBoxButton.OK, MessageBoxImage.Warning);
            _openBuiltInEditorCommand.Invalidate();
            return;
        }

        var viewModel = new BuiltInLyricsEditorViewModel(loaded.Asset, app.LyricsLibrary, _playbackState);
        var window = new BuiltInLyricsEditorWindow(viewModel);
        // Keep the main window hidden when opened from the overlay, but retain a stable owner
        // relationship so ShowDialog does not disable the independent desktop overlay window.
        window.Owner = this;
        _editorWindow = window;
        _openBuiltInEditorCommand.Invalidate();
        var topmostSuppressionStarted = false;
        try
        {
            _overlay.SetEditorModalTopmostSuppressed(true);
            topmostSuppressionStarted = true;
            window.ShowDialog();
        }
        finally
        {
            var shuttingDown = _applicationExitPending || app.Lifetime.IsFatalShutdown ||
                               app.Dispatcher.HasShutdownStarted || app.Dispatcher.HasShutdownFinished;
            if (topmostSuppressionStarted && !shuttingDown)
                _overlay.SetEditorModalTopmostSuppressed(false);
            _editorWindow = null;
            _openBuiltInEditorCommand.Invalidate();
            UpdateExternalLrcAvailability();
        }
    }

    private void UpdateOverlayTimingAvailability() => _overlay.SetTimingAvailability(
        _playbackState.CanAdjustCurrentLineTiming,
        _playbackState.CanAdjustTiming,
        _playbackState.GlobalOffsetMs);

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
