using System.Globalization;
using System.Text;
using LyricsDisplayer.Core.Protocol;
using LyricsDisplayer.Core.Settings;
using LyricsDisplayer.Core.Timeline;

namespace LyricsDisplayer;

public enum LyricLineRole { Past, Current, Upcoming, Status }
public enum LyricsLayoutStyle { CenterStacked }

public sealed record PresentedLyricLine(
    int Index,
    string Text,
    LyricLineRole Role,
    int Distance = 0,
    double Scale = 1,
    double Opacity = 1)
{
    public double FontSize => LyricsPresentationDefaults.BaseContextFontSize * Scale;
}

public readonly record struct LyricEmphasis(double Scale, double Opacity);

public static class LyricsPresentationDefaults
{
    public const double BaseContextFontSize = 26;
    public const double CurrentRoleScale = 1.06;
    public const double ContextMinimumScale = 0.72;
    public const double ContextMinimumOpacity = 0.52;

    public static LyricEmphasis ForRoleAndDistance(LyricLineRole role, int distance)
    {
        if (role == LyricLineRole.Current) return new(CurrentRoleScale, 1);

        var normalizedDistance = Math.Max(1, distance);
        var (roleScale, roleOpacity) = role switch
        {
            LyricLineRole.Past => (0.95, 0.82),
            LyricLineRole.Upcoming => (1.0, 0.98),
            _ => (1.0, 1.0)
        };
        return new(
            Math.Max(ContextMinimumScale, roleScale - 0.10 * (normalizedDistance - 1)),
            Math.Max(ContextMinimumOpacity, roleOpacity - 0.12 * (normalizedDistance - 1)));
    }
}

public static class LyricsContextWindow
{
    private const double VerticalPadding = 38;

    public static IReadOnlyList<PresentedLyricLine> Select(
        IReadOnlyList<LyricsLine> normalizedLines,
        int? currentIndex,
        int? nextIndex,
        double overlayWidth,
        double overlayHeight)
    {
        ArgumentNullException.ThrowIfNull(normalizedLines);
        if (normalizedLines.Count == 0) return [];
        if (!double.IsFinite(overlayWidth) || overlayWidth <= 0) overlayWidth = OverlayPreferences.DefaultWidth;
        if (!double.IsFinite(overlayHeight) || overlayHeight <= 0) overlayHeight = OverlayPreferences.DefaultHeight;

        var width = Math.Max(80, overlayWidth - 56);
        var budget = Math.Max(1, overlayHeight - VerticalPadding);
        var priority = BuildPrioritySequence(normalizedLines, currentIndex, nextIndex);
        var prefix = FitPriorityPrefix(priority, budget, line => EstimateHeight(line, width));
        return prefix.OrderBy(line => line.Index).ToArray();
    }

    internal static IReadOnlyList<PresentedLyricLine> BuildPrioritySequence(
        IReadOnlyList<LyricsLine> normalizedLines,
        int? currentIndex,
        int? nextIndex)
    {
        ArgumentNullException.ThrowIfNull(normalizedLines);
        var candidates = new List<PresentedLyricLine>(normalizedLines.Count);

        PresentedLyricLine Create(int index, LyricLineRole role, int distance)
        {
            var emphasis = LyricsPresentationDefaults.ForRoleAndDistance(role, distance);
            return new(index, normalizedLines[index].Text, role, distance, emphasis.Scale, emphasis.Opacity);
        }

        if (currentIndex is int current && current >= 0 && current < normalizedLines.Count)
        {
            candidates.Add(Create(current, LyricLineRole.Current, 0));
            for (var distance = 1; distance < normalizedLines.Count; distance++)
            {
                var upcoming = current + distance;
                if (upcoming < normalizedLines.Count)
                    candidates.Add(Create(upcoming, LyricLineRole.Upcoming, distance));

                var past = current - distance;
                if (past >= 0)
                    candidates.Add(Create(past, LyricLineRole.Past, distance));
            }
        }
        else
        {
            var first = nextIndex.GetValueOrDefault(0);
            if (first < 0 || first >= normalizedLines.Count) first = 0;
            for (var index = first; index < normalizedLines.Count; index++)
                candidates.Add(Create(index, LyricLineRole.Upcoming, index - first + 1));
        }

        return candidates;
    }

    internal static IReadOnlyList<PresentedLyricLine> FitPriorityPrefix(
        IReadOnlyList<PresentedLyricLine> prioritySequence,
        double availableHeight,
        Func<PresentedLyricLine, double> measureHeight)
    {
        ArgumentNullException.ThrowIfNull(prioritySequence);
        ArgumentNullException.ThrowIfNull(measureHeight);

        var selected = new List<PresentedLyricLine>(prioritySequence.Count);
        var measured = 0d;
        var currentIsMandatory = prioritySequence.Count > 0 &&
                                 prioritySequence[0].Role == LyricLineRole.Current;

        foreach (var candidate in prioritySequence)
        {
            var height = measureHeight(candidate);
            if (!double.IsFinite(height) || height < 0)
                throw new ArgumentOutOfRangeException(nameof(measureHeight), "Measured line heights must be finite and non-negative.");

            if (measured + height > availableHeight)
            {
                // The timeline Current occurrence is retained even if it exceeds the estimate;
                // every later item must fit or the priority prefix ends here.
                if (selected.Count == 0 && currentIsMandatory)
                {
                    selected.Add(candidate);
                    measured += height;
                    continue;
                }

                break;
            }

            selected.Add(candidate);
            measured += height;
        }

        return selected;
    }

    private static double EstimateHeight(PresentedLyricLine line, double width)
    {
        var fontSize = LyricsPresentationDefaults.BaseContextFontSize * line.Scale;
        var wrappedLines = line.Text.Split('\n').Sum(paragraph =>
            Math.Max(1, Math.Ceiling(EstimateTextWidth(paragraph, fontSize) / width)));
        return wrappedLines * fontSize * 1.28 + 8;
    }

    private static double EstimateTextWidth(string text, double fontSize)
    {
        var width = 0d;
        foreach (var rune in text.EnumerateRunes())
        {
            var value = rune.Value;
            if (value is '\r' or '\n') continue;
            var category = Rune.GetUnicodeCategory(rune);
            var factor = Rune.IsWhiteSpace(rune) ? 0.34 :
                category is UnicodeCategory.OtherLetter or UnicodeCategory.ModifierSymbol or UnicodeCategory.OtherSymbol
                    ? 1.0
                    : 0.56;
            if (value is >= 0x2E80 and <= 0x9FFF or >= 0xAC00 and <= 0xD7AF or >= 0xF900 and <= 0xFAFF)
                factor = 1.0;
            width += fontSize * factor;
        }
        return width;
    }
}

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
        ContentMode == other.ContentMode && LayoutStyle == other.LayoutStyle && CurrentIndex == other.CurrentIndex &&
        AllLines.SequenceEqual(other.AllLines);

    public static LyricsOverlayPresentationState FromLyrics(
        LyricsSnapshotPayload? lyrics,
        LyricsTimelinePosition timeline,
        LyricsContentMode contentMode = LyricsContentMode.TwoLines,
        double overlayWidth = OverlayPreferences.DefaultWidth,
        double overlayHeight = OverlayPreferences.DefaultHeight,
        IReadOnlyList<LyricsLine>? normalizedLines = null)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        if (lyrics is null) return Empty with { ContentMode = contentMode };
        if (!lyrics.Available) return new(NoLyricsText, string.Empty) { ContentMode = contentMode };
        if (!lyrics.Timed) return new(UntimedLyricsText, string.Empty) { ContentMode = contentMode };

        // LyricsTimeline uses this same stable ordering; indexes identify exact occurrences,
        // including duplicate timestamps and duplicate text.
        normalizedLines ??= NormalizeLines(lyrics.Lines);
        var current = timeline.CurrentLine?.Text ?? string.Empty;
        var next = timeline.NextLine?.Text ?? string.Empty;
        return contentMode switch
        {
            LyricsContentMode.OneLine => new(string.IsNullOrEmpty(current) ? next : current, string.Empty)
                { ContentMode = contentMode },
            LyricsContentMode.TwoLines => new(current, next) { ContentMode = contentMode },
            LyricsContentMode.AllLyrics => new(string.Empty, string.Empty)
            {
                ContentMode = contentMode,
                CurrentIndex = timeline.CurrentIndex,
                AllLines = LyricsContextWindow.Select(normalizedLines, timeline.CurrentIndex,
                    timeline.NextIndex, overlayWidth, overlayHeight)
            },
            _ => throw new ArgumentOutOfRangeException(nameof(contentMode))
        };
    }

    public static IReadOnlyList<LyricsLine> NormalizeLines(IReadOnlyList<LyricsLine> lines) =>
        lines.Select((line, index) => (line, index)).OrderBy(item => item.line.StartMs)
            .ThenBy(item => item.index).Select(item => item.line).ToArray();
}
