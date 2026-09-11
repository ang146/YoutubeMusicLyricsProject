using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using LyricsDisplayer.Core.Playback;
using LyricsDisplayer.Core.Protocol;

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
        ShowOverlayCheckBox.Checked += OnShowOverlayChecked;
        ShowOverlayCheckBox.Unchecked += OnShowOverlayUnchecked;
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
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _serverTask = RunServerSafelyAsync();
        _positionRefreshTimer.Start();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _positionRefreshTimer.Stop();
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
            _overlay.Update(LyricsDisplayer.Core.Timeline.LyricsTimeline.Empty.Evaluate(0));
            return;
        }

        var positionMs = _playbackState.GetLocalPositionMs();
        LocalPositionText.Text = $"{positionMs} ms ({FormatMilliseconds(positionMs)})";
        var timeline = _playbackState.GetTimelinePosition();
        _overlay.Update(timeline);
        CurrentLyricIndexText.Text = timeline.CurrentIndex?.ToString() ?? "None";
        CurrentLyricStartText.Text = timeline.CurrentLine is null ? "-" : $"{timeline.CurrentLine.StartMs} ms";
        CurrentLyricText.Text = timeline.CurrentLine?.Text ?? "-";
        NextLyricIndexText.Text = timeline.NextIndex?.ToString() ?? "None";
        NextLyricStartText.Text = timeline.NextLine is null ? "-" : $"{timeline.NextLine.StartMs} ms";
        NextLyricText.Text = timeline.NextLine?.Text ?? "-";
    }

    private static string FormatMilliseconds(long milliseconds) =>
        TimeSpan.FromMilliseconds(milliseconds).ToString(@"mm\:ss\.fff");

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
}
