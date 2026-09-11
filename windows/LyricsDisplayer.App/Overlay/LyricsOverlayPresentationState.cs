using LyricsDisplayer.Core.Timeline;

namespace LyricsDisplayer;

public sealed record LyricsOverlayPresentationState(string PrimaryText, string SecondaryText)
{
    public static LyricsOverlayPresentationState Empty { get; } = new(string.Empty, string.Empty);

    public static LyricsOverlayPresentationState FromTimeline(LyricsTimelinePosition timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        return new(timeline.CurrentLine?.Text ?? string.Empty, timeline.NextLine?.Text ?? string.Empty);
    }
}
