using System.Collections.ObjectModel;
using System.Windows.Input;
using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Core.Protocol;
using LyricsDisplayer.Core.Timeline;
using LyricsDisplayer.Infrastructure.Commands;
using LyricsDisplayer.Resources;
using Microsoft.Extensions.Logging;

namespace LyricsDisplayer;

/// <summary>Immutable row projection of the shared lyrics presentation for the Main window.</summary>
public sealed record MainLyricsLineViewModel(
    int DocumentIndex,
    string Text,
    long? StartMs,
    long? EndMs,
    LyricLineRole Role)
{
    public bool IsCurrent => Role == LyricLineRole.Current;

    internal static MainLyricsLineViewModel From(LyricsPresentationLine line) =>
        new(line.Index, line.Text, line.StartMs, line.EndMs, line.Role);

    internal bool RepresentsSameDocumentLine(LyricsPresentationLine line) =>
        DocumentIndex == line.Index && Text == line.Text && StartMs == line.StartMs && EndMs == line.EndMs;
}

/// <summary>Presentation state for the full-document, non-overlay lyrics reading window.</summary>
public sealed class MainLyricsWindowViewModel : ViewModelBase, IMainLyricsViewModel
{
    private LyricsPresentationState _presentation = LyricsPresentationState.Pending;
    private LyricsSnapshotPayload? _mappedLyrics;
    private IReadOnlyList<LyricsLine>? _mappedTimelineLines;
    private int? _mappedCurrentTimelineIndex;
    private int? _mappedNextTimelineIndex;
    private bool _mappedLocalFileMissing;
    private bool _hasMappedPresentation;
    private MainLyricsLineViewModel? _currentLine;
    private (string Source, string SourceTrackId)? _playbackTrackIdentity;
    private LyricsSnapshotPayload? _currentIdentityLyrics;
    private int? _currentIdentityDocumentIndex;
    private int? _currentIdentityTimelineIndex;
    private string _connectionStatus = Strings.MainWaitingForPlaybackSource;
    private EffectiveTrackMetadata? _effectiveMetadata;

    public ObservableCollection<MainLyricsLineViewModel> Lines { get; } = [];
    public LyricsPresentationState Presentation => _presentation;
    public IMainLyricsSurfaceActionsViewModel SurfaceActions { get; }
    public MainLyricsLineViewModel? CurrentLine => _currentLine;
    public string EffectiveTitle => string.IsNullOrWhiteSpace(_effectiveMetadata?.Title)
        ? Strings.AppName
        : _effectiveMetadata.Title;
    public string EffectiveArtist => string.IsNullOrWhiteSpace(_effectiveMetadata?.Artist)
        ? string.Empty
        : _effectiveMetadata.Artist;
    public bool HasEffectiveArtist => EffectiveArtist.Length > 0;
    public bool HasLines => Lines.Count > 0;
    public bool HasNoLines => Lines.Count == 0;
    public string EmptyStateMessage => HasLines ? string.Empty : _presentation.Status switch
    {
        LyricsPresentationStatus.Pending => Strings.MainWaitingForLyricsEllipsis,
        LyricsPresentationStatus.Unavailable => Strings.LyricsUnavailable,
        LyricsPresentationStatus.Untimed => Strings.LyricsUntimed,
        LyricsPresentationStatus.LocalFileMissing => Strings.LyricsLocalFileMissing,
        LyricsPresentationStatus.Timed => Strings.MainNoLyricLinesAvailable,
        _ => string.Empty
    };
    public string LyricsStatusText => _presentation.Status switch
    {
        LyricsPresentationStatus.Pending => Strings.MainWaitingForLyrics,
        LyricsPresentationStatus.Unavailable => Strings.LyricsUnavailable,
        LyricsPresentationStatus.Untimed => Strings.LyricsUntimed,
        LyricsPresentationStatus.LocalFileMissing => Strings.LyricsLocalFileMissing,
        LyricsPresentationStatus.Timed => Strings.MainSynchronizedLyrics,
        _ => string.Empty
    };
    public string ConnectionStatus
    {
        get => _connectionStatus;
        private set => SetProperty(ref _connectionStatus, value);
    }

    public event Action<MainLyricsLineViewModel?>? CurrentLineChanged;
    public event Action? PlaybackTrackChanged;

    public MainLyricsWindowViewModel(IMainLyricsSurfaceActionsViewModel surfaceActions,
        ILogger<MainLyricsWindowViewModel> logger) : base(logger)
    {
        SurfaceActions = surfaceActions ?? throw new ArgumentNullException(nameof(surfaceActions));
        Logger.LogDebug("Main Lyrics Window ViewModel initialized.");
    }

    public void UpdateLyricsPresentation(
        LyricsSnapshotPayload? lyrics,
        LyricsTimelinePosition timeline,
        IReadOnlyList<LyricsLine> timelineOrderedLines,
        bool localFileMissing = false)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(timelineOrderedLines);

        if (_hasMappedPresentation && ReferenceEquals(_mappedLyrics, lyrics) &&
            ReferenceEquals(_mappedTimelineLines, timelineOrderedLines) &&
            _mappedCurrentTimelineIndex == timeline.CurrentIndex &&
            _mappedNextTimelineIndex == timeline.NextIndex &&
            _mappedLocalFileMissing == localFileMissing)
            return;

        var presentation = LyricsPresentationMapper.FromResolvedTimeline(
            lyrics, timeline, timelineOrderedLines, localFileMissing);
        _mappedLyrics = lyrics;
        _mappedTimelineLines = timelineOrderedLines;
        _mappedCurrentTimelineIndex = timeline.CurrentIndex;
        _mappedNextTimelineIndex = timeline.NextIndex;
        _mappedLocalFileMissing = localFileMissing;
        _hasMappedPresentation = true;
        ApplyPresentation(presentation);
    }

    public void SetConnectionStatus(string value) => ConnectionStatus = value;

    public void SetPlaybackTrackIdentity(string? source, string? sourceTrackId)
    {
        (string Source, string SourceTrackId)? identity = source is null || sourceTrackId is null
            ? null
            : (source, sourceTrackId);
        if (_playbackTrackIdentity == identity) return;

        _playbackTrackIdentity = identity;
        PlaybackTrackChanged?.Invoke();
    }

    public void SetEffectiveMetadata(EffectiveTrackMetadata? metadata)
    {
        if (_effectiveMetadata == metadata) return;
        _effectiveMetadata = metadata;
        OnPropertyChanged(nameof(EffectiveTitle));
        OnPropertyChanged(nameof(EffectiveArtist));
        OnPropertyChanged(nameof(HasEffectiveArtist));
    }

    private void ApplyPresentation(LyricsPresentationState presentation)
    {
        var previouslyHadLines = HasLines;
        var documentChanged = Lines.Count != presentation.Lines.Count ||
            presentation.Lines.Where((line, itemIndex) => !Lines[itemIndex].RepresentsSameDocumentLine(line)).Any();
        if (documentChanged)
        {
            Lines.Clear();
            foreach (var line in presentation.Lines)
                Lines.Add(MainLyricsLineViewModel.From(line));
        }
        else
        {
            for (var index = 0; index < presentation.Lines.Count; index++)
            {
                var line = presentation.Lines[index];
                if (Lines[index].Role != line.Role)
                    Lines[index] = MainLyricsLineViewModel.From(line);
            }
        }

        if (previouslyHadLines != HasLines)
        {
            OnPropertyChanged(nameof(HasLines));
            OnPropertyChanged(nameof(HasNoLines));
            OnPropertyChanged(nameof(EmptyStateMessage));
        }

        var previousStatus = _presentation.Status;
        _presentation = presentation;
        OnPropertyChanged(nameof(Presentation));
        if (previousStatus != presentation.Status)
        {
            OnPropertyChanged(nameof(EmptyStateMessage));
            OnPropertyChanged(nameof(LyricsStatusText));
        }

        var current = presentation.CurrentIndex is int currentIndex && currentIndex >= 0 && currentIndex < Lines.Count
            ? Lines[currentIndex]
            : null;
        var currentIdentityChanged = !ReferenceEquals(_currentIdentityLyrics, _mappedLyrics) ||
                                     _currentIdentityDocumentIndex != presentation.CurrentIndex ||
                                     _currentIdentityTimelineIndex != presentation.CurrentTimelineIndex;
        _currentIdentityLyrics = _mappedLyrics;
        _currentIdentityDocumentIndex = presentation.CurrentIndex;
        _currentIdentityTimelineIndex = presentation.CurrentTimelineIndex;
        if (!currentIdentityChanged) return;

        _currentLine = current;
        OnPropertyChanged(nameof(CurrentLine));
        CurrentLineChanged?.Invoke(current);
    }
}
