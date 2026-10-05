using Microsoft.Extensions.Logging;

namespace LyricsDisplayer.App.Tests;

internal sealed record RecordedLogEntry(LogLevel Level, string Message, Exception? Exception);

internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<RecordedLogEntry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Entries.Add(new(logLevel, formatter(state, exception), exception));
}
