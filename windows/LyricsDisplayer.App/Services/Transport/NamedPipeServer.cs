using System.IO;
using System.IO.Pipes;
using System.Text;
using LyricsDisplayer.Core.Protocol;
using LyricsDisplayer.Resources;
using Microsoft.Extensions.Logging;

namespace LyricsDisplayer;

public sealed class NamedPipeServer(ILogger<NamedPipeServer> logger, PlaybackStateCoordinator playbackState)
{
    public event Action<string>? ConnectionStatusChanged;
    public event Action<PlaybackSnapshotMessage>? SnapshotAccepted;
    public event Action? LyricsChanged;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Named Pipe server started: {PipeName}.", ProtocolConstants.PipeName);
        ConnectionStatusChanged?.Invoke(Strings.TransportWaitingForNativeHost);

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
                logger.LogInformation("NativeHost connected.");
                ConnectionStatusChanged?.Invoke(Strings.TransportConnected);

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
                logger.LogWarning(exception, "Pipe receive error.");
            }
            finally
            {
                if (connected && !cancellationToken.IsCancellationRequested)
                {
                    playbackState.SourceDisconnected();
                    logger.LogInformation("NativeHost disconnected; local playback clock frozen.");
                    ConnectionStatusChanged?.Invoke(Strings.TransportDisconnectedWaitingForNativeHost);
                }
            }
        }
    }

    private void ProcessLine(string json)
    {
        if (!ProtocolSerializer.TryParse(json, out var message, out var error))
        {
            logger.LogWarning("Rejected pipe message: {Error}", error);
            return;
        }

        if (message is LyricsSnapshotMessage lyrics)
        {
            var lyricsDecision = playbackState.ApplyLyricsDetailed(lyrics);
            if (lyricsDecision == LyricsApplyDecision.Rejected)
                logger.LogWarning("Ignored lyrics for a non-current track/session or stale sequence.");
            else
                LyricsChanged?.Invoke();
            return;
        }

        if (message is not PlaybackSnapshotMessage snapshot)
        {
            logger.LogWarning("Unexpected pipe message type '{MessageType}'.", message!.Envelope.MessageType);
            return;
        }

        var previousLyrics = playbackState.CurrentLyrics;
        var decision = playbackState.Apply(snapshot);
        if (decision is SnapshotDecision.RejectedDuplicate or SnapshotDecision.RejectedStale)
        {
            logger.LogWarning("Rejected {Decision} snapshot for session {SessionId}, sequence {Sequence}.",
                decision.ToString().Replace("Rejected", string.Empty).ToLowerInvariant(),
                snapshot.Envelope.SourceSessionId, snapshot.Envelope.Sequence);
            return;
        }

        SnapshotAccepted?.Invoke(snapshot);
        if (previousLyrics != playbackState.CurrentLyrics) LyricsChanged?.Invoke();
    }
}
