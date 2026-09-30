using System.Threading;

namespace LyricsDisplayer.Core.Logging;

public sealed record FatalExceptionHandlingResult(
    bool WasAlreadyHandling,
    CrashReportDocument? Report,
    CrashReportWriteResult? WriteResult,
    string? ReentrantFallbackPath);

/// <summary>Coordinates one fatal incident without depending on WPF or attempting application recovery.</summary>
public sealed class FatalExceptionCoordinator
{
    private readonly CrashReportService _reports;
    private readonly Action<string, string, string> _log;
    private int _handling;
    private int _completed;

    public FatalExceptionCoordinator(CrashReportService reports, Action<string, string, string> log)
    {
        _reports = reports ?? throw new ArgumentNullException(nameof(reports));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public FatalExceptionHandlingResult ReportFatal(Exception exception, string fatalSource,
        bool? isTerminating = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (Interlocked.CompareExchange(ref _handling, 1, 0) != 0)
        {
            if (Volatile.Read(ref _completed) != 0)
                return new(true, null, null, null);

            var fallback = TryMinimalFallback(exception, fatalSource,
                "A second fatal exception arrived while the primary report was being written.");
            return new(true, null, null, fallback);
        }

        CrashReportDocument? report = null;
        CrashReportWriteResult? writeResult = null;
        try
        {
            try
            {
                report = _reports.Prepare(exception, fatalSource, isTerminating);
            }
            catch (Exception preparationFailure)
            {
                TryFatalLog(exception, fatalSource, null, preparationFailure);
                var fallback = TryMinimalFallback(exception, fatalSource,
                    $"Crash-report formatting failed: {SafeException(preparationFailure)}");
                return new(false, null, null, fallback);
            }

            Exception? loggerFailure = null;
            try
            {
                _log("Fatal", "Application", FormatFatalLog(report));
            }
            catch (Exception failure)
            {
                loggerFailure = failure;
            }

            writeResult = _reports.Write(report, loggerFailure);
            if (writeResult.PrimaryWriteFailure is { } reportFailure)
            {
                var fallbackPath = writeResult.WrittenPath;
                TryWriteFailureLog(report, reportFailure, fallbackPath,
                    writeResult.FallbackWriteFailure ?? writeResult.TemporaryFallbackWriteFailure);
            }
            return new(false, report, writeResult, null);
        }
        catch (Exception reporterFailure)
        {
            TryFatalLog(exception, fatalSource, report, reporterFailure);
            var fallback = TryMinimalFallback(exception, fatalSource,
                $"Fatal reporter failed: {SafeException(reporterFailure)}", report?.CrashReportId);
            return new(false, report, writeResult, fallback);
        }
        finally
        {
            Volatile.Write(ref _completed, 1);
        }
    }

    /// <summary>Unobserved task exceptions are recorded as Error and never initiate fatal shutdown.</summary>
    public void ReportUnobservedTaskException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        try
        {
            _log("Error", "TaskScheduler",
                $"Unobserved task exception; this event is not classified as fatal and does not request shutdown.{Environment.NewLine}" +
                CrashReportService.FormatExceptionDetails(exception));
        }
        catch (Exception)
        {
            // This non-fatal diagnostic must not promote an unobserved task exception into a process failure.
        }
    }

    private void TryFatalLog(Exception exception, string fatalSource, CrashReportDocument? report,
        Exception reporterFailure)
    {
        try
        {
            var id = report?.CrashReportId ?? "unavailable";
            var path = report?.Path ?? "unavailable";
            _log("Fatal", "Application",
                $"Fatal exception reporting failure. Crash Report ID: {id}. Fatal source: {fatalSource}. " +
                $"Original exception: {SafeException(exception)}. Reporter failure: {SafeException(reporterFailure)}. " +
                $"Crash report path: {path}");
        }
        catch (Exception)
        {
            // Reporting is best effort; the independent crash writer is attempted by the caller.
        }
    }

    private void TryWriteFailureLog(CrashReportDocument report, Exception primaryFailure, string? fallbackPath,
        Exception? fallbackFailure)
    {
        try
        {
            var message = $"Standalone crash report could not be written at '{report.Path}': {SafeException(primaryFailure)}. " +
                $"Fallback path: {fallbackPath ?? "unavailable"}.";
            if (fallbackFailure is not null) message += $" Fallback write also failed: {SafeException(fallbackFailure)}.";
            _log("Fatal", "Application", message);
        }
        catch (Exception)
        {
            // Do not let a secondary logger failure prevent termination.
        }
    }

    private string? TryMinimalFallback(Exception exception, string fatalSource, string reason, string? reportId = null)
    {
        try
        {
            return _reports.WriteMinimalFallback(exception, fatalSource, reason, reportId).WrittenPath;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string FormatFatalLog(CrashReportDocument report) =>
        $"Fatal unhandled exception. Crash Report ID: {report.CrashReportId}. " +
        $"Fatal source: {report.FatalSource}. Exception details:{Environment.NewLine}{report.ExceptionDetails}" +
        $"Crash report path: {report.Path}";

    private static string SafeException(Exception exception)
    {
        try { return exception.ToString(); }
        catch (Exception) { return exception.GetType().FullName ?? exception.GetType().Name; }
    }
}
