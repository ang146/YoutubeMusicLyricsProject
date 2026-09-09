using System.IO;
using System.IO.Pipes;
using System.Text;
using LyricsDisplayer.Core.Logging;
using LyricsDisplayer.Core.Protocol;

namespace LyricsDisplayer;

public sealed class NamedPipeServer(SessionFileLogger logger, PlaybackStateCoordinator playbackState)
{
    public event Action<string>? ConnectionStatusChanged;
    public event Action<PlaybackSnapshotMessage>? SnapshotAccepted;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        logger.Write("Information", "NamedPipe", $"Named Pipe server started: {ProtocolConstants.PipeName}.");
        ConnectionStatusChanged?.Invoke("Waiting for NativeHost");

        while (!cancellationToken.IsCancellationRequested)
        {
            var connected = false;
            await using var pipe = new NamedPipeServerStream(
                ProtocolConstants.PipeName,
                PipeDirection.In,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken);
                connected = true;
                logger.Write("Information", "NamedPipe", "NativeHost connected.");
                ConnectionStatusChanged?.Invoke("Connected");

                using var reader = new StreamReader(pipe, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false,
                    leaveOpen: true);
                while (!cancellationToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(cancellationToken);
                    if (line is null)
                    {
                        break;
                    }

                    ProcessLine(line);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (exception is IOException or DecoderFallbackException)
            {
                logger.Write("Warning", "NamedPipe", $"Pipe receive error: {exception.Message}");
            }
            finally
            {
                if (connected && !cancellationToken.IsCancellationRequested)
                {
                    playbackState.SourceDisconnected();
                    logger.Write("Information", "NamedPipe",
                        "NativeHost disconnected; local playback clock frozen.");
                    ConnectionStatusChanged?.Invoke("Disconnected; waiting for NativeHost");
                }
            }
        }
    }

    private void ProcessLine(string json)
    {
        if (!ProtocolSerializer.TryParse(json, out var message, out var error))
        {
            logger.Write("Warning", "Protocol", $"Rejected pipe message: {error}");
            return;
        }

        if (message is not PlaybackSnapshotMessage snapshot)
        {
            logger.Write("Warning", "Protocol", $"Unexpected pipe message type '{message!.Envelope.MessageType}'.");
            return;
        }

        var decision = playbackState.Apply(snapshot);
        if (decision is SnapshotDecision.RejectedDuplicate or SnapshotDecision.RejectedStale)
        {
            logger.Write("Warning", "Sequence",
                $"Rejected {decision.ToString().Replace("Rejected", string.Empty).ToLowerInvariant()} snapshot " +
                $"for session {snapshot.Envelope.SourceSessionId}, sequence {snapshot.Envelope.Sequence}.");
            return;
        }

        SnapshotAccepted?.Invoke(snapshot);
    }
}
