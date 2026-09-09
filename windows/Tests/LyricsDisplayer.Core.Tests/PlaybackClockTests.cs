using LyricsDisplayer.Core.Playback;

namespace LyricsDisplayer.Core.Tests;

[TestFixture]
public sealed class PlaybackClockTests
{
    [Test]
    public void FirstRebaseInitialisesClockAtAuthoritativePosition()
    {
        var clock = CreateClock(out _);

        clock.Rebase(10_000, 100_000, true, 1.0);

        Assert.Multiple(() =>
        {
            Assert.That(clock.HasState, Is.True);
            Assert.That(clock.GetPositionMs(), Is.EqualTo(10_000));
        });
    }

    [TestCase(0.5, 10_500)]
    [TestCase(1.0, 11_000)]
    [TestCase(2.0, 12_000)]
    public void PlayingClockAdvancesAtPlaybackRate(double rate, long expectedPosition)
    {
        var clock = CreateClock(out var time);
        clock.Rebase(10_000, 100_000, true, rate);

        time.AdvanceMilliseconds(1_000);

        Assert.That(clock.GetPositionMs(), Is.EqualTo(expectedPosition));
    }

    [Test]
    public void PausedClockDoesNotAdvance()
    {
        var clock = CreateClock(out var time);
        clock.Rebase(10_000, 100_000, false, 1.0);

        time.AdvanceMilliseconds(25_000);

        Assert.That(clock.GetPositionMs(), Is.EqualTo(10_000));
    }

    [Test]
    public void AuthoritativeSnapshotCorrectsAndResetsAnchor()
    {
        var clock = CreateClock(out var time);
        clock.Rebase(10_000, 100_000, true, 1.0);
        time.AdvanceMilliseconds(500);
        Assert.That(clock.GetPositionMs(), Is.EqualTo(10_500));

        clock.Rebase(10_540, 100_000, true, 1.0);
        time.AdvanceMilliseconds(250);

        Assert.That(clock.GetPositionMs(), Is.EqualTo(10_790));
    }

    [Test]
    public void ForwardSeekRebasesImmediately()
    {
        var clock = CreateClock(out var time);
        clock.Rebase(10_000, 100_000, true, 1.0);
        time.AdvanceMilliseconds(200);

        clock.Rebase(80_000, 100_000, true, 1.0);

        Assert.That(clock.GetPositionMs(), Is.EqualTo(80_000));
    }

    [Test]
    public void BackwardSeekRebasesImmediately()
    {
        var clock = CreateClock(out var time);
        clock.Rebase(80_000, 100_000, true, 1.0);
        time.AdvanceMilliseconds(200);

        clock.Rebase(20_000, 100_000, true, 1.0);

        Assert.That(clock.GetPositionMs(), Is.EqualTo(20_000));
    }

    [Test]
    public void PauseAndResumeUseNewAuthoritativeAnchors()
    {
        var clock = CreateClock(out var time);
        clock.Rebase(10_000, 100_000, true, 1.0);
        time.AdvanceMilliseconds(500);

        clock.Rebase(10_520, 100_000, false, 1.0);
        time.AdvanceMilliseconds(5_000);
        Assert.That(clock.GetPositionMs(), Is.EqualTo(10_520));

        clock.Rebase(10_520, 100_000, true, 1.0);
        time.AdvanceMilliseconds(480);

        Assert.That(clock.GetPositionMs(), Is.EqualTo(11_000));
    }

    [Test]
    public void PositionIsClampedToTrackDuration()
    {
        var clock = CreateClock(out var time);
        clock.Rebase(9_500, 10_000, true, 2.0);

        time.AdvanceMilliseconds(1_000);

        Assert.That(clock.GetPositionMs(), Is.EqualTo(10_000));
    }

    [Test]
    public void NegativeAuthoritativePositionIsClampedToZero()
    {
        var clock = CreateClock(out _);

        clock.Rebase(-500, 10_000, false, 1.0);

        Assert.That(clock.GetPositionMs(), Is.Zero);
    }

    [Test]
    public void FreezeCapturesCurrentEstimateAndStopsAdvancing()
    {
        var clock = CreateClock(out var time);
        clock.Rebase(10_000, 100_000, true, 1.0);
        time.AdvanceMilliseconds(750);

        clock.Freeze();
        time.AdvanceMilliseconds(10_000);

        Assert.That(clock.GetPositionMs(), Is.EqualTo(10_750));
    }

    private static PlaybackClock CreateClock(out FakeMonotonicTimeSource time)
    {
        time = new FakeMonotonicTimeSource();
        return new PlaybackClock(time);
    }

    private sealed class FakeMonotonicTimeSource : IMonotonicTimeSource
    {
        private long _milliseconds;

        public long GetTimestamp() => _milliseconds;

        public TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp) =>
            TimeSpan.FromMilliseconds(endingTimestamp - startingTimestamp);

        public void AdvanceMilliseconds(long milliseconds) => _milliseconds += milliseconds;
    }
}
