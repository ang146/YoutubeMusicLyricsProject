using LyricsDisplayer.Core.Logging;

namespace LyricsDisplayer.NativeHost.Tests;

[TestFixture]
public sealed class RetainedForwardingTests
{
    [Test]
    public async Task DisconnectDuringLyricsWriteReplaysBothMessagesOnNextConnection()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LyricsDisplayerTests", Guid.NewGuid().ToString("N"));
        try
        {
            using var logger = new SessionFileLogger(directory);
            var buffer = new LatestMessageBuffer("playbackSnapshot", "lyricsSnapshot");
            buffer.Publish("playbackSnapshot", "current playback");
            buffer.Publish("lyricsSnapshot", "current lyrics");
            buffer.Complete();
            var factory = new Factory();
            var forwarder = new ReconnectingMessageForwarder(factory, new ImmediateDelay(),
                TimeSpan.FromSeconds(5), logger, buffer);
            await forwarder.RunAsync(buffer.Reader, CancellationToken.None);
            Assert.That(factory.Attempts, Is.EqualTo(2));
            Assert.That(factory.Delivered, Is.EqualTo(new[] { "current playback", "current lyrics" }));
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class Factory : IMessageConnectionFactory
    {
        public int Attempts;
        public List<string> Delivered { get; } = [];
        public Task<IMessageConnection> ConnectAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IMessageConnection>(new Connection(++Attempts == 1, Delivered));
    }

    private sealed class Connection(bool failLyrics, List<string> delivered) : IMessageConnection
    {
        public Task WriteLineAsync(string message, CancellationToken cancellationToken)
        {
            if (failLyrics && message == "current lyrics") throw new IOException("Test disconnect");
            if (!failLyrics) delivered.Add(message);
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ImmediateDelay : IAsyncDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
