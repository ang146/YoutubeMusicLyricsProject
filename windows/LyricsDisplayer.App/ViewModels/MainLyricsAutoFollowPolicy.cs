namespace LyricsDisplayer;

/// <summary>Tracks temporary manual-scroll suspension of Main Lyrics Window auto-follow.</summary>
public sealed class MainLyricsAutoFollowPolicy
{
    public static readonly TimeSpan ManualScrollResumeDelay = TimeSpan.FromSeconds(5);

    private DateTimeOffset? _resumeAt;

    public bool IsManualScrollOverrideActive => _resumeAt.HasValue;

    public bool ShouldCenterCurrentLine(bool hasCurrentLine) =>
        hasCurrentLine && !IsManualScrollOverrideActive;

    public bool NotifyManualScroll(DateTimeOffset now, bool isProgrammatic = false)
    {
        if (isProgrammatic) return false;

        _resumeAt = now + ManualScrollResumeDelay;
        return true;
    }

    public TimeSpan? GetRemainingResumeDelay(DateTimeOffset now) => _resumeAt is { } resumeAt
        ? resumeAt > now ? resumeAt - now : TimeSpan.Zero
        : null;

    public bool TryResume(DateTimeOffset now)
    {
        if (_resumeAt is not { } resumeAt || now < resumeAt) return false;

        _resumeAt = null;
        return true;
    }

    public void Reset() => _resumeAt = null;
}
