using System.Collections.Frozen;

namespace LyricsDisplayer.Core.Library;

/// <summary>Shared semantics for explicit lyric-line markers in portable LRC content.</summary>
public static class LrcLineSemantics
{
    /// <summary>The immutable built-in explicit break marker set.</summary>
    public static IReadOnlySet<string> DefaultExplicitBreakMarkers { get; } =
        new[] { "♪" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Determines whether lyric text represents a zero-time intro/break anchor. Blank text is
    /// intrinsic; a supplied marker set is the complete effective set and is matched ordinally
    /// after trimming, otherwise the immutable defaults are used.
    /// </summary>
    public static bool IsZeroTimeIntroOrBreakAnchor(
        string? lyricsText, IReadOnlySet<string>? explicitBreakMarkers = null)
    {
        if (string.IsNullOrWhiteSpace(lyricsText)) return true;

        var trimmedLyricsText = lyricsText.Trim();
        var effectiveMarkers = explicitBreakMarkers ?? DefaultExplicitBreakMarkers;
        return effectiveMarkers.Any(marker =>
            string.Equals(trimmedLyricsText, marker, StringComparison.Ordinal));
    }
}
