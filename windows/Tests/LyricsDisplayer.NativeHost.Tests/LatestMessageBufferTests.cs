using LyricsDisplayer.NativeHost;

namespace LyricsDisplayer.NativeHost.Tests;

[TestFixture]
public sealed class LatestMessageBufferTests
{
    [Test]
    public void PlaybackTrafficCannotEvictLyricsAndReplayOrdersPlaybackFirst()
    {
        var buffer = new LatestMessageBuffer("playbackSnapshot", "lyricsSnapshot");
        buffer.Publish("playbackSnapshot", "playback-1");
        buffer.Publish("lyricsSnapshot", "lyrics-2");
        for (var i = 3; i <= 100; i++) buffer.Publish("playbackSnapshot", $"playback-{i}");
        Assert.That(buffer.Drain(), Is.EqualTo(new[] { "playback-100", "lyrics-2" }));
        Assert.That(buffer.Drain(), Is.Empty);
        Assert.That(buffer.Drain(replay: true), Is.EqualTo(new[] { "playback-100", "lyrics-2" }));
    }

    [Test]
    public void LyricsAreNotResentForEveryPlaybackSnapshot()
    {
        var buffer = new LatestMessageBuffer("playbackSnapshot", "lyricsSnapshot");
        buffer.Publish("lyricsSnapshot", "lyrics");
        buffer.Drain();
        buffer.Publish("playbackSnapshot", "playback");
        Assert.That(buffer.Drain(), Is.EqualTo(new[] { "playback" }));
    }
}
