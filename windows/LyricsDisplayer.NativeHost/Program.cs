using LyricsDisplayer.Core.Logging;
using LyricsDisplayer.Core.Protocol;
using LyricsDisplayer.NativeHost;
using Microsoft.Extensions.Logging;

var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
var logsRoot = Path.Combine(localAppData, "LyricsDisplayer", "Logs");
using var hostFileLogger = new SessionFileLogger(Path.Combine(logsRoot, "NativeHost"));
using var extensionFileLogger = new SessionFileLogger(Path.Combine(logsRoot, "FirefoxExtension"));
using var hostLoggerFactory = LoggerFactory.Create(builder =>
{
    builder.SetMinimumLevel(LogLevel.Debug);
    builder.AddProvider(new SessionFileLoggerProvider(hostFileLogger));
});
using var extensionLoggerFactory = LoggerFactory.Create(builder =>
{
    builder.SetMinimumLevel(LogLevel.Trace);
    builder.AddProvider(new SessionFileLoggerProvider(extensionFileLogger));
});
var processLogger = hostLoggerFactory.CreateLogger("Process");
var nativeMessagingLogger = hostLoggerFactory.CreateLogger("NativeMessaging");
var protocolLogger = hostLoggerFactory.CreateLogger("Protocol");
using var shutdown = new CancellationTokenSource();

processLogger.LogInformation("LyricsDisplayer.NativeHost started.");
nativeMessagingLogger.LogInformation("Firefox Native Messaging input session started.");

var messages = new LatestMessageBuffer(ProtocolConstants.PlaybackSnapshot, ProtocolConstants.LyricsSnapshot);

var forwarder = new ReconnectingMessageForwarder(
    new NamedPipeConnectionFactory(ProtocolConstants.PipeName, TimeSpan.FromSeconds(1)),
    new SystemAsyncDelay(),
    TimeSpan.FromSeconds(5),
    hostLoggerFactory.CreateLogger<ReconnectingMessageForwarder>(), messages);
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
            nativeMessagingLogger.LogError(exception, "Invalid Native Messaging input.");
            break;
        }

        if (json is null)
        {
            nativeMessagingLogger.LogInformation("Firefox input reached EOF or disconnected.");
            break;
        }

        if (!ProtocolSerializer.TryParse(json, out var message, out var error))
        {
            protocolLogger.LogWarning("Rejected message: {Error}", error);
            continue;
        }

        if (message is DiagnosticLogMessage diagnostic)
        {
            var level = Enum.TryParse<LogLevel>(diagnostic.Payload.Level, ignoreCase: true, out var parsedLevel) &&
                        parsedLevel is not LogLevel.None
                ? parsedLevel
                : LogLevel.Information;
            extensionLoggerFactory.CreateLogger(diagnostic.Payload.Category)
                .Log(level, "{Message}", diagnostic.Payload.Message);
            continue;
        }

        if (message is PlaybackSnapshotMessage snapshot)
        {
            messages.Publish(ProtocolConstants.PlaybackSnapshot, ProtocolSerializer.Serialize(snapshot));
        }
        else if (message is LyricsSnapshotMessage lyrics)
        {
            messages.Publish(ProtocolConstants.LyricsSnapshot, ProtocolSerializer.Serialize(lyrics));
        }
    }
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
{
    nativeMessagingLogger.LogInformation("Native Messaging processing was cancelled.");
}
catch (Exception exception)
{
    nativeMessagingLogger.LogError(exception, "Fatal input error.");
}
finally
{
    messages.Complete();
    shutdown.Cancel();
    hostLoggerFactory.CreateLogger<ReconnectingMessageForwarder>()
        .LogInformation("Cancelling outstanding connection/retry work.");
    try
    {
        await forwardingTask;
    }
    catch (OperationCanceledException)
    {
        // Expected while Firefox shutdown cancels a pending connection or delay.
    }

    processLogger.LogInformation("LyricsDisplayer.NativeHost shut down gracefully.");
}
