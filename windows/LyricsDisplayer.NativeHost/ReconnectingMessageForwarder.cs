using System.IO.Pipes;
using System.Text;
using System.Threading.Channels;
using LyricsDisplayer.Core.Logging;
using Microsoft.Extensions.Logging;

namespace LyricsDisplayer.NativeHost;

public interface IMessageConnection : IAsyncDisposable
{
    Task WriteLineAsync(string message, CancellationToken cancellationToken);
}

public interface IMessageConnectionFactory
{
    Task<IMessageConnection> ConnectAsync(CancellationToken cancellationToken);
}

public interface IAsyncDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class SystemAsyncDelay : IAsyncDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}

public sealed class ReconnectingMessageForwarder(
    IMessageConnectionFactory connectionFactory,
    IAsyncDelay delay,
    TimeSpan retryDelay,
    ILogger<ReconnectingMessageForwarder> logger,
    LatestMessageBuffer? retainedMessages = null)
{
    public async Task RunAsync(ChannelReader<string> messages, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            IMessageConnection? connection = null;
            try
            {
                logger.LogInformation("Attempting to connect to the application pipe.");
                connection = await connectionFactory.ConnectAsync(cancellationToken);
                logger.LogInformation("Connected to LyricsDisplayer.NativeHost.v1.");

                if (retainedMessages is not null)
                {
                    foreach (var retained in retainedMessages.Drain(replay: true))
                        await connection.WriteLineAsync(retained, cancellationToken);
                }

                while (await messages.WaitToReadAsync(cancellationToken))
                {
                    while (messages.TryRead(out var message))
                    {
                        foreach (var pending in retainedMessages?.Drain() ?? [message])
                            await connection.WriteLineAsync(pending, cancellationToken);
                    }
                }

                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation("Connection or retry processing cancelled.");
                return;
            }
            catch (Exception exception)
            {
                var wasConnected = connection is not null;
                logger.LogWarning(exception, wasConnected
                    ? "Pipe connection was lost."
                    : "Connection attempt failed.");
            }
            finally
            {
                if (connection is not null)
                {
                    try
                    {
                        await connection.DisposeAsync();
                    }
                    catch (Exception exception)
                    {
                        logger.LogWarning(exception, "Error while closing pipe connection.");
                    }
                }
            }

            logger.LogInformation("Retrying in approximately {RetryDelaySeconds:0.#} seconds.", retryDelay.TotalSeconds);
            try
            {
                await delay.DelayAsync(retryDelay, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation("Retry delay cancelled.");
                return;
            }
        }
    }
}

public sealed class NamedPipeConnectionFactory(string pipeName, TimeSpan connectionTimeout) : IMessageConnectionFactory
{
    public async Task<IMessageConnection> ConnectAsync(CancellationToken cancellationToken)
    {
        var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.Out,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough);
        try
        {
            await pipe.ConnectAsync((int)connectionTimeout.TotalMilliseconds, cancellationToken);
            return new NamedPipeMessageConnection(pipe);
        }
        catch
        {
            await pipe.DisposeAsync();
            throw;
        }
    }
}

internal sealed class NamedPipeMessageConnection : IMessageConnection
{
    private readonly NamedPipeClientStream _pipe;
    private readonly StreamWriter _writer;

    public NamedPipeMessageConnection(NamedPipeClientStream pipe)
    {
        _pipe = pipe;
        _writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true)
        {
            AutoFlush = true,
            NewLine = "\n"
        };
    }

    public Task WriteLineAsync(string message, CancellationToken cancellationToken) =>
        _writer.WriteLineAsync(message.AsMemory(), cancellationToken);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _writer.DisposeAsync();
        }
        finally
        {
            await _pipe.DisposeAsync();
        }
    }
}
