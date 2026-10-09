using System.IO;
using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Resources;
using Microsoft.Extensions.Logging;

namespace LyricsDisplayer;

/// <summary>Owns current authoritative-LRC resolution and the external-editor action/status.</summary>
public sealed class CurrentLyricsFileService : ICurrentLyricsFileService
{
    private readonly PlaybackStateCoordinator _playback;
    private readonly LyricsLibrary _library;
    private readonly ExternalLrcOpener _opener;
    private readonly ILogger<CurrentLyricsFileService> _logger;
    private bool _canOpen;
    private string _status = Strings.LrcNoCurrentLocalFile;

    public CurrentLyricsFileService(PlaybackStateCoordinator playback, LyricsLibrary library,
        ExternalLrcOpener opener, ILogger<CurrentLyricsFileService> logger)
    {
        _playback = playback ?? throw new ArgumentNullException(nameof(playback));
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _opener = opener ?? throw new ArgumentNullException(nameof(opener));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _playback.LyricsActionAvailabilityChanged += RefreshAvailability;
        RefreshAvailability();
    }

    public bool CanOpen => _canOpen;
    public string Status => _status;
    public event Action? AvailabilityChanged;
    public event Action? StatusChanged;

    public void RefreshAvailability()
    {
        var canOpen = false;
        if (_playback.IsCurrentLocalLrcUsable && _playback.ActiveLocalLyricsRecord is { } record)
        {
            try { canOpen = File.Exists(_library.ResolveLyricsPath(record)); }
            catch (Exception exception) when (exception is IOException or InvalidDataException or
                                                UnauthorizedAccessException or ArgumentException or
                                                System.Security.SecurityException)
            {
                _logger.LogDebug(exception, "The active local LRC path is currently unavailable.");
            }
        }
        if (_canOpen == canOpen) return;
        _canOpen = canOpen;
        AvailabilityChanged?.Invoke();
    }

    public void SetStatus(string status)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (string.Equals(_status, status, StringComparison.Ordinal)) return;
        _status = status;
        StatusChanged?.Invoke();
    }

    public void OpenExternally()
    {
        RefreshAvailability();
        if (!_canOpen || !_playback.IsCurrentLocalLrcUsable ||
            _playback.ActiveLocalLyricsRecord is not { } record)
        {
            SetStatus(Strings.LrcNoCurrentLocalFile);
            return;
        }

        string path;
        try { path = _library.ResolveLyricsPath(record); }
        catch (Exception exception) when (exception is IOException or InvalidDataException or
                                            UnauthorizedAccessException or ArgumentException or
                                            System.Security.SecurityException)
        {
            _logger.LogWarning(exception, "Could not resolve the active authoritative LRC path.");
            SetStatus(Strings.LrcPathUnavailable);
            RefreshAvailability();
            return;
        }

        if (!_opener.TryOpen(path, out var error))
        {
            SetStatus(error ?? Strings.LrcCouldNotBeOpened);
            RefreshAvailability();
            return;
        }
        SetStatus(Strings.LrcExternalOpened);
        RefreshAvailability();
    }
}
