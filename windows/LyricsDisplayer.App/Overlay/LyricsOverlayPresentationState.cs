using System.Globalization;
using System.Text;
using LyricsDisplayer.Core.Protocol;
using LyricsDisplayer.Core.Settings;
using LyricsDisplayer.Core.Timeline;
using LyricsDisplayer.Resources;

namespace LyricsDisplayer;

public enum LyricsLayoutStyle { CenterStacked }

public sealed record PresentedLyricLine(
    int Index,
    string Text,
    LyricLineRole Role,
    int Distance = 0,
    double Scale = 1,
    double Opacity = 1)
{
    public double FontSize => LyricsOverlayAppearanceDefaults.BaseContextFontSize * Scale;
}

public readonly record struct LyricEmphasis(double Scale, double Opacity);

public static class LyricsOverlayAppearanceDefaults
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
        LyricsPresentationState presentation,
        double overlayWidth,
        double overlayHeight)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        if (presentation.Status != LyricsPresentationStatus.Timed || presentation.Lines.Count == 0) return [];
        if (!double.IsFinite(overlayWidth) || overlayWidth <= 0) overlayWidth = OverlayPreferences.DefaultWidth;
        if (!double.IsFinite(overlayHeight) || overlayHeight <= 0) overlayHeight = OverlayPreferences.DefaultHeight;

        var width = Math.Max(80, overlayWidth - 56);
        var budget = Math.Max(1, overlayHeight - VerticalPadding);
        var priority = BuildPrioritySequence(presentation);
        var prefix = FitPriorityPrefix(priority, budget, line => EstimateHeight(line, width));
        return prefix.OrderBy(line => line.Index).ToArray();
    }

    internal static IReadOnlyList<PresentedLyricLine> BuildPrioritySequence(
        LyricsPresentationState presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        var lines = presentation.Lines;
        var candidates = new List<PresentedLyricLine>(lines.Count);

        PresentedLyricLine? Create(int timelineIndex)
        {
            var line = presentation.LineAtTimelineIndex(timelineIndex);
            if (line is null) return null;
            var emphasis = LyricsOverlayAppearanceDefaults.ForRoleAndDistance(line.Role, line.Distance);
            return new(line.Index, line.Text, line.Role, line.Distance, emphasis.Scale, emphasis.Opacity);
        }

        if (presentation.CurrentTimelineIndex is int current && current >= 0 && current < presentation.TimelineOrder.Count)
        {
            if (Create(current) is { } currentLine) candidates.Add(currentLine);
            for (var distance = 1; distance < presentation.TimelineOrder.Count; distance++)
            {
                var upcoming = current + distance;
                if (upcoming < presentation.TimelineOrder.Count && Create(upcoming) is { } upcomingLine)
                    candidates.Add(upcomingLine);

                var past = current - distance;
                if (past >= 0 && Create(past) is { } pastLine)
                    candidates.Add(pastLine);
            }
        }
        else
        {
            var first = presentation.NextTimelineIndex.GetValueOrDefault(0);
            if (first < 0 || first >= presentation.TimelineOrder.Count) first = 0;
            for (var index = first; index < presentation.TimelineOrder.Count; index++)
                if (Create(index) is { Role: LyricLineRole.Upcoming } upcomingLine)
                    candidates.Add(upcomingLine);
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
        var fontSize = LyricsOverlayAppearanceDefaults.BaseContextFontSize * line.Scale;
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
    public static LyricsOverlayPresentationState Empty { get; } = new(string.Empty, string.Empty);
    public LyricsContentMode ContentMode { get; init; } = LyricsContentMode.TwoLines;
    public LyricsLayoutStyle LayoutStyle { get; init; } = LyricsLayoutStyle.CenterStacked;
    public int? CurrentIndex { get; init; }
    public IReadOnlyList<PresentedLyricLine> AllLines { get; init; } = [];
    public bool IsLocalFileMissing { get; init; }

    public bool EquivalentTo(LyricsOverlayPresentationState other) =>
        PrimaryText == other.PrimaryText && SecondaryText == other.SecondaryText &&
        ContentMode == other.ContentMode && LayoutStyle == other.LayoutStyle && CurrentIndex == other.CurrentIndex &&
        IsLocalFileMissing == other.IsLocalFileMissing && AllLines.SequenceEqual(other.AllLines);

    public static LyricsOverlayPresentationState FromLyrics(
        LyricsSnapshotPayload? lyrics,
        LyricsTimelinePosition timeline,
        LyricsContentMode contentMode = LyricsContentMode.TwoLines,
        double overlayWidth = OverlayPreferences.DefaultWidth,
        double overlayHeight = OverlayPreferences.DefaultHeight,
        IReadOnlyList<LyricsLine>? timelineOrderedLines = null,
        bool localFileMissing = false)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        timelineOrderedLines ??= lyrics?.Lines ?? [];
        var presentation = LyricsPresentationMapper.FromResolvedTimeline(
            lyrics, timeline, timelineOrderedLines, localFileMissing);
        return FromPresentation(presentation, contentMode, overlayWidth, overlayHeight);
    }

    public static LyricsOverlayPresentationState FromPresentation(
        LyricsPresentationState presentation,
        LyricsContentMode contentMode = LyricsContentMode.TwoLines,
        double overlayWidth = OverlayPreferences.DefaultWidth,
        double overlayHeight = OverlayPreferences.DefaultHeight)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var statusText = presentation.Status switch
        {
            LyricsPresentationStatus.LocalFileMissing => Strings.LyricsLocalFileMissing,
            LyricsPresentationStatus.Unavailable => Strings.LyricsUnavailable,
            LyricsPresentationStatus.Untimed => Strings.LyricsUntimed,
            LyricsPresentationStatus.Pending => string.Empty,
            LyricsPresentationStatus.Timed => null,
            _ => throw new ArgumentOutOfRangeException(nameof(presentation))
        };
        if (statusText is not null)
            return new(statusText, string.Empty)
            {
                ContentMode = contentMode,
                IsLocalFileMissing = presentation.Status == LyricsPresentationStatus.LocalFileMissing
            };

        var current = presentation.CurrentLine?.Text ?? string.Empty;
        var next = presentation.NextLine?.Text ?? string.Empty;
        return contentMode switch
        {
            LyricsContentMode.OneLine => new(string.IsNullOrEmpty(current) ? next : current, string.Empty)
                { ContentMode = contentMode },
            LyricsContentMode.TwoLines => new(current, next) { ContentMode = contentMode },
            LyricsContentMode.AllLyrics => new(string.Empty, string.Empty)
            {
                ContentMode = contentMode,
                CurrentIndex = presentation.CurrentIndex,
                AllLines = LyricsContextWindow.Select(presentation, overlayWidth, overlayHeight)
            },
            _ => throw new ArgumentOutOfRangeException(nameof(contentMode))
        };
    }
}
