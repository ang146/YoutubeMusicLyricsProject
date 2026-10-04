using LyricsDisplayer.Core.Protocol;
using LyricsDisplayer.Core.Timeline;

namespace LyricsDisplayer;

public enum LyricsPresentationStatus
{
    Pending,
    Unavailable,
    Untimed,
    LocalFileMissing,
    Timed
}

public enum LyricLineRole
{
    Neutral,
    Past,
    Current,
    Upcoming,
    Status
}

public static class LyricsPresentationMessages
{
    public const string Unavailable = "\u66AB\u7121\u53EF\u7528\u6B4C\u8A5E";
    public const string Untimed = "\u6B64\u6B4C\u66F2\u66AB\u7121\u540C\u6B65\u6B4C\u8A5E";
    public const string LocalFileMissing = "\u672C\u6A5F\u6B4C\u8A5E\u6A94\u6848\u907A\u5931";
}

/// <summary>
/// A surface-neutral runtime lyric occurrence. Index identifies the occurrence within this ordered
/// snapshot, so identical text and multi-timestamp occurrences remain distinguishable.
/// </summary>
public sealed record LyricsPresentationLine(
    int Index,
    string Text,
    long? StartMs,
    long? EndMs,
    LyricLineRole Role,
    int Distance)
{
    public bool IsCurrent => Role == LyricLineRole.Current;
}

/// <summary>
/// Full, ordered lyrics presentation semantics independent of any particular window or layout.
/// Lines retain source-document order; TimelineOrder maps the timeline's resolved order to them.
/// </summary>
public sealed record LyricsPresentationState(
    LyricsPresentationStatus Status,
    IReadOnlyList<LyricsPresentationLine> Lines,
    int? CurrentIndex,
    int? NextIndex,
    IReadOnlyList<int> TimelineOrder,
    int? CurrentTimelineIndex,
    int? NextTimelineIndex)
{
    public static LyricsPresentationState Pending { get; } =
        new(LyricsPresentationStatus.Pending, [], null, null, [], null, null);

    public LyricsPresentationLine? CurrentLine => LineAt(CurrentIndex);
    public LyricsPresentationLine? NextLine => LineAt(NextIndex);

    public LyricsPresentationLine? LineAt(int? index) =>
        index is int value && value >= 0 && value < Lines.Count ? Lines[value] : null;

    public LyricsPresentationLine? LineAtTimelineIndex(int? timelineIndex) =>
        timelineIndex is int value && value >= 0 && value < TimelineOrder.Count
            ? LineAt(TimelineOrder[value])
            : null;
}

public static class LyricsPresentationMapper
{
    public static LyricsPresentationState FromResolvedTimeline(
        LyricsSnapshotPayload? lyrics,
        LyricsTimelinePosition timeline,
        IReadOnlyList<LyricsLine> timelineOrderedLines,
        bool localFileMissing = false)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(timelineOrderedLines);

        if (localFileMissing)
            return new(LyricsPresentationStatus.LocalFileMissing, [], null, null, [], null, null);
        if (lyrics is null)
            return LyricsPresentationState.Pending;
        if (!lyrics.Available)
            return new(LyricsPresentationStatus.Unavailable, [], null, null, [], null, null);
        if (!lyrics.Timed)
        {
            var untimedLines = (lyrics.UntimedLines ?? [])
                .Select((text, index) => new LyricsPresentationLine(
                    index, text, null, null, LyricLineRole.Neutral, 0))
                .ToArray();
            return new(LyricsPresentationStatus.Untimed, untimedLines, null, null, [], null, null);
        }

        var documentLines = lyrics.Lines;
        var timelineOrder = MapTimelineToDocumentOrder(documentLines, timelineOrderedLines);
        if (timelineOrder is null)
        {
            // Inconsistent callers can still render the resolved timeline safely. Normal runtime
            // snapshots map one-to-one, preserving the payload's canonical document order.
            documentLines = timelineOrderedLines;
            timelineOrder = Enumerable.Range(0, timelineOrderedLines.Count).ToArray();
        }

        var currentTimelineIndex = IsValidIndex(timeline.CurrentIndex, timelineOrderedLines.Count)
            ? timeline.CurrentIndex : null;
        var nextTimelineIndex = IsValidIndex(timeline.NextIndex, timelineOrderedLines.Count)
            ? timeline.NextIndex : null;
        var documentIndexByTimeline = timelineOrder;
        int? currentIndex = currentTimelineIndex is int currentTimeline
            ? documentIndexByTimeline[currentTimeline] : null;
        int? nextIndex = nextTimelineIndex is int nextTimeline
            ? documentIndexByTimeline[nextTimeline] : null;
        var timelineIndexByDocument = new int?[documentLines.Count];
        for (var timelineIndex = 0; timelineIndex < documentIndexByTimeline.Length; timelineIndex++)
            timelineIndexByDocument[documentIndexByTimeline[timelineIndex]] = timelineIndex;

        var lines = documentLines.Select((line, index) =>
        {
            var (role, distance) = timelineIndexByDocument[index] is int timelineIndex
                ? GetRole(timelineIndex, currentTimelineIndex, nextTimelineIndex)
                : (LyricLineRole.Neutral, 0);
            return new LyricsPresentationLine(index, line.Text, line.StartMs, line.EndMs, role, distance);
        }).ToArray();

        return new(LyricsPresentationStatus.Timed, lines, currentIndex, nextIndex,
            documentIndexByTimeline, currentTimelineIndex, nextTimelineIndex);
    }

    private static bool IsValidIndex(int? index, int count) => index is int value && value >= 0 && value < count;

    private static int[]? MapTimelineToDocumentOrder(
        IReadOnlyList<LyricsLine> documentLines,
        IReadOnlyList<LyricsLine> timelineOrderedLines)
    {
        if (documentLines.Count != timelineOrderedLines.Count) return null;
        var documentOccurrences = new Dictionary<LyricsLine, Queue<int>>();
        for (var index = 0; index < documentLines.Count; index++)
        {
            var line = documentLines[index];
            if (!documentOccurrences.TryGetValue(line, out var indices))
                documentOccurrences.Add(line, indices = new Queue<int>());
            indices.Enqueue(index);
        }

        var timelineOrder = new int[timelineOrderedLines.Count];
        for (var index = 0; index < timelineOrderedLines.Count; index++)
        {
            if (!documentOccurrences.TryGetValue(timelineOrderedLines[index], out var indices) || indices.Count == 0)
                return null;
            timelineOrder[index] = indices.Dequeue();
        }
        return documentOccurrences.Values.Any(indices => indices.Count > 0) ? null : timelineOrder;
    }

    private static (LyricLineRole Role, int Distance) GetRole(
        int index, int? currentIndex, int? nextIndex)
    {
        if (currentIndex is int current)
        {
            if (index == current) return (LyricLineRole.Current, 0);
            return index < current
                ? (LyricLineRole.Past, current - index)
                : (LyricLineRole.Upcoming, index - current);
        }

        if (nextIndex is int next && index >= next)
            return (LyricLineRole.Upcoming, index - next + 1);

        return (LyricLineRole.Neutral, 0);
    }
}
