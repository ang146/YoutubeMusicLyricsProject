using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using LyricsDisplayer.Core.Playback;
using LyricsDisplayer.Core.Protocol;
using LyricsDisplayer.Core.Settings;
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
    private Task? _serverTask;
    private bool _synchronizingOverlayToggle;
    private bool _synchronizingOverlayPreferences;
    private GlobalHotkeyService? _globalHotkeys;
    private TrayLifecycleService? _tray;
    private ActiveLrcFileWatcher? _activeLrcWatcher;
    private readonly ExternalLrcOpener _externalLrcOpener;
    private string? _watchedLocalTrackId;
    private string? _watchedLrcPath;
    private string? _externalLrcStatus;
    private int _externalLrcGeneration;
    private bool _allowApplicationExit;
    private string? _currentLineTimingStatusTrackId;

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
        _overlay.InteractionStateChanged += OnOverlayInteractionStateChanged;
        _overlay.OpenControlPanelRequested += OpenControlPanel;
        _overlay.OpenExternalLyricsRequested += OpenCurrentLrcExternally;
        _overlay.TimingCommandRequested += OnOverlayTimingCommand;
        ShowOverlayCheckBox.Checked += OnShowOverlayChecked;
        ShowOverlayCheckBox.Unchecked += OnShowOverlayUnchecked;
        OverlayLockedCheckBox.Checked += OnOverlayLockedChanged;
        OverlayLockedCheckBox.Unchecked += OnOverlayLockedChanged;
        OverlayClickThroughCheckBox.Checked += OnOverlayClickThroughChanged;
        OverlayClickThroughCheckBox.Unchecked += OnOverlayClickThroughChanged;
        OverlayTopmostCheckBox.Checked += OnOverlayTopmostChanged;
        OverlayTopmostCheckBox.Unchecked += OnOverlayTopmostChanged;
        OverlayContentModeComboBox.SelectionChanged += OnOverlayContentModeChanged;
        OverlayWidthApplyButton.Click += OnOverlayWidthApply;
        CloseControlPanelToTrayCheckBox.Checked += OnCloseToTrayChanged;
        CloseControlPanelToTrayCheckBox.Unchecked += OnCloseToTrayChanged;
        TimingMinus500Button.Click += (_, _) => AdjustTiming(-500);
        TimingMinus100Button.Click += (_, _) => AdjustTiming(-100);
        TimingResetButton.Click += (_, _) => ResetTiming();
        TimingPlus100Button.Click += (_, _) => AdjustTiming(100);
        TimingPlus500Button.Click += (_, _) => AdjustTiming(500);
        TimingBakeButton.Click += (_, _) => BakeTiming();
        OpenLrcExternallyButton.Click += (_, _) => OpenCurrentLrcExternally();
        CurrentLineMinus500Button.Click += (_, _) => AdjustCurrentLineTiming(-500);
        CurrentLineMinus100Button.Click += (_, _) => AdjustCurrentLineTiming(-100);
        CurrentLinePlus100Button.Click += (_, _) => AdjustCurrentLineTiming(100);
        CurrentLinePlus500Button.Click += (_, _) => AdjustCurrentLineTiming(500);
        _server.ConnectionStatusChanged += status => Dispatcher.InvokeAsync(() => PipeStatusText.Text = status);
        _server.SnapshotAccepted += snapshot => Dispatcher.InvokeAsync(() => DisplaySnapshot(snapshot));
        _server.LyricsChanged += () => Dispatcher.InvokeAsync(DisplayLyrics);
        _positionRefreshTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _positionRefreshTimer.Tick += (_, _) => RefreshLocalPosition();
        Loaded += OnLoaded;
        Activated += OnActivated;
        Closing += OnClosing;
        DisplayLibrary();
        UpdateTimingControls();
        SynchronizeOverlayPreferences(_overlay.Interaction);
        _synchronizingOverlayPreferences = true;
        CloseControlPanelToTrayCheckBox.IsChecked = app.SettingsStore.LoadCloseControlPanelToTray();
        _synchronizingOverlayPreferences = false;
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
            HotkeyStatusText.Text = "One or more global shortcuts are already in use and could not be registered.";
        _tray = new TrayLifecycleService(new WindowsTrayIcon(), OpenControlPanel,
            ToggleOverlayFromTray, ExitApplication);
        _tray.Start(_overlay.IsVisible);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (ControlPanelClosePolicy.ShouldHideToTray(
                CloseControlPanelToTrayCheckBox.IsChecked == true, _allowApplicationExit))
        {
            e.Cancel = true;
            Hide();
            ((App)Application.Current).Logger.Write("Information", "Application",
                "Control Panel hidden to the system tray.");
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
            await Dispatcher.InvokeAsync(() => PipeStatusText.Text = "Server error; see App logs");
        }
    }

    private void DisplaySnapshot(PlaybackSnapshotMessage snapshot)
    {
        var envelope = snapshot.Envelope;
        var payload = snapshot.Payload;
        SessionIdText.Text = envelope.SourceSessionId;
        SequenceText.Text = envelope.Sequence.ToString();
        LastMessageText.Text = envelope.SentAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff zzz");
        TrackIdText.Text = payload.Track.SourceTrackId;
        TitleText.Text = payload.Track.Title;
        ArtistText.Text = payload.Track.Artist;
        AlbumText.Text = string.IsNullOrWhiteSpace(payload.Track.Album) ? "(unavailable)" : payload.Track.Album;
        DurationText.Text = $"{payload.Track.DurationMs} ms ({FormatMilliseconds(payload.Track.DurationMs)})";
        SnapshotPositionText.Text =
            $"{payload.Playback.PositionMs} ms ({FormatMilliseconds(payload.Playback.PositionMs)})";
        PlayingText.Text = payload.Playback.Playing.ToString();
        PlaybackRateText.Text = payload.Playback.PlaybackRate.ToString("0.###");
        DisplayLyrics();
        RawJsonText.Text = snapshot.RawJson;
    }

    private void DisplayLyrics()
    {
        SynchronizeActiveLrcWatcher();
        UpdateExternalLrcAvailability();
        var lyrics = _playbackState.CurrentLyrics?.Payload;
        LyricsAvailableText.Text = lyrics?.Available.ToString() ?? "Pending / unknown";
        LyricsTimedText.Text = lyrics?.Timed.ToString() ?? "Pending / unknown";
        LyricsSourceText.Text = lyrics?.Source ?? "-";
        LyricsCountText.Text = (lyrics?.Lines.Count ?? 0).ToString();
        LyricsAttributionText.Text = lyrics?.Attribution ?? "-";

        var lines = new StringBuilder();
        foreach (var line in lyrics?.Lines ?? [])
        {
            lines.Append('[').Append(line.StartMs).Append(" - ").Append(line.EndMs).Append("] ")
                .AppendLine(line.Text);
        }

        LyricsLinesText.Text = lines.ToString();
        if (_playbackState.HasClockState) RefreshLocalPosition();
        DisplayLibrary();
        UpdateTimingControls();
        UpdateOverlayTimingAvailability();
    }

    private void DisplayLibrary()
    {
        var app = (App)Application.Current;
        var library = app.LyricsLibrary;
        var local = _playbackState.CurrentLocalLyrics;
        var current = _playbackState.Current;
        var source = local?.Sidecar.SourceAssociations.FirstOrDefault(association =>
                         association.Source == current?.Envelope.Source &&
                         association.SourceTrackId == current.Payload.Track.SourceTrackId)
                     ?? local?.Sidecar.SourceAssociations.FirstOrDefault();
        LibraryDiagnosticsText.Text = $"""
            Lyrics Library Path: {library.Paths.LibraryPath}
            SQLite Index Path: {library.Paths.IndexPath}
            SQLite Index Status: {library.IndexStatus}
            Local Track ID: {local?.Record.LocalTrackId ?? "-"}
            Local Association Status: {_playbackState.LocalAssociationStatus}
            Lyrics Loaded From: {_playbackState.LyricsLoadedFrom}
            Lyrics Provider: {local?.Sidecar.Lyrics.Source ?? _playbackState.CurrentLyrics?.Payload.Source ?? "-"}
            Attribution: {local?.Sidecar.Lyrics.Attribution ?? _playbackState.CurrentLyrics?.Payload.Attribution ?? "-"}
            Source Title: {source?.Metadata.Title ?? "-"}
            Source Artist: {source?.Metadata.Artist ?? "-"}
            User Title Override: {local?.Sidecar.UserMetadata.Title ?? "-"}
            User Artist Override: {local?.Sidecar.UserMetadata.Artist ?? "-"}
            Effective Title: {local?.EffectiveMetadata.Title ?? "-"}
            Effective Artist: {local?.EffectiveMetadata.Artist ?? "-"}
            """;
    }

    private void RefreshLocalPosition()
    {
        UpdateOverlayTimingAvailability();
        if (!_playbackState.HasClockState)
        {
            _overlay.Update(null, LyricsDisplayer.Core.Timeline.LyricsTimeline.Empty.Evaluate(0));
            UpdateCurrentLineTimingControls();
            return;
        }

        var positionMs = _playbackState.GetLocalPositionMs();
        LocalPositionText.Text = $"{positionMs} ms ({FormatMilliseconds(positionMs)})";
        var timeline = _playbackState.GetTimelinePosition();
        _overlay.Update(_playbackState.CurrentLyrics?.Payload, timeline);
        CurrentLyricIndexText.Text = timeline.CurrentIndex?.ToString() ?? "None";
        CurrentLyricStartText.Text = timeline.CurrentLine is null ? "-" : $"{timeline.CurrentLine.StartMs} ms";
        CurrentLyricText.Text = timeline.CurrentLine?.Text ?? "-";
        NextLyricIndexText.Text = timeline.NextIndex?.ToString() ?? "None";
        NextLyricStartText.Text = timeline.NextLine is null ? "-" : $"{timeline.NextLine.StartMs} ms";
        NextLyricText.Text = timeline.NextLine?.Text ?? "-";
        UpdateCurrentLineTimingControls();
    }

    private void AdjustCurrentLineTiming(long deltaMs)
    {
        var target = _playbackState.CaptureCurrentLineTimingTarget();
        if (target is null) return;
        var sourceTrackId = _playbackState.Current?.Payload.Track.SourceTrackId;
        var result = _playbackState.AdjustCurrentLineTiming(target, deltaMs);
        _currentLineTimingStatusTrackId = sourceTrackId;
        CurrentLineTimingStatusText.Text = result.Succeeded ? string.Empty :
            result.Error ?? "The current lyric timestamp could not be saved.";
        DisplayLyrics();
    }

    private void UpdateCurrentLineTimingControls()
    {
        var sourceTrackId = _playbackState.Current?.Payload.Track.SourceTrackId;
        if (_currentLineTimingStatusTrackId != sourceTrackId)
        {
            CurrentLineTimingStatusText.Text = string.Empty;
            _currentLineTimingStatusTrackId = sourceTrackId;
        }
        var enabled = _playbackState.CanAdjustCurrentLineTiming;
        CurrentLineMinus500Button.IsEnabled = enabled;
        CurrentLineMinus100Button.IsEnabled = enabled;
        CurrentLinePlus100Button.IsEnabled = enabled;
        CurrentLinePlus500Button.IsEnabled = enabled;
        var timeline = _playbackState.GetTimelinePosition();
        var line = timeline.CurrentLine;
        CurrentLineTimingText.Text = _playbackState.CurrentLocalLyrics is null || line is null
            ? "No current local lyric"
            : $"Line {timeline.CurrentIndex + 1} — {line.StartMs / 60_000:00}:{line.StartMs % 60_000 / 1000:00}.{line.StartMs % 1000:000} — {line.Text}";
    }

    private static string FormatMilliseconds(long milliseconds) =>
        TimeSpan.FromMilliseconds(milliseconds).ToString(@"mm\:ss\.fff");

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
        TimingStatusText.Text = result.Succeeded ? string.Empty : result.Error ?? "Timing adjustment could not be saved.";
        UpdateTimingControls();
        RefreshLocalPosition();
        DisplayLibrary();
    }

    private void BakeTiming()
    {
        var target = _playbackState.CaptureTimingAdjustmentTarget();
        if (target is null || target.GlobalOffsetMs == 0) return;
        var formatted = FormatOffset(target.GlobalOffsetMs);
        var choice = MessageBox.Show(this,
            $"Bake {formatted} timing adjustment into this LRC?\n\nThis will rewrite the LRC timestamps and reset the global offset to 0.",
            "Bake timing adjustment", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (choice != MessageBoxResult.OK) return;

        var result = _playbackState.BakeTiming(target);
        if (!result.Succeeded)
        {
            var message = result.Status == LyricsDisplayer.Core.Library.TimingAdjustmentStatus.NegativeTimestamp
                ? "Bake was cancelled because at least one adjusted timestamp would be negative. Reduce the offset and try again."
                : result.Error ?? "The timing adjustment could not be baked into the LRC.";
            MessageBox.Show(this, message, "Bake timing adjustment", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        ShowTimingResult(result);
    }

    private void UpdateTimingControls()
    {
        var enabled = _playbackState.CanAdjustTiming;
        TimingMinus500Button.IsEnabled = enabled;
        TimingMinus100Button.IsEnabled = enabled;
        TimingResetButton.IsEnabled = enabled;
        TimingPlus100Button.IsEnabled = enabled;
        TimingPlus500Button.IsEnabled = enabled;
        TimingBakeButton.IsEnabled = enabled && _playbackState.GlobalOffsetMs != 0;
        TimingOffsetText.Text = FormatOffset(_playbackState.GlobalOffsetMs);
        if (!enabled) TimingStatusText.Text = string.Empty;
        UpdateCurrentLineTimingControls();
    }

    private static string FormatOffset(long milliseconds) =>
        ((decimal)milliseconds / 1000m).ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture) + "s";

    private void OnShowOverlayChecked(object sender, RoutedEventArgs e)
    {
        if (!_synchronizingOverlayToggle) _overlay.Show();
    }

    private void OnShowOverlayUnchecked(object sender, RoutedEventArgs e)
    {
        if (!_synchronizingOverlayToggle) _overlay.Hide();
    }

    private void OnOverlayVisibilityChanged(bool visible)
    {
        _tray?.SetOverlayVisible(visible);
        if (visible)
        {
            SynchronizeActiveLrcWatcher(retryUnavailable: true);
            _activeLrcWatcher?.CheckNow();
        }
        _synchronizingOverlayToggle = true;
        try
        {
            ShowOverlayCheckBox.IsChecked = visible;
        }
        finally
        {
            _synchronizingOverlayToggle = false;
        }
    }

    private void OnOverlayInteractionStateChanged(OverlayInteractionState state) =>
        SynchronizeOverlayPreferences(state);

    private void SynchronizeOverlayPreferences(OverlayInteractionState state)
    {
        _synchronizingOverlayPreferences = true;
        try
        {
            OverlayLockedCheckBox.IsChecked = state.Locked;
            OverlayClickThroughCheckBox.IsChecked = state.ClickThrough;
            OverlayTopmostCheckBox.IsChecked = state.Topmost;
            OverlayContentModeComboBox.SelectedIndex = (int)state.ContentMode;
            OverlayWidthTextBox.Text = state.Width.ToString("0", CultureInfo.InvariantCulture);
            OverlayPreferenceStatusText.Text = string.Empty;
        }
        finally
        {
            _synchronizingOverlayPreferences = false;
        }
    }

    private void OnOverlayLockedChanged(object sender, RoutedEventArgs e)
    {
        if (!_synchronizingOverlayPreferences)
            _overlay.SetLocked(OverlayLockedCheckBox.IsChecked == true);
    }

    private void OnOverlayClickThroughChanged(object sender, RoutedEventArgs e)
    {
        if (!_synchronizingOverlayPreferences)
            _overlay.SetClickThrough(OverlayClickThroughCheckBox.IsChecked == true);
    }

    private void OnOverlayTopmostChanged(object sender, RoutedEventArgs e)
    {
        if (!_synchronizingOverlayPreferences)
            _overlay.SetTopmost(OverlayTopmostCheckBox.IsChecked == true);
    }

    private void OnOverlayContentModeChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_synchronizingOverlayPreferences && OverlayContentModeComboBox.SelectedIndex >= 0)
            _overlay.SetContentMode((LyricsContentMode)OverlayContentModeComboBox.SelectedIndex);
    }

    private void OnOverlayWidthApply(object sender, RoutedEventArgs e)
    {
        if (_synchronizingOverlayPreferences) return;
        if (!double.TryParse(OverlayWidthTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture,
                out var width) || !double.IsFinite(width) || width < OverlayPreferences.MinimumWidth)
        {
            OverlayPreferenceStatusText.Text = $"Width must be at least {OverlayPreferences.MinimumWidth:0}.";
            return;
        }
        OverlayPreferenceStatusText.Text = string.Empty;
        _overlay.SetWidth(width);
    }

    private void OpenControlPanel()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
    }

    private void OnCloseToTrayChanged(object sender, RoutedEventArgs e)
    {
        if (_synchronizingOverlayPreferences) return;
        var store = ((App)Application.Current).SettingsStore;
        try
        {
            store.SaveCloseControlPanelToTray(CloseControlPanelToTrayCheckBox.IsChecked == true);
            ApplicationPreferenceStatusText.Text = string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ApplicationPreferenceStatusText.Text =
                $"The close-to-tray preference could not be saved ({exception.GetType().Name}).";
            ((App)Application.Current).Logger.Write("Warning", "Settings",
                $"Close-to-tray preference could not be saved ({exception.GetType().Name}).");
            _synchronizingOverlayPreferences = true;
            try { CloseControlPanelToTrayCheckBox.IsChecked = store.LoadCloseControlPanelToTray(); }
            finally { _synchronizingOverlayPreferences = false; }
        }
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
            _externalLrcStatus = "No current local timed LRC";
            return;
        }

        string path;
        try { path = ((App)Application.Current).LyricsLibrary.ResolveLyricsPath(record); }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            if (_watchedLocalTrackId is not null) StopWatchingActiveLrc();
            _externalLrcStatus = "The current local LRC path is unavailable.";
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
        _externalLrcStatus = "Watching current LRC";
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
            _externalLrcStatus = "File watching is unavailable; the file will be checked when the app is activated.";
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
                            _externalLrcStatus = "External LRC reloaded";
                            DisplayLyrics();
                            return;
                        case ExternalLocalLyricsUpdate.Unchanged:
                            if (_playbackState.IsCurrentLocalLrcUsable) _externalLrcStatus = "Watching current LRC";
                            UpdateExternalLrcAvailability();
                            return;
                        case ExternalLocalLyricsUpdate.Invalid:
                            _externalLrcStatus = "External LRC was rejected; last valid lyrics are retained.";
                            DisplayLyrics();
                            return;
                        case ExternalLocalLyricsUpdate.Unavailable:
                            _externalLrcStatus = "External LRC is temporarily unavailable; last valid lyrics are retained.";
                            DisplayLyrics();
                            return;
                        case ExternalLocalLyricsUpdate.Retry:
                            _externalLrcStatus = "Checking the latest LRC change…";
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
                    _externalLrcStatus = "LRC file unavailable; it will be reloaded if restored.";
                    DisplayLyrics();
                    return;
                case ActiveLrcFileObservationKind.Unavailable:
                    _playbackState.MarkExternalLocalLyricsUnavailable(localTrackId, observation.Error);
                    _externalLrcStatus = "LRC cannot currently be read; last valid lyrics are retained.";
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
        OpenLrcExternallyButton.IsEnabled = canOpen;
        _overlay.SetExternalLyricsAvailability(canOpen);
        ExternalLrcStatusText.Text = _externalLrcStatus ??
            (_playbackState.ActiveLocalLyricsRecord is null ? "No current local timed LRC" : "Watching current LRC");
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
            _externalLrcStatus = "The current local LRC path is unavailable.";
            ((App)Application.Current).Logger.Write("Warning", "ExternalLyrics",
                $"Could not resolve active LRC path ({exception.GetType().Name}).");
            UpdateExternalLrcAvailability();
            return;
        }

        if (!_externalLrcOpener.TryOpen(path, out var error))
        {
            _externalLrcStatus = error ?? "The current LRC could not be opened.";
            UpdateExternalLrcAvailability();
            return;
        }

        _externalLrcStatus = "Opened the current LRC externally";
        UpdateExternalLrcAvailability();
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
