using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Core.Protocol;
using LyricsDisplayer.Core.Timeline;
using LyricsDisplayer.Infrastructure.Commands;

namespace LyricsDisplayer;

public interface IMainLyricsViewModel : INotifyPropertyChanged
{
    ObservableCollection<MainLyricsLineViewModel> Lines { get; }
    LyricsPresentationState Presentation { get; }
    IRelayCommand OpenBuiltInEditorCommand { get; }
    IRelayCommand? OpenSettingsCommand { get; }
    MainLyricsLineViewModel? CurrentLine { get; }
    string EffectiveTitle { get; }
    string EffectiveArtist { get; }
    bool HasEffectiveArtist { get; }
    bool HasLines { get; }
    bool HasNoLines { get; }
    string EmptyStateMessage { get; }
    string LyricsStatusText { get; }
    string ConnectionStatus { get; }
    bool CanOpenExternalLyrics { get; }
    string ExternalLyricsStatus { get; }
    event Action<MainLyricsLineViewModel?>? CurrentLineChanged;
    void UpdateLyricsPresentation(LyricsSnapshotPayload? lyrics, LyricsTimelinePosition timeline,
        IReadOnlyList<LyricsLine> timelineOrderedLines, bool localFileMissing = false);
    void SetConnectionStatus(string value);
    void SetEffectiveMetadata(EffectiveTrackMetadata? metadata);
    void SetCanOpenExternalLyrics(bool value);
    void SetExternalLyricsStatus(string value);
}
