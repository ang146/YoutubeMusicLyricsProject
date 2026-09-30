using System.Threading;

namespace LyricsDisplayer.Core.Logging;

public sealed record FatalExceptionHandlingResult(
    bool IsPrimaryIncident,
    CrashReportDocument? Report,
    CrashReportWriteResult? WriteResult,
    string? ReentrantFallbackPath)
{
    public bool WasAlreadyHandling => !IsPrimaryIncident;
}

/// <summary>Coordinates one fatal incident without depending on WPF or attempting application recovery.</summary>
public sealed class FatalExceptionCoordinator
{
    private readonly CrashReportService _reports;
    private readonly Action<string, string, string> _log;
    private readonly Action<string, string, string> _fatalLog;
    private int _handling;
    private int _completed;

    public FatalExceptionCoordinator(CrashReportService reports, Action<string, string, string> log,
        Action<string, string, string>? fatalLog = null)
    {
        _reports = reports ?? throw new ArgumentNullException(nameof(reports));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _fatalLog = fatalLog ?? log;
    }

    public FatalExceptionHandlingResult ReportFatal(Exception exception, string fatalSource,
        bool? isTerminating = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (Interlocked.CompareExchange(ref _handling, 1, 0) != 0)
        {
            if (Volatile.Read(ref _completed) != 0)
                return new(false, null, null, null);

            var fallback = TryMinimalFallback(exception, fatalSource,
                "A second fatal exception arrived while the primary report was being written.");
            return new(false, null, null, fallback);
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
                return new(true, null, null, fallback);
            }

            Exception? loggerFailure = null;
            try
            {
                _fatalLog("Fatal", "Application", FormatFatalLog(report));
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
            return new(true, report, writeResult, null);
        }
        catch (Exception reporterFailure)
        {
            TryFatalLog(exception, fatalSource, report, reporterFailure);
            var fallback = TryMinimalFallback(exception, fatalSource,
                $"Fatal reporter failed: {SafeException(reporterFailure)}", report?.CrashReportId);
            return new(true, report, writeResult, fallback);
        }
        finally
        {
            Volatile.Write(ref _completed, 1);
        }
    }

    /// <summary>
    /// Handles a Dispatcher fatal incident as one lifecycle: only the primary incident may notify
    /// the user or start shutdown. Notification failure never prevents the one shutdown request.
    /// </summary>
    public FatalExceptionHandlingResult HandleDispatcherFatal(Exception exception, string fatalSource,
        Action<FatalExceptionHandlingResult> notifyUser, Action shutdown)
    {
        ArgumentNullException.ThrowIfNull(notifyUser);
        ArgumentNullException.ThrowIfNull(shutdown);

        var result = ReportFatal(exception, fatalSource, isTerminating: false);
        if (!result.IsPrimaryIncident) return result;

        try
        {
            notifyUser(result);
        }
        catch (Exception)
        {
            // The report is already captured; failed UI must not trigger another notification attempt.
        }
        finally
        {
            try { shutdown(); }
            catch (Exception) { }
        }

        return result;
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
            _fatalLog("Fatal", "Application",
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
            _fatalLog("Fatal", "Application", message);
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
        $"Fatal unhandled exception.{Environment.NewLine}" +
        $"Crash Report ID: {report.CrashReportId}{Environment.NewLine}" +
        $"Fatal source: {report.FatalSource}{Environment.NewLine}" +
        $"{report.ExceptionDetails}{Environment.NewLine}" +
        $"Crash report path: {report.Path}";

    private static string SafeException(Exception exception)
    {
        try { return exception.ToString(); }
        catch (Exception) { return exception.GetType().FullName ?? exception.GetType().Name; }
    }
}
