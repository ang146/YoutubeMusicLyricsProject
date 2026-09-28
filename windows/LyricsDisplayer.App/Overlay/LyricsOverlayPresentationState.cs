using LyricsDisplayer.Core.Protocol;
using LyricsDisplayer.Core.Settings;
using LyricsDisplayer.Core.Timeline;

namespace LyricsDisplayer;

public enum LyricLineRole
{
    Past,
    Current,
    Upcoming,
    Status
}

public enum LyricsLayoutStyle
{
    CenterStacked
}

public sealed record PresentedLyricLine(int Index, string Text, LyricLineRole Role);

public sealed record LyricsOverlayPresentationState(string PrimaryText, string SecondaryText)
{
    public const string NoLyricsText = "暫無可用歌詞";
    public const string UntimedLyricsText = "此歌曲暫無同步歌詞";

    public static LyricsOverlayPresentationState Empty { get; } = new(string.Empty, string.Empty);
    public LyricsContentMode ContentMode { get; init; } = LyricsContentMode.TwoLines;
    public LyricsLayoutStyle LayoutStyle { get; init; } = LyricsLayoutStyle.CenterStacked;
    public int? CurrentIndex { get; init; }
    public IReadOnlyList<PresentedLyricLine> AllLines { get; init; } = [];

    public bool EquivalentTo(LyricsOverlayPresentationState other) =>
        PrimaryText == other.PrimaryText && SecondaryText == other.SecondaryText &&
        ContentMode == other.ContentMode && CurrentIndex == other.CurrentIndex &&
        AllLines.SequenceEqual(other.AllLines);

    public static LyricsOverlayPresentationState FromLyrics(
        LyricsSnapshotPayload? lyrics,
        LyricsTimelinePosition timeline,
        LyricsContentMode contentMode = LyricsContentMode.TwoLines)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        if (lyrics is null) return Empty with { ContentMode = contentMode };
        if (!lyrics.Available) return new(NoLyricsText, string.Empty) { ContentMode = contentMode };
        if (!lyrics.Timed) return new(UntimedLyricsText, string.Empty) { ContentMode = contentMode };
        var current = timeline.CurrentLine?.Text ?? string.Empty;
        var next = timeline.NextLine?.Text ?? string.Empty;
        var lines = lyrics.Lines.Select((line, index) => new PresentedLyricLine(index, line.Text,
            timeline.CurrentIndex == index ? LyricLineRole.Current :
            timeline.CurrentIndex is not null && index < timeline.CurrentIndex ? LyricLineRole.Past :
            LyricLineRole.Upcoming)).ToArray();
        return contentMode switch
        {
            LyricsContentMode.OneLine => new(string.IsNullOrEmpty(current) ? next : current, string.Empty)
                { ContentMode = contentMode },
            LyricsContentMode.TwoLines => new(current, next) { ContentMode = contentMode },
            LyricsContentMode.AllLyrics => new(string.Empty, string.Empty)
            {
                ContentMode = contentMode,
                CurrentIndex = timeline.CurrentIndex,
                AllLines = lines
            },
            _ => throw new ArgumentOutOfRangeException(nameof(contentMode))
        };
    }
}
