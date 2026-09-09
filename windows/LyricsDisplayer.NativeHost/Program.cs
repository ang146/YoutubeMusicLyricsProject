using System.Threading.Channels;
using LyricsDisplayer.Core.Logging;
using LyricsDisplayer.Core.Protocol;
using LyricsDisplayer.NativeHost;

var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
var logsRoot = Path.Combine(localAppData, "LyricsDisplayer", "Logs");
using var hostLogger = new SessionFileLogger(Path.Combine(logsRoot, "NativeHost"));
using var extensionLogger = new SessionFileLogger(Path.Combine(logsRoot, "FirefoxExtension"));
using var shutdown = new CancellationTokenSource();

hostLogger.Write("Information", "Process", "LyricsDisplayer.NativeHost started.");
hostLogger.Write("Information", "NativeMessaging", "Firefox Native Messaging input session started.");

var messages = Channel.CreateBounded<string>(new BoundedChannelOptions(1)
{
    FullMode = BoundedChannelFullMode.DropOldest,
    SingleReader = true,
    SingleWriter = true
});

var forwarder = new ReconnectingMessageForwarder(
    new NamedPipeConnectionFactory(ProtocolConstants.PipeName, TimeSpan.FromSeconds(1)),
    new SystemAsyncDelay(),
    TimeSpan.FromSeconds(5),
    hostLogger);
var forwardingTask = forwarder.RunAsync(messages.Reader, shutdown.Token);

try
{
    var reader = new NativeMessagingReader(Console.OpenStandardInput());
    while (true)
    {
        string? json;
        try
        {
            json = await reader.ReadMessageAsync(shutdown.Token);
        }
        catch (InvalidDataException exception)
        {
            hostLogger.Write("Error", "NativeMessaging", exception.Message);
            break;
        }

        if (json is null)
        {
            hostLogger.Write("Information", "NativeMessaging", "Firefox input reached EOF or disconnected.");
            break;
        }

        if (!ProtocolSerializer.TryParse(json, out var message, out var error))
        {
            hostLogger.Write("Warning", "Protocol", $"Rejected message: {error}");
            continue;
        }

        if (message is DiagnosticLogMessage diagnostic)
        {
            extensionLogger.Write(diagnostic.Payload.Level, diagnostic.Payload.Category, diagnostic.Payload.Message);
            continue;
        }

        if (message is PlaybackSnapshotMessage snapshot)
        {
            await messages.Writer.WriteAsync(ProtocolSerializer.Serialize(snapshot), shutdown.Token);
        }
    }
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
{
    hostLogger.Write("Information", "NativeMessaging", "Native Messaging processing was cancelled.");
}
catch (Exception exception)
{
    hostLogger.Write("Error", "NativeMessaging", $"Fatal input error: {exception}");
}
finally
{
    messages.Writer.TryComplete();
    shutdown.Cancel();
    hostLogger.Write("Information", "NamedPipe", "Cancelling outstanding connection/retry work.");
    try
    {
        await forwardingTask;
    }
    catch (OperationCanceledException)
    {
        // Expected while Firefox shutdown cancels a pending connection or delay.
    }

    hostLogger.Write("Information", "Process", "LyricsDisplayer.NativeHost shut down gracefully.");
}
