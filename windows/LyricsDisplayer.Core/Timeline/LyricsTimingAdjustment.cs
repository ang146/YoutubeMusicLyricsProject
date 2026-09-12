namespace LyricsDisplayer.Core.Timeline;

public static class LyricsTimingAdjustment
{
    public static long GetEvaluationPosition(long playbackPositionMs, long globalOffsetMs)
    {
        try
        {
            return checked(playbackPositionMs - globalOffsetMs);
        }
        catch (OverflowException)
        {
            return globalOffsetMs > 0 ? long.MinValue : long.MaxValue;
        }
    }

    public static bool TryAdjustOffset(long currentOffsetMs, long deltaMs, out long adjustedOffsetMs)
    {
        try
        {
            adjustedOffsetMs = checked(currentOffsetMs + deltaMs);
            return true;
        }
        catch (OverflowException)
        {
            adjustedOffsetMs = currentOffsetMs;
            return false;
        }
    }
}
