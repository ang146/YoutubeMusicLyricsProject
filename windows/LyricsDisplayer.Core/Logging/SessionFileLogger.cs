using System.Globalization;
using System.Text.RegularExpressions;

namespace LyricsDisplayer.Core.Logging;

public interface IClock
{
    DateTimeOffset Now { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}

public sealed partial class SessionFileLogger : IDisposable
{
    private const string CurrentFileName = "current.logs";
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly string _currentPath;
    private readonly IClock _clock;
    private DateOnly _activeDate;
    private bool _disposed;

    public SessionFileLogger(string directory, IClock? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
        _currentPath = Path.Combine(directory, CurrentFileName);
        _clock = clock ?? new SystemClock();
        Directory.CreateDirectory(_directory);

        var now = _clock.Now;
        if (File.Exists(_currentPath) && new FileInfo(_currentPath).Length > 0)
        {
            RotateCurrent(FindCurrentLogDate());
        }

        using (File.Open(_currentPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
        {
        }

        _activeDate = DateOnly.FromDateTime(now.LocalDateTime);
        CleanupRetention(now);
    }

    public string CurrentPath => _currentPath;

    public void Write(string level, string category, string message)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var now = _clock.Now;
            var localDate = DateOnly.FromDateTime(now.LocalDateTime);
            if (localDate != _activeDate)
            {
                RotateCurrent(_activeDate);
                CleanupRetention(now);
                _activeDate = localDate;
            }

            var safeLevel = SingleLine(level);
            var safeCategory = SingleLine(category);
            var safeMessage = SingleLine(message);
            var entry = $"{now:yyyy-MM-dd'T'HH:mm:ss.fffzzz} [{safeLevel}] [{safeCategory}] {safeMessage}{Environment.NewLine}";
            File.AppendAllText(_currentPath, entry, System.Text.Encoding.UTF8);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }
    }

    private void RotateCurrent(DateOnly date)
    {
        if (!File.Exists(_currentPath) || new FileInfo(_currentPath).Length == 0)
        {
            return;
        }

        var baseName = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var destination = Path.Combine(_directory, $"{baseName}.logs");
        var suffix = 0;
        while (File.Exists(destination))
        {
            suffix++;
            destination = Path.Combine(_directory, $"{baseName}-({suffix}).logs");
        }

        File.Move(_currentPath, destination);
    }

    private DateOnly FindCurrentLogDate()
    {
        try
        {
            var firstLine = File.ReadLines(_currentPath).FirstOrDefault();
            var firstToken = firstLine?.Split(' ', 2)[0];
            if (DateTimeOffset.TryParse(firstToken, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
                    out var timestamp))
            {
                return DateOnly.FromDateTime(timestamp.DateTime);
            }
        }
        catch (IOException)
        {
            // Fall back to the file timestamp when prior content is not readable.
        }

        return DateOnly.FromDateTime(File.GetLastWriteTime(_currentPath));
    }

    private void CleanupRetention(DateTimeOffset now)
    {
        var cutoff = DateOnly.FromDateTime(now.LocalDateTime.Subtract(Retention));
        foreach (var path in Directory.EnumerateFiles(_directory, "*.logs", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(path);
            var match = RotatedFilePattern().Match(fileName);
            if (!match.Success ||
                !DateOnly.TryParseExact(match.Groups["date"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var fileDate))
            {
                continue;
            }

            if (fileDate < cutoff)
            {
                File.Delete(path);
            }
        }
    }

    private static string SingleLine(string value) =>
        (value ?? string.Empty).Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

    [GeneratedRegex(@"^(?<date>\d{4}-\d{2}-\d{2})(?:-\([1-9]\d*\))?\.logs$", RegexOptions.CultureInvariant)]
    private static partial Regex RotatedFilePattern();
}
