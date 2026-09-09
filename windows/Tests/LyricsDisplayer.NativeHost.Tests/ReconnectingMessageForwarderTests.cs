using System.Threading.Channels;
using LyricsDisplayer.Core.Logging;

namespace LyricsDisplayer.NativeHost.Tests;

[TestFixture]
public sealed class ReconnectingMessageForwarderTests
{
    private string _directory = null!;
    private SessionFileLogger _logger = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "LyricsDisplayerTests", Guid.NewGuid().ToString("N"));
        _logger = new SessionFileLogger(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        _logger.Dispose();
        Directory.Delete(_directory, true);
    }

    [Test]
    public async Task FailuresRetryUntilAConnectionSucceedsWithoutRealDelay()
    {
        var factory = new CountingFactory(failuresBeforeSuccess: 3);
        var delay = new ImmediateDelay();
        var channel = Channel.CreateUnbounded<string>();
        channel.Writer.TryComplete();
        var forwarder = new ReconnectingMessageForwarder(factory, delay, TimeSpan.FromSeconds(5), _logger);

        await forwarder.RunAsync(channel.Reader, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(factory.Attempts, Is.EqualTo(4));
            Assert.That(delay.Calls, Is.EqualTo(3));
            Assert.That(delay.RequestedDelays, Is.All.EqualTo(TimeSpan.FromSeconds(5)));
        });
    }

    [Test]
    public async Task CancellationStopsIndefiniteRetryProcessing()
    {
        var factory = new CountingFactory(int.MaxValue);
        var delay = new CancelOnlyDelay();
        var channel = Channel.CreateUnbounded<string>();
        var forwarder = new ReconnectingMessageForwarder(factory, delay, TimeSpan.FromSeconds(5), _logger);
        using var cancellation = new CancellationTokenSource();

        var run = forwarder.RunAsync(channel.Reader, cancellation.Token);
        await delay.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cancellation.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.That(factory.Attempts, Is.EqualTo(1));
    }

    [Test]
    public async Task CancellationStopsAPendingConnectionAttempt()
    {
        var factory = new PendingFactory();
        var channel = Channel.CreateUnbounded<string>();
        var forwarder = new ReconnectingMessageForwarder(factory, new ImmediateDelay(), TimeSpan.FromSeconds(5), _logger);
        using var cancellation = new CancellationTokenSource();

        var run = forwarder.RunAsync(channel.Reader, cancellation.Token);
        await factory.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cancellation.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.That(factory.Attempts, Is.EqualTo(1));
    }

    [Test]
    public async Task WriteDisconnectReturnsToReconnectState()
    {
        var factory = new DisconnectThenSuccessFactory();
        var channel = Channel.CreateUnbounded<string>();
        await channel.Writer.WriteAsync("first");
        channel.Writer.TryComplete();
        var forwarder = new ReconnectingMessageForwarder(factory, new ImmediateDelay(), TimeSpan.FromSeconds(5), _logger);

        await forwarder.RunAsync(channel.Reader, CancellationToken.None);

        Assert.That(factory.Attempts, Is.EqualTo(2));
    }

    private sealed class CountingFactory(int failuresBeforeSuccess) : IMessageConnectionFactory
    {
        public int Attempts { get; private set; }

        public Task<IMessageConnection> ConnectAsync(CancellationToken cancellationToken)
        {
            Attempts++;
            if (Attempts <= failuresBeforeSuccess)
            {
                throw new IOException("not available");
            }

            return Task.FromResult<IMessageConnection>(new SuccessfulConnection());
        }
    }

    private sealed class DisconnectThenSuccessFactory : IMessageConnectionFactory
    {
        public int Attempts { get; private set; }

        public Task<IMessageConnection> ConnectAsync(CancellationToken cancellationToken)
        {
            Attempts++;
            return Task.FromResult<IMessageConnection>(Attempts == 1
                ? new DisconnectingConnection()
                : new SuccessfulConnection());
        }
    }

    private sealed class PendingFactory : IMessageConnectionFactory
    {
        public int Attempts { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IMessageConnection> ConnectAsync(CancellationToken cancellationToken)
        {
            Attempts++;
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable after cancellation.");
        }
    }

    private sealed class SuccessfulConnection : IMessageConnection
    {
        public Task WriteLineAsync(string message, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class DisconnectingConnection : IMessageConnection
    {
        public Task WriteLineAsync(string message, CancellationToken cancellationToken) =>
            throw new IOException("pipe lost");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ImmediateDelay : IAsyncDelay
    {
        public int Calls { get; private set; }
        public List<TimeSpan> RequestedDelays { get; } = [];

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Calls++;
            RequestedDelays.Add(delay);
            return Task.CompletedTask;
        }
    }

    private sealed class CancelOnlyDelay : IAsyncDelay
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}
