using System.ComponentModel;
using System.Text;
using System.Windows;
using LyricsDisplayer.Core.Protocol;

namespace LyricsDisplayer;

public partial class MainWindow : Window
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly NamedPipeServer _server;
    private Task? _serverTask;

    public MainWindow()
    {
        InitializeComponent();
        var logger = ((App)Application.Current).Logger;
        _server = new NamedPipeServer(logger, new SnapshotStateTracker());
        _server.ConnectionStatusChanged += status => Dispatcher.InvokeAsync(() => PipeStatusText.Text = status);
        _server.SnapshotAccepted += snapshot => Dispatcher.InvokeAsync(() => DisplaySnapshot(snapshot));
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _serverTask = RunServerSafelyAsync();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
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
        AlbumText.Text = payload.Track.Album;
        DurationText.Text = $"{payload.Track.DurationMs} ms ({FormatMilliseconds(payload.Track.DurationMs)})";
        PositionText.Text = $"{payload.Playback.PositionMs} ms ({FormatMilliseconds(payload.Playback.PositionMs)})";
        PlayingText.Text = payload.Playback.Playing.ToString();
        PlaybackRateText.Text = payload.Playback.PlaybackRate.ToString("0.###");
        LyricsAvailableText.Text = payload.Lyrics.Available.ToString();
        LyricsTimedText.Text = payload.Lyrics.Timed.ToString();

        var lines = new StringBuilder();
        foreach (var line in payload.Lyrics.Lines)
        {
            lines.Append('[').Append(line.StartMs).Append(" - ").Append(line.EndMs).Append("] ")
                .AppendLine(line.Text);
        }

        LyricsLinesText.Text = lines.ToString();
        RawJsonText.Text = snapshot.RawJson;
    }

    private static string FormatMilliseconds(long milliseconds) =>
        TimeSpan.FromMilliseconds(milliseconds).ToString(@"mm\:ss\.fff");
}
