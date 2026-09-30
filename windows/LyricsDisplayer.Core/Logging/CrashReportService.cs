using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace LyricsDisplayer.Core.Logging;

public interface ICrashReportFileWriter
{
    void WriteNew(string path, string content);
}

public sealed class CrashReportFileWriter : ICrashReportFileWriter
{
    public void WriteNew(string path, string content)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new ArgumentException("A crash-report directory is required.", nameof(path));
        Directory.CreateDirectory(directory);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }
}

public sealed record CrashReportDocument(
    string CrashReportId,
    string FatalSource,
    DateTimeOffset Timestamp,
    string Path,
    string Content,
    string ExceptionDetails);

public sealed record CrashReportWriteResult(
    CrashReportDocument Report,
    string? WrittenPath,
    bool IsFallback,
    Exception? PrimaryWriteFailure = null,
    Exception? FallbackWriteFailure = null,
    Exception? TemporaryFallbackWriteFailure = null)
{
    public bool Succeeded => WrittenPath is not null;
}

public sealed record CrashFallbackWriteResult(string? WrittenPath, Exception? CrashDirectoryFailure,
    Exception? TemporaryDirectoryFailure)
{
    public bool Succeeded => WrittenPath is not null;
}

/// <summary>Builds privacy-conscious diagnostics and writes standalone reports outside the WPF dispatcher.</summary>
public sealed class CrashReportService
{
    private const int MaximumReportCharacters = 1_000_000;
    private const int MaximumDataEntries = 50;
    private readonly string _crashDirectory;
    private readonly IClock _clock;
    private readonly ICrashReportFileWriter _writer;
    private readonly string _applicationVersion;

    public CrashReportService(string crashDirectory, IClock? clock = null,
        ICrashReportFileWriter? writer = null, string? applicationVersion = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(crashDirectory);
        _crashDirectory = crashDirectory;
        _clock = clock ?? new SystemClock();
        _writer = writer ?? new CrashReportFileWriter();
        _applicationVersion = applicationVersion ?? GetApplicationVersion();
    }

    public string CrashDirectory => _crashDirectory;

    public CrashReportDocument Prepare(Exception exception, string fatalSource, bool? isTerminating = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(fatalSource);

        var timestamp = _clock.Now;
        var id = $"{timestamp:yyyyMMdd-HHmmss-fff}-{Environment.ProcessId}-{Guid.NewGuid():N}";
        var reportId = $"CR-{id}";
        var path = Path.Combine(_crashDirectory, $"crash-{id}.txt");
        var details = FormatExceptionDetails(exception);
        var builder = new StringBuilder();
        builder.AppendLine("Lyrics Displayer Crash Report");
        builder.AppendLine($"Crash Report ID: {reportId}");
        builder.AppendLine($"Timestamp (local): {timestamp:O}");
        builder.AppendLine($"Timestamp (UTC): {timestamp.UtcDateTime:O}");
        builder.AppendLine($"Fatal source: {fatalSource}");
        builder.AppendLine($"Application version: {_applicationVersion}");
        builder.AppendLine($"Process ID: {Environment.ProcessId}");
        builder.AppendLine($"Process architecture: {RuntimeInformation.ProcessArchitecture}");
        builder.AppendLine($"OS: {RuntimeInformation.OSDescription}");
        builder.AppendLine($".NET runtime: {RuntimeInformation.FrameworkDescription}");
        builder.AppendLine($"Is terminating: {isTerminating?.ToString() ?? "unknown"}");
        builder.AppendLine();
        builder.AppendLine("Exception details:");
        builder.AppendLine(details);
        var content = LimitReport(builder.ToString());
        return new(reportId, fatalSource, timestamp, path, content, details);
    }

    public CrashReportWriteResult Write(CrashReportDocument report, Exception? loggerFailure = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        var content = loggerFailure is null
            ? report.Content
            : AppendReporterFailure(report.Content, "Existing application logger failed", loggerFailure);
        try
        {
            _writer.WriteNew(report.Path, content);
            return new(report, report.Path, IsFallback: false);
        }
        catch (Exception primaryFailure)
        {
            var fallbackContent = FormatFallback(report.CrashReportId, report.FatalSource,
                "Primary crash-report write failed.", report.ExceptionDetails, primaryFailure, loggerFailure);
            var fallbackPath = CreatePath("fatal-fallback", report.Timestamp, report.CrashReportId);
            try
            {
                _writer.WriteNew(fallbackPath, fallbackContent);
                return new(report, fallbackPath, IsFallback: true, primaryFailure);
            }
            catch (Exception fallbackFailure)
            {
                var temporaryPath = CreateTemporaryFallbackPath(report.Timestamp, report.CrashReportId);
                try
                {
                    _writer.WriteNew(temporaryPath, fallbackContent);
                    return new(report, temporaryPath, IsFallback: true, primaryFailure, fallbackFailure);
                }
                catch (Exception temporaryFailure)
                {
                    return new(report, null, IsFallback: true, primaryFailure, fallbackFailure, temporaryFailure);
                }
            }
        }
    }

    public CrashFallbackWriteResult WriteMinimalFallback(Exception exception, string fatalSource,
        string reason, string? crashReportId = null)
    {
        var timestamp = _clock.Now;
        var id = crashReportId ?? $"CR-{timestamp:yyyyMMdd-HHmmss-fff}-{Environment.ProcessId}-{Guid.NewGuid():N}";
        var text = FormatFallback(id, fatalSource, reason, FormatExceptionDetails(exception), null, null);
        var crashPath = CreatePath("fatal-fallback", timestamp, id);
        try
        {
            _writer.WriteNew(crashPath, text);
            return new(crashPath, null, null);
        }
        catch (Exception crashDirectoryFailure)
        {
            try
            {
                var temporaryPath = CreateTemporaryFallbackPath(timestamp, id);
                _writer.WriteNew(temporaryPath, text);
                return new(temporaryPath, crashDirectoryFailure, null);
            }
            catch (Exception temporaryDirectoryFailure)
            {
                return new(null, crashDirectoryFailure, temporaryDirectoryFailure);
            }
        }
    }

    public static string FormatExceptionDetails(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var builder = new StringBuilder();
        var visited = new HashSet<Exception>(ExceptionReferenceComparer.Instance);
        AppendException(builder, exception, "Exception", visited, 0);
        return LimitReport(builder.ToString());
    }

    private static void AppendException(StringBuilder builder, Exception exception, string label,
        HashSet<Exception> visited, int depth)
    {
        builder.AppendLine($"{label}:");
        if (!visited.Add(exception))
        {
            builder.AppendLine("  [exception reference already reported]");
            return;
        }

        var type = exception.GetType();
        builder.AppendLine($"  Type: {type.Name}");
        builder.AppendLine($"  Full type: {type.FullName ?? type.Name}");
        builder.AppendLine($"  Message: {Safe(() => exception.Message)}");
        builder.AppendLine($"  HResult: 0x{unchecked((uint)exception.HResult):X8} ({exception.HResult})");
        builder.AppendLine($"  Source: {Safe(() => exception.Source) ?? "(unavailable)"}");
        builder.AppendLine($"  Target site: {FormatTargetSite(exception)}");
        builder.AppendLine("  Stack trace:");
        builder.AppendLine(Indent(Safe(() => exception.StackTrace) ?? "(unavailable)", "    "));
        AppendExceptionData(builder, exception.Data);

        if (depth >= 32)
        {
            builder.AppendLine("  Inner exception nesting limit reached.");
            return;
        }

        if (exception is AggregateException aggregate)
        {
            for (var index = 0; index < aggregate.InnerExceptions.Count; index++)
                AppendException(builder, aggregate.InnerExceptions[index], $"Inner exception [{index + 1}]", visited, depth + 1);
        }
        else if (exception.InnerException is { } inner)
        {
            AppendException(builder, inner, "Inner exception", visited, depth + 1);
        }
    }

    private static void AppendExceptionData(StringBuilder builder, IDictionary data)
    {
        builder.AppendLine("  Exception.Data (values omitted):");
        try
        {
            var count = 0;
            foreach (DictionaryEntry item in data)
            {
                if (count++ >= MaximumDataEntries)
                {
                    builder.AppendLine("    [additional entries omitted]");
                    break;
                }
                var key = Safe(() => item.Key?.ToString()) ?? "(null key)";
                var valueType = item.Value?.GetType().FullName ?? "null";
                builder.AppendLine($"    {SafeKey(key)}: <{valueType}>");
            }
            if (count == 0) builder.AppendLine("    (none)");
        }
        catch (Exception)
        {
            builder.AppendLine("    (could not enumerate exception data)");
        }
    }

    private static string FormatFallback(string crashReportId, string fatalSource, string reason,
        string originalDetails, Exception? primaryFailure, Exception? loggerFailure)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Lyrics Displayer Minimal Fatal Fallback");
        builder.AppendLine($"Crash Report ID: {crashReportId}");
        builder.AppendLine($"Timestamp (local): {DateTimeOffset.Now:O}");
        builder.AppendLine($"Fatal source: {fatalSource}");
        builder.AppendLine($"Reason: {reason}");
        builder.AppendLine("Original exception details:");
        builder.AppendLine(originalDetails);
        if (loggerFailure is not null)
            builder.AppendLine($"Logger failure: {Safe(() => loggerFailure.ToString())}");
        if (primaryFailure is not null)
            builder.AppendLine($"Report writer failure: {Safe(() => primaryFailure.ToString())}");
        return LimitReport(builder.ToString());
    }

    private static string AppendReporterFailure(string report, string label, Exception failure) =>
        LimitReport($"{report}{Environment.NewLine}{label}:{Environment.NewLine}{Safe(() => failure.ToString())}{Environment.NewLine}");

    private string CreatePath(string prefix, DateTimeOffset timestamp, string id) =>
        Path.Combine(_crashDirectory,
            $"{prefix}-{timestamp:yyyyMMdd-HHmmss-fff}-{Environment.ProcessId}-{SafeFilePart(id)}-{Guid.NewGuid():N}.txt");

    private static string CreateTemporaryFallbackPath(DateTimeOffset timestamp, string id)
    {
        var directory = Path.Combine(Path.GetTempPath(), "LyricsDisplayer", "Crash");
        return Path.Combine(directory,
            $"fatal-fallback-{timestamp:yyyyMMdd-HHmmss-fff}-{Environment.ProcessId}-{SafeFilePart(id)}-{Guid.NewGuid():N}.txt");
    }

    private static string GetApplicationVersion()
    {
        try
        {
            var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly.GetName().Version?.ToString()
                ?? "unknown";
        }
        catch (Exception)
        {
            return "unknown";
        }
    }

    private static string FormatTargetSite(Exception exception)
    {
        try
        {
            var method = exception.TargetSite;
            return method is null ? "(unavailable)" :
                $"{method.DeclaringType?.FullName ?? "(unknown type)"}.{method.Name}";
        }
        catch (Exception)
        {
            return "(unavailable)";
        }
    }

    private static string? Safe(Func<string?> read)
    {
        try { return read(); }
        catch (Exception) { return "(unavailable)"; }
    }

    private static string SafeKey(string value)
    {
        var clean = new string(value.Where(character => !char.IsControl(character)).Take(120).ToArray());
        return clean.Length == 0 ? "(empty key)" : clean;
    }

    private static string SafeFilePart(string value) => new(value.Where(char.IsAsciiLetterOrDigit).Take(48).ToArray());

    private static string Indent(string value, string prefix) =>
        string.Join(Environment.NewLine, value.Split(["\r\n", "\n", "\r"], StringSplitOptions.None)
            .Select(line => prefix + line));

    private static string LimitReport(string report) => report.Length <= MaximumReportCharacters
        ? report
        : report[..MaximumReportCharacters] + Environment.NewLine + "[report truncated]";

    private sealed class ExceptionReferenceComparer : IEqualityComparer<Exception>
    {
        public static ExceptionReferenceComparer Instance { get; } = new();
        public bool Equals(Exception? x, Exception? y) => ReferenceEquals(x, y);
        public int GetHashCode(Exception obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
