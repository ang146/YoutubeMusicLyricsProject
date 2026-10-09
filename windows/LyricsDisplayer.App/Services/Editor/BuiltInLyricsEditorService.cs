using System.Windows;
using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Infrastructure.Factories;
using LyricsDisplayer.Resources;
using Microsoft.Extensions.Logging;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace LyricsDisplayer;

/// <summary>Owns the modal editor session and its relationship to the app's windows.</summary>
public sealed class BuiltInLyricsEditorService : IBuiltInLyricsEditorService
{
    private readonly PlaybackStateCoordinator _playback;
    private readonly LyricsLibrary _library;
    private readonly IBuiltInLyricsEditorFactory _factory;
    private readonly LyricsOverlayController _overlay;
    private readonly IApplicationLifetimeState _lifetime;
    private readonly ILogger<BuiltInLyricsEditorService> _logger;
    private BuiltInLyricsEditorWindow? _window;
    private bool _applicationExitPending;
    private bool _canOpen;

    public BuiltInLyricsEditorService(PlaybackStateCoordinator playback, LyricsLibrary library,
        IBuiltInLyricsEditorFactory factory, LyricsOverlayController overlay,
        IApplicationLifetimeState lifetime, ILogger<BuiltInLyricsEditorService> logger)
    {
        _playback = playback ?? throw new ArgumentNullException(nameof(playback));
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _playback.LyricsActionAvailabilityChanged += RefreshAvailability;
        RefreshAvailability();
    }

    public bool CanOpen => _canOpen;
    public event Action? AvailabilityChanged;

    public void RefreshAvailability()
    {
        var canOpen = _window is null && !_lifetime.IsFatalShutdown &&
            _playback.ActiveLocalLyricsRecord is { } record &&
            _library.LoadForEditing(record).Status == EditorAssetStatus.Ready;
        if (_canOpen == canOpen) return;
        _canOpen = canOpen;
        AvailabilityChanged?.Invoke();
    }

    public void Open()
    {
        if (_window is { IsVisible: true } existing)
        {
            existing.Activate();
            return;
        }
        RefreshAvailability();
        if (!_canOpen || _playback.ActiveLocalLyricsRecord is not { } record) return;

        var creation = _factory.Create(record);
        if (!creation.Succeeded || creation.Window is null)
        {
            MessageBox.Show(Application.Current?.MainWindow,
                creation.Error ?? Strings.EditorUnavailableForEditing,
                Strings.EditorWindowTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            _logger.LogWarning("Could not open the built-in editor for LocalTrackId={LocalTrackId}: {Error}",
                record.LocalTrackId, creation.Error ?? creation.Status.ToString());
            RefreshAvailability();
            return;
        }

        var window = creation.Window;
        if (Application.Current?.MainWindow is { } owner) window.Owner = owner;
        _window = window;
        RefreshAvailability();
        var suppressTopmost = false;
        try
        {
            _overlay.SetEditorModalTopmostSuppressed(true);
            suppressTopmost = true;
            window.ShowDialog();
        }
        finally
        {
            var shuttingDown = _applicationExitPending || _lifetime.IsFatalShutdown ||
                               Application.Current?.Dispatcher.HasShutdownStarted == true ||
                               Application.Current?.Dispatcher.HasShutdownFinished == true;
            if (suppressTopmost && !shuttingDown)
                _overlay.SetEditorModalTopmostSuppressed(false);
            _window = null;
            _applicationExitPending = false;
            RefreshAvailability();
        }
    }

    public bool TryCloseForOwnerClosing(bool applicationExitPending)
    {
        _applicationExitPending = applicationExitPending || _lifetime.IsFatalShutdown;
        if (_window is not { } editor) return true;
        if (editor.RequestCloseFromApplication()) return true;
        _applicationExitPending = false;
        return false;
    }

    public void CancelOwnerClose() => _applicationExitPending = false;
}
