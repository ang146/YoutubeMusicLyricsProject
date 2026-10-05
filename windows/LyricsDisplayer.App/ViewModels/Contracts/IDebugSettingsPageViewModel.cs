using System.Windows.Input;
using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Core.Protocol;

namespace LyricsDisplayer;

public interface IDebugSettingsPageViewModel : ISettingsPageViewModel
{
    string LibraryPath { get; }
    string IndexPath { get; }
    string LogsDirectory { get; }
    string CrashReportsDirectory { get; }
    string TransportStatus { get; }
    string FolderActionStatus { get; }
    string PlaybackSource { get; }
    string SourceTrackId { get; }
    string LocalTrackId { get; }
    string EffectiveTitle { get; }
    string EffectiveArtist { get; }
    string PlaybackState { get; }
    string LyricsLoadedFrom { get; }
    string LocalAssociationStatus { get; }
    string LyricsSource { get; }
    string LyricsAvailability { get; }
    string LyricsTiming { get; }
    string RawPlaybackJson { get; }
    string RawLyricsJson { get; }
    ICommand OpenLogsFolderCommand { get; }
    ICommand OpenCrashReportsFolderCommand { get; }
    void SetTransportStatus(string status);
    void UpdateRuntimeState(PlaybackSnapshotMessage? playback, LyricsSnapshotMessage? lyrics,
        LyricsSnapshotMessage? rawLyricsSnapshot, LocalTrackRecord? localTrack,
        EffectiveTrackMetadata? effectiveMetadata, string lyricsLoadedFrom, string localAssociationStatus);
}
