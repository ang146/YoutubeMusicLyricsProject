namespace LyricsDisplayer.Core.Library;

/// <summary>Single-timestamp validation shared by document-level timing adjustments.</summary>
public static class LrcTimestampRewriter
{
    public const string NegativeTimestampError = "The adjusted LRC would contain a negative timestamp.";
    public const string TimestampOverflowError = "An adjusted LRC timestamp cannot be represented safely.";

    /// <summary>Applies the shared safe-shift rules to one parsed timestamp occurrence.</summary>
    public static bool TryShiftTimestamp(long timestampMs, long deltaMs, string lyricsText,
        out long adjustedTimestampMs, out string? error)
    {
        ArgumentNullException.ThrowIfNull(lyricsText);
        adjustedTimestampMs = timestampMs;
        error = null;
        try
        {
            adjustedTimestampMs = checked(timestampMs + deltaMs);
        }
        catch (OverflowException)
        {
            adjustedTimestampMs = timestampMs;
            error = TimestampOverflowError;
            return false;
        }

        if (adjustedTimestampMs >= 0) return true;
        if (deltaMs < 0 && timestampMs == 0 && LrcLineSemantics.IsZeroTimeIntroOrBreakAnchor(lyricsText))
        {
            adjustedTimestampMs = 0;
            return true;
        }

        adjustedTimestampMs = timestampMs;
        error = NegativeTimestampError;
        return false;
    }
}
