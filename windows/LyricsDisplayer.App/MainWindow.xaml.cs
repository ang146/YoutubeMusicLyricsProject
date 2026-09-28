using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using LyricsDisplayer.Core.Playback;
using LyricsDisplayer.Core.Protocol;
using LyricsDisplayer.Core.Settings;

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
    private string? _currentLineTimingStatusTrackId;

    public MainWindow()
    {
        InitializeComponent();
        var app = (App)Application.Current;
        var logger = app.Logger;
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
        ShowOverlayCheckBox.Checked += OnShowOverlayChecked;
        ShowOverlayCheckBox.Unchecked += OnShowOverlayUnchecked;
        OverlayLockedCheckBox.Checked += OnOverlayLockedChanged;
        OverlayLockedCheckBox.Unchecked += OnOverlayLockedChanged;
        OverlayClickThroughCheckBox.Checked += OnOverlayClickThroughChanged;
        OverlayClickThroughCheckBox.Unchecked += OnOverlayClickThroughChanged;
        OverlayTopmostCheckBox.Checked += OnOverlayTopmostChanged;
        OverlayTopmostCheckBox.Unchecked += OnOverlayTopmostChanged;
        OverlayDisplayModeComboBox.SelectionChanged += OnOverlayDisplayModeChanged;
        OverlayWidthApplyButton.Click += OnOverlayWidthApply;
        TimingMinus500Button.Click += (_, _) => AdjustTiming(-500);
        TimingMinus100Button.Click += (_, _) => AdjustTiming(-100);
        TimingResetButton.Click += (_, _) => ResetTiming();
        TimingPlus100Button.Click += (_, _) => AdjustTiming(100);
        TimingPlus500Button.Click += (_, _) => AdjustTiming(500);
        TimingBakeButton.Click += (_, _) => BakeTiming();
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
        Closing += OnClosing;
        DisplayLibrary();
        UpdateTimingControls();
        SynchronizeOverlayPreferences(_overlay.Interaction);
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
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _positionRefreshTimer.Stop();
        _globalHotkeys?.Dispose();
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
            OverlayDisplayModeComboBox.SelectedIndex = state.DisplayMode == OverlayDisplayMode.OneLine ? 0 : 1;
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

    private void OnOverlayDisplayModeChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_synchronizingOverlayPreferences && OverlayDisplayModeComboBox.SelectedIndex >= 0)
            _overlay.SetDisplayMode(OverlayDisplayModeComboBox.SelectedIndex == 0
                ? OverlayDisplayMode.OneLine
                : OverlayDisplayMode.TwoLines);
    }

    private void OnOverlayWidthApply(object sender, RoutedEventArgs e)
    {
        if (_synchronizingOverlayPreferences) return;
        if (!double.TryParse(OverlayWidthTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture,
                out var width) || width < OverlayPreferences.MinimumWidth ||
            width > OverlayPreferences.MaximumWidth)
        {
            OverlayPreferenceStatusText.Text =
                $"Width must be between {OverlayPreferences.MinimumWidth:0} and {OverlayPreferences.MaximumWidth:0}.";
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
}
