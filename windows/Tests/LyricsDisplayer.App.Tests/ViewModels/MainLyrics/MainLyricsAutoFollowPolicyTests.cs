namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class MainLyricsAutoFollowPolicyTests
{
    [Test]
    public void FollowStartsEnabledAndManualScrollingSuspendsCurrentLineCentering()
    {
        var policy = new MainLyricsAutoFollowPolicy();
        var now = DateTimeOffset.Parse("2026-10-05T10:00:00Z");

        Assert.That(policy.ShouldCenterCurrentLine(hasCurrentLine: true), Is.True);

        policy.NotifyManualScroll(now);

        Assert.Multiple(() =>
        {
            Assert.That(policy.IsManualScrollOverrideActive, Is.True);
            Assert.That(policy.ShouldCenterCurrentLine(hasCurrentLine: true), Is.False,
                "Current-line changes must not pull the viewport while the user browses.");
            Assert.That(policy.ShouldCenterCurrentLine(hasCurrentLine: false), Is.False);
        });
    }

    [Test]
    public void ManualScrollRestartsInactivityDeadlineAndResumesAtTheLatestDeadline()
    {
        var policy = new MainLyricsAutoFollowPolicy();
        var start = DateTimeOffset.Parse("2026-10-05T10:00:00Z");
        policy.NotifyManualScroll(start);

        var secondScroll = start.AddSeconds(2);
        policy.NotifyManualScroll(secondScroll);

        Assert.Multiple(() =>
        {
            Assert.That(policy.GetRemainingResumeDelay(start.AddSeconds(6)), Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(policy.TryResume(start.AddSeconds(6.999)), Is.False,
                "The first scroll's deadline must not resume follow after a later scroll restarted it.");
            Assert.That(policy.IsManualScrollOverrideActive, Is.True);
        });

        Assert.That(policy.TryResume(start.AddSeconds(7)), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(policy.IsManualScrollOverrideActive, Is.False);
            Assert.That(policy.ShouldCenterCurrentLine(hasCurrentLine: true), Is.True);
        });
    }

    [Test]
    public void TrackChangeResetClearsManualOverrideImmediately()
    {
        var policy = new MainLyricsAutoFollowPolicy();
        policy.NotifyManualScroll(DateTimeOffset.Parse("2026-10-05T10:00:00Z"));

        policy.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(policy.IsManualScrollOverrideActive, Is.False);
            Assert.That(policy.ShouldCenterCurrentLine(hasCurrentLine: true), Is.True);
            Assert.That(policy.TryResume(DateTimeOffset.Parse("2026-10-05T10:00:01Z")), Is.False);
        });
    }

    [Test]
    public void ProgrammaticScrollDoesNotStartManualOverride()
    {
        var policy = new MainLyricsAutoFollowPolicy();
        var accepted = policy.NotifyManualScroll(
            DateTimeOffset.Parse("2026-10-05T10:00:00Z"), isProgrammatic: true);

        Assert.Multiple(() =>
        {
            Assert.That(accepted, Is.False);
            Assert.That(policy.IsManualScrollOverrideActive, Is.False);
            Assert.That(policy.ShouldCenterCurrentLine(hasCurrentLine: true), Is.True);
        });
    }

    [Test]
    public void TimeoutCanResumeEvenWhenPlaybackIsPausedAndNoLineDoesNotRequestCentering()
    {
        var policy = new MainLyricsAutoFollowPolicy();
        var start = DateTimeOffset.Parse("2026-10-05T10:00:00Z");
        policy.NotifyManualScroll(start);

        Assert.That(policy.TryResume(start.Add(MainLyricsAutoFollowPolicy.ManualScrollResumeDelay)), Is.True);
        Assert.That(policy.ShouldCenterCurrentLine(hasCurrentLine: false), Is.False,
            "Untimed, pending, and unavailable documents have no current row to center.");
    }
}
