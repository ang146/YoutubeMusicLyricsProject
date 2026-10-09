using LyricsDisplayer.Core.Logging;
using Microsoft.Extensions.Logging;

namespace LyricsDisplayer.Core.Logging;

/// <summary>Adapts the existing rotating application log to the standard logging abstractions.</summary>
public sealed class SessionFileLoggerProvider(SessionFileLogger fileLogger) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new SessionLogger(fileLogger, categoryName);

    // SessionFileLogger is owned by the application composition root, not by LoggerFactory.
    public void Dispose() { }

    private sealed class SessionLogger(SessionFileLogger fileLogger, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel is >= LogLevel.Debug and < LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            ArgumentNullException.ThrowIfNull(formatter);

            var message = formatter(state, exception);
            if (exception is not null)
                fileLogger.WriteMultiline(ToFileLevel(logLevel), categoryName,
                    $"{message}{Environment.NewLine}{exception}");
            else
                fileLogger.Write(ToFileLevel(logLevel), categoryName, message);
        }

        private static string ToFileLevel(LogLevel logLevel) => logLevel switch
        {
            LogLevel.Trace => "Trace",
            LogLevel.Debug => "Debug",
            LogLevel.Information => "Information",
            LogLevel.Warning => "Warning",
            LogLevel.Error => "Error",
            LogLevel.Critical => "Critical",
            _ => throw new ArgumentOutOfRangeException(nameof(logLevel), logLevel, null)
        };
    }
}
