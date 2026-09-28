using LyricsDisplayer.Core.Protocol;
using LyricsDisplayer.Core.Settings;
using LyricsDisplayer.Core.Timeline;

namespace LyricsDisplayer;

public sealed record LyricsOverlayPresentationState(string PrimaryText, string SecondaryText)
{
    public const string NoLyricsText = "暫無可用歌詞";
    public const string UntimedLyricsText = "此歌曲暫無同步歌詞";

    public static LyricsOverlayPresentationState Empty { get; } = new(string.Empty, string.Empty);

    public static LyricsOverlayPresentationState FromLyrics(
        LyricsSnapshotPayload? lyrics,
        LyricsTimelinePosition timeline,
        OverlayDisplayMode displayMode = OverlayDisplayMode.TwoLines)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        if (lyrics is null) return Empty;
        if (!lyrics.Available) return new(NoLyricsText, string.Empty);
        if (!lyrics.Timed) return new(UntimedLyricsText, string.Empty);
        var current = timeline.CurrentLine?.Text ?? string.Empty;
        var next = timeline.NextLine?.Text ?? string.Empty;
        return displayMode == OverlayDisplayMode.OneLine
            ? new(string.IsNullOrEmpty(current) ? next : current, string.Empty)
            : new(current, next);
    }
}
