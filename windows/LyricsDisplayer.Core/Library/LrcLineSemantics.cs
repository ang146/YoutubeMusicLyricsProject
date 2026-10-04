namespace LyricsDisplayer.Core.Library;

/// <summary>Shared semantics for explicit lyric-line markers in portable LRC content.</summary>
public static class LrcLineSemantics
{
    public const string ExplicitBreakMarker = "♪";

    public static bool IsZeroTimeIntroOrBreakAnchor(string? lyricsText) =>
        string.IsNullOrWhiteSpace(lyricsText) ||
        string.Equals(lyricsText.Trim(), ExplicitBreakMarker, StringComparison.Ordinal);
}
