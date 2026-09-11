using LyricsDisplayer.Core.Protocol;

namespace LyricsDisplayer.Core.Timeline;

public sealed record LyricsTimelinePosition(
    int? CurrentIndex,
    LyricsLine? CurrentLine,
    int? NextIndex,
    LyricsLine? NextLine)
{
    public bool HasLyrics => CurrentLine is not null || NextLine is not null;
}

public sealed class LyricsTimeline
{
    private readonly IReadOnlyList<LyricsLine> _lines;

    public static LyricsTimeline Empty { get; } = new([]);

    public LyricsTimeline(IReadOnlyList<LyricsLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        _lines = lines.Select((line, originalIndex) => (Line: line, OriginalIndex: originalIndex))
            .OrderBy(item => item.Line.StartMs)
            .ThenBy(item => item.OriginalIndex)
            .Select(item => item.Line)
            .ToArray();
    }

    public bool HasLyrics => _lines.Count > 0;

    public LyricsTimelinePosition Evaluate(long positionMs)
    {
        if (_lines.Count == 0) return new(null, null, null, null);
        if (positionMs < _lines[0].StartMs) return new(null, null, 0, _lines[0]);

        // Upper-bound search deliberately selects the last stable line when starts are equal.
        var low = 0;
        var high = _lines.Count;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (_lines[middle].StartMs <= positionMs) low = middle + 1;
            else high = middle;
        }

        var currentIndex = low - 1;
        var nextIndex = currentIndex + 1;
        return nextIndex < _lines.Count
            ? new(currentIndex, _lines[currentIndex], nextIndex, _lines[nextIndex])
            : new(currentIndex, _lines[currentIndex], null, null);
    }
}
