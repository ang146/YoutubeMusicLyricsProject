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
    private Task? _serverTask;

    public MainWindow()
    {
        InitializeComponent();
        var logger = ((App)Application.Current).Logger;
        _playbackState = new PlaybackStateCoordinator(new SnapshotStateTracker(), new PlaybackClock());
        _server = new NamedPipeServer(logger, _playbackState);
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
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _serverTask = RunServerSafelyAsync();
        _positionRefreshTimer.Start();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _positionRefreshTimer.Stop();
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
    }

    private void RefreshLocalPosition()
    {
        if (!_playbackState.HasClockState)
        {
            return;
        }

        var positionMs = _playbackState.GetLocalPositionMs();
        LocalPositionText.Text = $"{positionMs} ms ({FormatMilliseconds(positionMs)})";
    }

    private static string FormatMilliseconds(long milliseconds) =>
        TimeSpan.FromMilliseconds(milliseconds).ToString(@"mm\:ss\.fff");
}
