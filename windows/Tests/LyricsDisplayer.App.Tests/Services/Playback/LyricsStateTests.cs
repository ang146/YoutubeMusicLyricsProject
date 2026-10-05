using LyricsDisplayer.Core.Playback;
using LyricsDisplayer.Core.Protocol;

namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class LyricsStateTests
{
    private static PlaybackStateCoordinator State() => new(NullLogger<PlaybackStateCoordinator>.Instance,
        new SnapshotStateTracker(), new PlaybackClock());
    private static EnvelopeMetadata Metadata(long sequence, string session = "session-a", string type = "playbackSnapshot") =>
        new(1, type, "youtubeMusic", session, sequence, DateTimeOffset.UnixEpoch);
    private static PlaybackSnapshotMessage Track(string id, long sequence, string session = "session-a") =>
        new(Metadata(sequence, session), new(new(id, id, "artist", null, 100000), new(1000, false, 1),
            new(false, false, null, [])), "{}");
    private static LyricsSnapshotMessage Lyrics(string id, long sequence, string session = "session-a",
        bool available = true, bool timed = true) =>
        new(Metadata(sequence, session, "lyricsSnapshot"), new(id, available, timed, available ? "youtubeMusic" : null,
            timed ? [new LyricsLine(100, 500, "Test")] : []), "{}");

    [Test]
    public void CurrentLyricsAcceptedWithoutRebasingClock()
    {
        var state = State();
        state.Apply(Track("a", 1));
        Assert.That(state.ApplyLyrics(Lyrics("a", 2)), Is.True);
        Assert.That(state.CurrentLyrics!.Payload.Lines[0], Is.EqualTo(new LyricsLine(100, 500, "Test")));
        Assert.That(state.GetLocalPositionMs(), Is.EqualTo(1000));
        state.Apply(Track("a", 3));
        Assert.That(state.CurrentLyrics.Payload.Timed, Is.True);
    }

    [Test]
    public void TrackChangeClearsAndRejectsLatePreviousTrack()
    {
        var state = State();
        state.Apply(Track("a", 1));
        state.ApplyLyrics(Lyrics("a", 2));
        state.Apply(Track("b", 3));
        Assert.That(state.CurrentLyrics, Is.Null);
        Assert.That(state.ApplyLyrics(Lyrics("a", 4)), Is.False);
        Assert.That(state.ApplyLyrics(Lyrics("b", 5)), Is.True);
        Assert.That(state.CurrentLyrics!.Payload.SourceTrackId, Is.EqualTo("b"));
    }

    [Test]
    public void NewSessionClearsAndRejectsOldSession()
    {
        var state = State();
        state.Apply(Track("a", 1));
        state.ApplyLyrics(Lyrics("a", 2));
        state.Apply(Track("a", 1, "session-b"));
        Assert.That(state.CurrentLyrics, Is.Null);
        Assert.That(state.ApplyLyrics(Lyrics("a", 50)), Is.False);
    }

    [TestCase(5)]
    [TestCase(4)]
    public void DuplicateOrStaleLyricsRejected(long sequence)
    {
        var state = State();
        state.Apply(Track("a", 1));
        state.ApplyLyrics(Lyrics("a", 5));
        Assert.That(state.ApplyLyrics(Lyrics("a", sequence, available: false, timed: false)), Is.False);
        Assert.That(state.CurrentLyrics!.Payload.Timed, Is.True);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void UntimedAndUnavailableStatesReplaceTimedLines(bool available)
    {
        var state = State();
        state.Apply(Track("a", 1));
        state.ApplyLyrics(Lyrics("a", 2));
        Assert.That(state.ApplyLyrics(Lyrics("a", 4, available: available, timed: false)), Is.True);
        Assert.That(state.CurrentLyrics!.Payload.Lines, Is.Empty);
        Assert.That(state.CurrentLyrics.Payload.Available, Is.EqualTo(available));
    }

    [Test]
    public void RetainedLyricsCanPredateLatestPlaybackOnInitialConnection()
    {
        var state = State();
        state.Apply(Track("a", 100));
        Assert.That(state.ApplyLyrics(Lyrics("a", 3)), Is.True);
    }

    [Test]
    public void ReturnToTrackRejectsLyricsFromEarlierVisit()
    {
        var state = State();
        state.Apply(Track("a", 1));
        state.Apply(Track("b", 10));
        state.Apply(Track("a", 20));
        Assert.That(state.ApplyLyrics(Lyrics("a", 2)), Is.False);
        Assert.That(state.ApplyLyrics(Lyrics("a", 21)), Is.True);
    }

    [Test]
    public void LyricsCannotEstablishPlaybackOrClearStateThroughRejectedPlayback()
    {
        var state = State();
        Assert.That(state.ApplyLyrics(Lyrics("a", 1)), Is.False);
        state.Apply(Track("a", 10));
        state.ApplyLyrics(Lyrics("a", 11));
        state.Apply(Track("b", 9));
        Assert.That(state.CurrentLyrics!.Payload.SourceTrackId, Is.EqualTo("a"));
    }
}
