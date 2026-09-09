using System.IO.Pipes;
using System.Text;
using System.Threading.Channels;
using LyricsDisplayer.Core.Logging;

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
    SessionFileLogger logger)
{
    public async Task RunAsync(ChannelReader<string> messages, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            IMessageConnection? connection = null;
            try
            {
                logger.Write("Information", "NamedPipe", "Attempting to connect to the application pipe.");
                connection = await connectionFactory.ConnectAsync(cancellationToken);
                logger.Write("Information", "NamedPipe", "Connected to LyricsDisplayer.NativeHost.v1.");

                while (await messages.WaitToReadAsync(cancellationToken))
                {
                    while (messages.TryRead(out var message))
                    {
                        await connection.WriteLineAsync(message, cancellationToken);
                    }
                }

                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                logger.Write("Information", "NamedPipe", "Connection or retry processing cancelled.");
                return;
            }
            catch (Exception exception)
            {
                var wasConnected = connection is not null;
                logger.Write("Warning", "NamedPipe", wasConnected
                    ? $"Pipe connection was lost: {exception.Message}"
                    : $"Connection attempt failed: {exception.Message}");
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
                        logger.Write("Warning", "NamedPipe", $"Error while closing pipe connection: {exception.Message}");
                    }
                }
            }

            logger.Write("Information", "NamedPipe", $"Retrying in approximately {retryDelay.TotalSeconds:0.#} seconds.");
            try
            {
                await delay.DelayAsync(retryDelay, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                logger.Write("Information", "NamedPipe", "Retry delay cancelled.");
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
