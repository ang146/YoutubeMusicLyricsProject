using System.IO;
using System.Security;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace LyricsDisplayer;

public enum ActiveLrcFileObservationKind
{
    Content,
    Missing,
    Unavailable
}

public sealed record ActiveLrcFileObservation(
    ActiveLrcFileObservationKind Kind,
    string? Fingerprint = null,
    string? Error = null);

/// <summary>Watches only one active LRC and coalesces filesystem notifications into stable fingerprints.</summary>
public sealed class ActiveLrcFileWatcher : IDisposable
{
    internal static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(300);
    internal static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromMilliseconds(125);
    internal const int DefaultReadAttempts = 6;

    private readonly object _sync = new();
    private readonly string _path;
    private readonly string _directory;
    private readonly string _fileName;
    private readonly string _parentDirectory;
    private readonly string _directoryName;
    private readonly TimeSpan _debounce;
    private readonly TimeSpan _retryDelay;
    private readonly int _readAttempts;
    private readonly Action<ActiveLrcFileObservation> _changed;
    private readonly ILogger<ActiveLrcFileWatcher> _logger;
    private readonly CancellationTokenSource _stop = new();
    private readonly CancellationToken _stopToken;
    private readonly SemaphoreSlim _checkGate = new(1, 1);
    private readonly System.Threading.Timer _debounceTimer;
    private readonly System.Threading.Timer _recoveryTimer;
    private FileSystemWatcher? _fileWatcher;
    private FileSystemWatcher? _directoryWatcher;
    private string? _lastFingerprint;
    private ActiveLrcFileObservationKind? _lastObservationKind;
    private int _scheduledVersion;
    private int _recoveryAttempt;
    private bool _disposed;

    public ActiveLrcFileWatcher(
        ILogger<ActiveLrcFileWatcher> logger,
        string path,
        string? initialFingerprint,
        Action<ActiveLrcFileObservation> changed,
        TimeSpan? debounce = null,
        TimeSpan? retryDelay = null,
        int readAttempts = DefaultReadAttempts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(changed);
        if (readAttempts < 2) throw new ArgumentOutOfRangeException(nameof(readAttempts));

        _path = Path.GetFullPath(path);
        _directory = Path.GetDirectoryName(_path) ?? throw new ArgumentException("An LRC parent directory is required.", nameof(path));
        _fileName = Path.GetFileName(_path);
        _parentDirectory = Path.GetDirectoryName(_directory) ?? _directory;
        _directoryName = Path.GetFileName(_directory);
        _debounce = debounce ?? DefaultDebounce;
        _retryDelay = retryDelay ?? DefaultRetryDelay;
        _readAttempts = readAttempts;
        _changed = changed;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _lastFingerprint = initialFingerprint;
        _lastObservationKind = initialFingerprint is null ? null : ActiveLrcFileObservationKind.Content;
        _stopToken = _stop.Token;
        _debounceTimer = new System.Threading.Timer(OnDebounce, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _recoveryTimer = new System.Threading.Timer(OnRecovery, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        lock (_sync) AttachWatchersLocked();
    }

    public void CheckNow() => ScheduleCheck(TimeSpan.Zero);

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _fileWatcher?.Dispose();
            _directoryWatcher?.Dispose();
            _fileWatcher = null;
            _directoryWatcher = null;
            _debounceTimer.Dispose();
            _recoveryTimer.Dispose();
        }
        _stop.Cancel();
    }

    private void AttachWatchersLocked()
    {
        AttachFileWatcherLocked();
        if (_directoryWatcher is not null || !Directory.Exists(_parentDirectory)) return;
        try
        {
            var watcher = new FileSystemWatcher(_parentDirectory, _directoryName)
            {
                NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            watcher.Created += OnDirectoryChanged;
            watcher.Deleted += OnDirectoryChanged;
            watcher.Renamed += OnDirectoryRenamed;
            watcher.Error += OnWatcherError;
            _directoryWatcher = watcher;
        }
        catch (Exception exception) when (IsWatcherException(exception))
        {
            _logger.LogWarning(exception, "Could not watch the active LRC directory.");
            ScheduleRecoveryLocked();
        }
    }

    private void AttachFileWatcherLocked()
    {
        if (_fileWatcher is not null || !Directory.Exists(_directory)) return;
        try
        {
            var watcher = new FileSystemWatcher(_directory, _fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            watcher.Changed += OnFileChanged;
            watcher.Created += OnFileChanged;
            watcher.Deleted += OnFileChanged;
            watcher.Renamed += OnFileRenamed;
            watcher.Error += OnWatcherError;
            _fileWatcher = watcher;
            _logger.LogInformation("External LRC watcher started.");
        }
        catch (Exception exception) when (IsWatcherException(exception))
        {
            _logger.LogWarning(exception, "Could not start the active LRC watcher.");
            ScheduleRecoveryLocked();
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (IsTargetFile(e.FullPath))
        {
            _logger.LogDebug("External LRC change detected.");
            ScheduleCheck(_debounce);
        }
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        if (IsTargetFile(e.FullPath) || IsTargetFile(e.OldFullPath))
        {
            _logger.LogDebug("External LRC rename/replacement detected.");
            ScheduleCheck(_debounce);
        }
    }

    private void OnDirectoryChanged(object sender, FileSystemEventArgs e) => RefreshForDirectoryChange(e.FullPath);

    private void OnDirectoryRenamed(object sender, RenamedEventArgs e)
    {
        if (IsTargetDirectory(e.FullPath) || IsTargetDirectory(e.OldFullPath))
            RefreshForDirectoryChange(e.FullPath);
    }

    private void RefreshForDirectoryChange(string changedPath)
    {
        if (!IsTargetDirectory(changedPath)) return;
        lock (_sync)
        {
            if (_disposed) return;
            _fileWatcher?.Dispose();
            _fileWatcher = null;
            AttachFileWatcherLocked();
        }
        ScheduleCheck(_debounce);
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _logger.LogWarning(e.GetException(), "External LRC watcher error.");
        lock (_sync)
        {
            if (_disposed) return;
            _fileWatcher?.Dispose();
            _directoryWatcher?.Dispose();
            _fileWatcher = null;
            _directoryWatcher = null;
            ScheduleRecoveryLocked();
        }
        CheckNow();
    }

    private void ScheduleRecoveryLocked()
    {
        var seconds = Math.Min(30, 2 * (1 << Math.Min(_recoveryAttempt++, 4)));
        _recoveryTimer.Change(TimeSpan.FromSeconds(seconds), Timeout.InfiniteTimeSpan);
    }

    private void OnRecovery(object? state)
    {
        lock (_sync)
        {
            if (_disposed) return;
            AttachWatchersLocked();
            if (_fileWatcher is not null) _recoveryAttempt = 0;
            else ScheduleRecoveryLocked();
        }
        CheckNow();
    }

    private void ScheduleCheck(TimeSpan delay)
    {
        lock (_sync)
        {
            if (_disposed) return;
            _scheduledVersion++;
            try { _debounceTimer.Change(delay, Timeout.InfiniteTimeSpan); }
            catch (ObjectDisposedException) { }
        }
    }

    private async void OnDebounce(object? state)
    {
        try { await CheckStableFileAsync(_stopToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "External LRC verification failed safely.");
        }
    }

    private async Task CheckStableFileAsync(CancellationToken cancellationToken)
    {
        if (!await _checkGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            ScheduleCheck(TimeSpan.FromMilliseconds(25));
            return;
        }

        try
        {
            int version;
            lock (_sync)
            {
                if (_disposed) return;
                version = _scheduledVersion;
            }

            string? previousFingerprint = null;
            string? lastError = null;
            var consecutiveMissing = 0;
            for (var attempt = 0; attempt < _readAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsCurrent(version)) return;

                var read = TryReadFingerprint();
                if (read.Fingerprint is { } fingerprint)
                {
                    consecutiveMissing = 0;
                    lastError = null;
                    if (string.Equals(previousFingerprint, fingerprint, StringComparison.Ordinal))
                    {
                        ReportIfChanged(version, new(ActiveLrcFileObservationKind.Content, fingerprint));
                        return;
                    }
                    previousFingerprint = fingerprint;
                }
                else if (read.Missing)
                {
                    consecutiveMissing++;
                    previousFingerprint = null;
                }
                else
                {
                    lastError = read.Error;
                    previousFingerprint = null;
                }

                if (attempt + 1 < _readAttempts)
                    await Task.Delay(_retryDelay, cancellationToken).ConfigureAwait(false);
            }

            var finalObservation = consecutiveMissing == _readAttempts || !File.Exists(_path)
                ? new ActiveLrcFileObservation(ActiveLrcFileObservationKind.Missing)
                : new ActiveLrcFileObservation(ActiveLrcFileObservationKind.Unavailable,
                    Error: lastError ?? "The file did not become stable before the retry limit.");
            ReportIfChanged(version, finalObservation);
        }
        finally
        {
            _checkGate.Release();
        }
    }

    private (string? Fingerprint, bool Missing, string? Error) TryReadFingerprint()
    {
        try
        {
            var bytes = File.ReadAllBytes(_path);
            return (Convert.ToHexString(SHA256.HashData(bytes)), false, null);
        }
        catch (FileNotFoundException) { return (null, true, null); }
        catch (DirectoryNotFoundException) { return (null, true, null); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        { return (null, false, exception.Message); }
    }

    private bool IsCurrent(int version)
    {
        lock (_sync) return !_disposed && version == _scheduledVersion;
    }

    private void ReportIfChanged(int version, ActiveLrcFileObservation observation)
    {
        lock (_sync)
        {
            if (_disposed || version != _scheduledVersion) return;
            if (observation.Kind == _lastObservationKind &&
                (observation.Kind != ActiveLrcFileObservationKind.Content ||
                 string.Equals(observation.Fingerprint, _lastFingerprint, StringComparison.Ordinal)))
            {
                _logger.LogDebug("External LRC reload skipped: content unchanged.");
                return;
            }
            _lastObservationKind = observation.Kind;
            if (observation.Fingerprint is not null) _lastFingerprint = observation.Fingerprint;
        }

        try { _changed(observation); }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "External LRC change handler failed safely.");
        }
    }

    private bool IsTargetFile(string path) =>
        string.Equals(Path.GetFullPath(path), _path, StringComparison.OrdinalIgnoreCase);

    private bool IsTargetDirectory(string path) =>
        string.Equals(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar),
            _directory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private static bool IsWatcherException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or
            PlatformNotSupportedException or NotSupportedException or SecurityException;
}
