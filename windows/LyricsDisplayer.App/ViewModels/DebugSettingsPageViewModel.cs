using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Core.Protocol;
using LyricsDisplayer.Infrastructure.Commands;
using LyricsDisplayer.Resources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LyricsDisplayer;

public sealed class DebugSettingsPageViewModel : ViewModelBase, IDebugSettingsPageViewModel
{
    private static readonly JsonSerializerOptions PrettyPrintJsonOptions = new() { WriteIndented = true };
    private readonly string _libraryPath;
    private readonly string _indexPath;
    private readonly string _logsDirectory;
    private readonly string _crashReportsDirectory;
    private readonly Action<string> _openFolder;
    private PlaybackSnapshotMessage? _playbackSnapshot;
    private LyricsSnapshotMessage? _lyricsSnapshot;
    private LyricsSnapshotMessage? _rawLyricsSnapshot;
    private LocalTrackRecord? _localTrack;
    private EffectiveTrackMetadata? _effectiveMetadata;
    private string _lyricsLoadedFrom = Strings.ValueNotAvailable;
    private string _localAssociationStatus = Strings.ValueNotAvailable;
    private string _transportStatus = Strings.TransportWaitingForNativeHost;
    private string _folderActionStatus = string.Empty;

    public SettingsPageId Id => SettingsPageId.Debug;
    public string Title => Strings.SettingsDebug;
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
    public string PlaybackSource => _playbackSnapshot?.Envelope.Source ?? Strings.ValueNotAvailable;
    public string SourceTrackId => _playbackSnapshot?.Payload.Track.SourceTrackId ?? Strings.ValueNotAvailable;
    public string LocalTrackId => _localTrack?.LocalTrackId ?? Strings.ValueNotAvailable;
    public string EffectiveTitle => _effectiveMetadata?.Title ?? Strings.ValueNotAvailable;
    public string EffectiveArtist => _effectiveMetadata?.Artist ?? Strings.ValueNotAvailable;
    public string PlaybackState => _playbackSnapshot is not { } playback
        ? Strings.ValueNotAvailable
        : string.Format(CultureInfo.CurrentCulture, Strings.DebugPlaybackStateFormat,
            playback.Payload.Playback.Playing ? Strings.ValuePlaying : Strings.ValuePaused,
            playback.Payload.Playback.PositionMs, playback.Payload.Playback.PlaybackRate);
    public string LyricsLoadedFrom => _lyricsLoadedFrom;
    public string LocalAssociationStatus => _localAssociationStatus;
    public string LyricsSource => _lyricsSnapshot?.Payload.Source ??
        _playbackSnapshot?.Payload.Lyrics.Source ?? Strings.ValueNotAvailable;
    public string LyricsAvailability => _lyricsSnapshot is { } lyrics
        ? lyrics.Payload.Available ? Strings.ValueAvailable : Strings.ValueUnavailable
        : _playbackSnapshot is { } playback
            ? playback.Payload.Lyrics.Available ? Strings.ValueAvailable : Strings.ValueUnavailable
            : Strings.ValueNotAvailable;
    public string LyricsTiming => _lyricsSnapshot is { } lyrics
        ? lyrics.Payload.Timed ? Strings.ValueTimed : Strings.ValueUntimed
        : _playbackSnapshot is { } playback
            ? playback.Payload.Lyrics.Timed ? Strings.ValueTimed : Strings.ValueUntimed
            : Strings.ValueNotAvailable;
    public string RawPlaybackJson => FormatRawJsonForDisplay(_playbackSnapshot?.RawJson,
        Strings.DebugNoPlaybackSnapshot);
    public string RawLyricsJson => FormatRawJsonForDisplay(_rawLyricsSnapshot?.RawJson,
        Strings.DebugNoRawLyricsSnapshot);

    public ICommand OpenLogsFolderCommand { get; }
    public ICommand OpenCrashReportsFolderCommand { get; }

    public DebugSettingsPageViewModel(ICommandFactory commandFactory, ILogger<DebugSettingsPageViewModel> logger,
        string? libraryPath = null, string? indexPath = null,
        string? logsDirectory = null, string? crashReportsDirectory = null,
        Action<string>? openFolder = null)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(commandFactory);
        _libraryPath = libraryPath ?? string.Empty;
        _indexPath = indexPath ?? string.Empty;
        _logsDirectory = logsDirectory ?? string.Empty;
        _crashReportsDirectory = crashReportsDirectory ?? string.Empty;
        _openFolder = openFolder ?? OpenFolderInShell;
        OpenLogsFolderCommand = commandFactory.Create(ApplicationCommandIds.Debug.OpenLogsFolder,
            _ => OpenFolder(_logsDirectory), _ => IsConfigured(_logsDirectory));
        OpenCrashReportsFolderCommand = commandFactory.Create(ApplicationCommandIds.Debug.OpenCrashReportsFolder,
            _ => OpenFolder(_crashReportsDirectory), _ => IsConfigured(_crashReportsDirectory));
        Logger.LogDebug("Debug Settings ViewModel initialized.");
    }

    public void SetTransportStatus(string status) => TransportStatus = status;

    public void UpdateRuntimeState(PlaybackSnapshotMessage? playback,
        LyricsSnapshotMessage? lyrics, LyricsSnapshotMessage? rawLyricsSnapshot,
        LocalTrackRecord? localTrack,
        EffectiveTrackMetadata? effectiveMetadata, string lyricsLoadedFrom,
        string localAssociationStatus)
    {
        var displayedLyricsLoadedFrom = PresentLyricsLoadedFrom(lyricsLoadedFrom);
        var displayedLocalAssociationStatus = PresentLocalAssociationStatus(localAssociationStatus);
        if (ReferenceEquals(_playbackSnapshot, playback) &&
            ReferenceEquals(_lyricsSnapshot, lyrics) &&
            ReferenceEquals(_rawLyricsSnapshot, rawLyricsSnapshot) &&
            ReferenceEquals(_localTrack, localTrack) &&
            Equals(_effectiveMetadata, effectiveMetadata) &&
            _lyricsLoadedFrom == displayedLyricsLoadedFrom &&
            _localAssociationStatus == displayedLocalAssociationStatus)
            return;

        _playbackSnapshot = playback;
        _lyricsSnapshot = lyrics;
        _rawLyricsSnapshot = rawLyricsSnapshot;
        _localTrack = localTrack;
        _effectiveMetadata = effectiveMetadata;
        _lyricsLoadedFrom = displayedLyricsLoadedFrom;
        _localAssociationStatus = displayedLocalAssociationStatus;
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
            FolderActionStatus = Strings.DebugFolderOpened;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or
                                           UnauthorizedAccessException or InvalidOperationException or
                                           Win32Exception or System.Security.SecurityException)
        {
            FolderActionStatus = string.Format(CultureInfo.CurrentCulture,
                Strings.DebugFolderOpenFailed, exception.Message);
        }
    }

    private static bool IsConfigured(string path) => !string.IsNullOrWhiteSpace(path);

    private static string PresentLyricsLoadedFrom(string value) => value switch
    {
        "Pending / unknown" => Strings.DebugPendingUnknown,
        "YouTube Music (runtime)" => Strings.DebugYoutubeMusicRuntime,
        "Local Library" => Strings.DebugLocalLibrary,
        "Local Library (unavailable)" => Strings.DebugLocalLibraryUnavailable,
        "Local Library (external LRC rejected)" => Strings.DebugLocalLibraryExternalLrcRejected,
        "Local Library (last valid lyrics retained)" => Strings.DebugLocalLibraryLastValidLyricsRetained,
        "Local Library (file unavailable)" => Strings.DebugLocalLibraryFileUnavailable,
        "Local Library (file temporarily unavailable)" => Strings.DebugLocalLibraryFileTemporarilyUnavailable,
        _ => value
    };

    private static string PresentLocalAssociationStatus(string value) => value switch
    {
        "Not checked" => Strings.DebugAssociationNotChecked,
        nameof(LocalLyricsLookupStatus.Found) => Strings.DebugAssociationFound,
        nameof(LocalLyricsLookupStatus.NotFound) => Strings.DebugAssociationNotFound,
        nameof(LocalLyricsLookupStatus.LibraryUnavailable) or nameof(LyricsImportStatus.LibraryUnavailable) =>
            Strings.DebugAssociationLibraryUnavailable,
        nameof(LocalLyricsLookupStatus.IndexUnavailable) or nameof(LyricsImportStatus.IndexUnavailable) =>
            Strings.DebugAssociationIndexUnavailable,
        nameof(LocalLyricsLookupStatus.BrokenRecord) => Strings.DebugAssociationBrokenRecord,
        nameof(LocalLyricsLookupStatus.DuplicateAssociation) => Strings.DebugAssociationDuplicate,
        nameof(LyricsImportStatus.Imported) => Strings.DebugImportImported,
        nameof(LyricsImportStatus.AlreadyLocal) => Strings.DebugImportAlreadyLocal,
        nameof(LyricsImportStatus.NotTimed) => Strings.DebugImportNotTimed,
        nameof(LyricsImportStatus.BlockedByBrokenRecord) => Strings.DebugImportBlockedByBrokenRecord,
        nameof(LyricsImportStatus.FilesPersistedIndexFailed) => Strings.DebugImportFilesPersistedIndexFailed,
        nameof(LyricsImportStatus.Failed) => Strings.DebugImportFailed,
        "External LRC rejected" => Strings.DebugExternalLrcRejected,
        "Local LRC unavailable" => Strings.DebugLocalLrcUnavailable,
        "External LRC temporarily unavailable" => Strings.DebugExternalLrcTemporarilyUnavailable,
        _ => value
    };

    private static string DisplayPath(string path) => IsConfigured(path) ? path : Strings.ValueNotAvailable;

    private static string FormatRawJsonForDisplay(string? rawJson, string unavailableMessage)
    {
        if (string.IsNullOrEmpty(rawJson)) return unavailableMessage;

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            return JsonSerializer.Serialize(document.RootElement, PrettyPrintJsonOptions);
        }
        catch (JsonException)
        {
            return rawJson;
        }
    }

    private static void OpenFolderInShell(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
}
