using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Core.Protocol;

namespace LyricsDisplayer;

public sealed class DebugSettingsPageViewModel : ViewModelBase, ISettingsPageViewModel
{
    private const string Unavailable = "Not available";
    private readonly string _libraryPath;
    private readonly string _indexPath;
    private readonly string _logsDirectory;
    private readonly string _crashReportsDirectory;
    private readonly Action<string> _openFolder;
    private PlaybackSnapshotMessage? _playbackSnapshot;
    private LyricsSnapshotMessage? _lyricsSnapshot;
    private LocalTrackRecord? _localTrack;
    private EffectiveTrackMetadata? _effectiveMetadata;
    private string _lyricsLoadedFrom = Unavailable;
    private string _localAssociationStatus = Unavailable;
    private string _transportStatus = "Waiting for NativeHost";
    private string _folderActionStatus = string.Empty;

    public SettingsPageId Id => SettingsPageId.Debug;
    public string Title => "Debug";
    public string LibraryPath => DisplayPath(_libraryPath);
    public string IndexPath => DisplayPath(_indexPath);
    public string LogsDirectory => DisplayPath(_logsDirectory);
    public string CrashReportsDirectory => DisplayPath(_crashReportsDirectory);
    public string TransportStatus
    {
        get => _transportStatus;
        private set => SetProperty(ref _transportStatus, value);
    }
    public string FolderActionStatus
    {
        get => _folderActionStatus;
        private set => SetProperty(ref _folderActionStatus, value);
    }
    public string PlaybackSource => _playbackSnapshot?.Envelope.Source ?? Unavailable;
    public string SourceTrackId => _playbackSnapshot?.Payload.Track.SourceTrackId ?? Unavailable;
    public string LocalTrackId => _localTrack?.LocalTrackId ?? Unavailable;
    public string EffectiveTitle => _effectiveMetadata?.Title ?? Unavailable;
    public string EffectiveArtist => _effectiveMetadata?.Artist ?? Unavailable;
    public string PlaybackState => _playbackSnapshot is not { } playback
        ? Unavailable
        : $"{(playback.Payload.Playback.Playing ? "Playing" : "Paused")} · " +
          $"{playback.Payload.Playback.PositionMs:N0} ms at {playback.Payload.Playback.PlaybackRate:0.##}×";
    public string LyricsLoadedFrom => _lyricsLoadedFrom;
    public string LocalAssociationStatus => _localAssociationStatus;
    public string LyricsSource => _lyricsSnapshot?.Payload.Source ??
        _playbackSnapshot?.Payload.Lyrics.Source ?? Unavailable;
    public string LyricsAvailability => _lyricsSnapshot is { } lyrics
        ? lyrics.Payload.Available ? "Available" : "Unavailable"
        : _playbackSnapshot is { } playback
            ? playback.Payload.Lyrics.Available ? "Available" : "Unavailable"
            : Unavailable;
    public string LyricsTiming => _lyricsSnapshot is { } lyrics
        ? lyrics.Payload.Timed ? "Timed" : "Untimed"
        : _playbackSnapshot is { } playback
            ? playback.Payload.Lyrics.Timed ? "Timed" : "Untimed"
            : Unavailable;
    public string RawPlaybackJson => _playbackSnapshot?.RawJson is { Length: > 0 } json
        ? json
        : "No playback snapshot is currently available.";
    public string RawLyricsJson => _lyricsSnapshot?.RawJson is { Length: > 0 } json
        ? json
        : "No raw lyrics snapshot is currently available.";

    public ICommand OpenLogsFolderCommand { get; }
    public ICommand OpenCrashReportsFolderCommand { get; }

    public DebugSettingsPageViewModel(string? libraryPath = null, string? indexPath = null,
        string? logsDirectory = null, string? crashReportsDirectory = null,
        Action<string>? openFolder = null)
    {
        _libraryPath = libraryPath ?? string.Empty;
        _indexPath = indexPath ?? string.Empty;
        _logsDirectory = logsDirectory ?? string.Empty;
        _crashReportsDirectory = crashReportsDirectory ?? string.Empty;
        _openFolder = openFolder ?? OpenFolderInShell;
        OpenLogsFolderCommand = new EditorCommand("debug.open-logs-folder",
            _ => OpenFolder(_logsDirectory), _ => IsConfigured(_logsDirectory));
        OpenCrashReportsFolderCommand = new EditorCommand("debug.open-crash-reports-folder",
            _ => OpenFolder(_crashReportsDirectory), _ => IsConfigured(_crashReportsDirectory));
    }

    public void SetTransportStatus(string status) => TransportStatus = status;

    public void UpdateRuntimeState(PlaybackSnapshotMessage? playback,
        LyricsSnapshotMessage? lyrics, LocalTrackRecord? localTrack,
        EffectiveTrackMetadata? effectiveMetadata, string lyricsLoadedFrom,
        string localAssociationStatus)
    {
        if (ReferenceEquals(_playbackSnapshot, playback) &&
            ReferenceEquals(_lyricsSnapshot, lyrics) &&
            ReferenceEquals(_localTrack, localTrack) &&
            Equals(_effectiveMetadata, effectiveMetadata) &&
            _lyricsLoadedFrom == lyricsLoadedFrom &&
            _localAssociationStatus == localAssociationStatus)
            return;

        _playbackSnapshot = playback;
        _lyricsSnapshot = lyrics;
        _localTrack = localTrack;
        _effectiveMetadata = effectiveMetadata;
        _lyricsLoadedFrom = lyricsLoadedFrom;
        _localAssociationStatus = localAssociationStatus;
        OnPropertyChanged(nameof(PlaybackSource));
        OnPropertyChanged(nameof(SourceTrackId));
        OnPropertyChanged(nameof(LocalTrackId));
        OnPropertyChanged(nameof(EffectiveTitle));
        OnPropertyChanged(nameof(EffectiveArtist));
        OnPropertyChanged(nameof(PlaybackState));
        OnPropertyChanged(nameof(LyricsLoadedFrom));
        OnPropertyChanged(nameof(LocalAssociationStatus));
        OnPropertyChanged(nameof(LyricsSource));
        OnPropertyChanged(nameof(LyricsAvailability));
        OnPropertyChanged(nameof(LyricsTiming));
        OnPropertyChanged(nameof(RawPlaybackJson));
        OnPropertyChanged(nameof(RawLyricsJson));
    }

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            _openFolder(path);
            FolderActionStatus = "Folder opened.";
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or
                                           UnauthorizedAccessException or InvalidOperationException or
                                           Win32Exception or System.Security.SecurityException)
        {
            FolderActionStatus = $"Could not open folder: {exception.Message}";
        }
    }

    private static bool IsConfigured(string path) => !string.IsNullOrWhiteSpace(path);

    private static string DisplayPath(string path) => IsConfigured(path) ? path : Unavailable;

    private static void OpenFolderInShell(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
}
