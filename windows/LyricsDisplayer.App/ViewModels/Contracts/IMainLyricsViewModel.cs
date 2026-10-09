using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Core.Protocol;
using LyricsDisplayer.Core.Timeline;

namespace LyricsDisplayer;

public interface IMainLyricsViewModel : INotifyPropertyChanged
{
    ObservableCollection<MainLyricsLineViewModel> Lines { get; }
    LyricsPresentationState Presentation { get; }
    IMainLyricsSurfaceActionsViewModel SurfaceActions { get; }
    MainLyricsLineViewModel? CurrentLine { get; }
    string EffectiveTitle { get; }
    string EffectiveArtist { get; }
    bool HasEffectiveArtist { get; }
    bool HasLines { get; }
    bool HasNoLines { get; }
    string EmptyStateMessage { get; }
    string LyricsStatusText { get; }
    string ConnectionStatus { get; }
    event Action<MainLyricsLineViewModel?>? CurrentLineChanged;
    event Action? PlaybackTrackChanged;
    void UpdateLyricsPresentation(LyricsSnapshotPayload? lyrics, LyricsTimelinePosition timeline,
        IReadOnlyList<LyricsLine> timelineOrderedLines, bool localFileMissing = false);
    void SetPlaybackTrackIdentity(string? source, string? sourceTrackId);
    void SetConnectionStatus(string value);
    void SetEffectiveMetadata(EffectiveTrackMetadata? metadata);
}
