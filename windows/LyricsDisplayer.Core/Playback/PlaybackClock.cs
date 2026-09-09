using System.Diagnostics;

namespace LyricsDisplayer.Core.Playback;

public interface IMonotonicTimeSource
{
    long GetTimestamp();

    TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp);
}

public sealed class StopwatchTimeSource : IMonotonicTimeSource
{
    public static StopwatchTimeSource Instance { get; } = new();

    private StopwatchTimeSource()
    {
    }

    public long GetTimestamp() => Stopwatch.GetTimestamp();

    public TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp) =>
        Stopwatch.GetElapsedTime(startingTimestamp, endingTimestamp);
}

public sealed class PlaybackClock
{
    private readonly object _gate = new();
    private readonly IMonotonicTimeSource _timeSource;
    private bool _hasState;
    private bool _playing;
    private long _anchorPositionMs;
    private long _anchorTimestamp;
    private long _durationMs;
    private double _playbackRate = 1.0;

    public PlaybackClock(IMonotonicTimeSource? timeSource = null)
    {
        _timeSource = timeSource ?? StopwatchTimeSource.Instance;
    }

    public bool HasState
    {
        get
        {
            lock (_gate)
            {
                return _hasState;
            }
        }
    }

    public long GetPositionMs()
    {
        lock (_gate)
        {
            return GetPositionAt(_timeSource.GetTimestamp());
        }
    }

    public void Rebase(long positionMs, long durationMs, bool playing, double playbackRate)
    {
        if (!double.IsFinite(playbackRate) || playbackRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(playbackRate),
                "Playback rate must be a positive finite number.");
        }

        lock (_gate)
        {
            _durationMs = Math.Max(0, durationMs);
            _anchorPositionMs = Clamp(positionMs, _durationMs);
            _anchorTimestamp = _timeSource.GetTimestamp();
            _playbackRate = playbackRate;
            _playing = playing;
            _hasState = true;
        }
    }

    public void Freeze()
    {
        lock (_gate)
        {
            if (!_hasState)
            {
                return;
            }

            var timestamp = _timeSource.GetTimestamp();
            _anchorPositionMs = GetPositionAt(timestamp);
            _anchorTimestamp = timestamp;
            _playing = false;
        }
    }

    private long GetPositionAt(long timestamp)
    {
        if (!_hasState)
        {
            return 0;
        }

        if (!_playing)
        {
            return _anchorPositionMs;
        }

        var elapsedMs = Math.Max(0, _timeSource.GetElapsedTime(_anchorTimestamp, timestamp).TotalMilliseconds);
        var estimatedPosition = _anchorPositionMs + (elapsedMs * _playbackRate);
        if (!double.IsFinite(estimatedPosition))
        {
            return _durationMs;
        }

        var clampedPosition = Math.Clamp(estimatedPosition, 0, (double)_durationMs);
        return (long)Math.Round(clampedPosition, MidpointRounding.AwayFromZero);
    }

    private static long Clamp(long positionMs, long durationMs) => Math.Clamp(positionMs, 0, durationMs);
}
