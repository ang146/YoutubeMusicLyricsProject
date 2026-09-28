namespace LyricsDisplayer;

public interface ITrayIconAdapter : IDisposable
{
    void Show(Action openControlPanel, Func<bool> toggleOverlay, Action exitApplication, bool overlayVisible);
    void SetOverlayVisible(bool visible);
}

public sealed class TrayLifecycleService : IDisposable
{
    private readonly ITrayIconAdapter _icon;
    private readonly Action _openControlPanel;
    private readonly Func<bool> _toggleOverlay;
    private readonly Action _exitApplication;
    private bool _started;
    private bool _disposed;

    public TrayLifecycleService(
        ITrayIconAdapter icon,
        Action openControlPanel,
        Func<bool> toggleOverlay,
        Action exitApplication)
    {
        _icon = icon;
        _openControlPanel = openControlPanel;
        _toggleOverlay = toggleOverlay;
        _exitApplication = exitApplication;
    }

    public void Start(bool overlayVisible = false)
    {
        if (_started || _disposed) return;
        _started = true;
        _icon.Show(_openControlPanel, _toggleOverlay, _exitApplication, overlayVisible);
    }

    public void SetOverlayVisible(bool visible)
    {
        if (_started && !_disposed) _icon.SetOverlayVisible(visible);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _icon.Dispose();
    }
}

public static class ControlPanelClosePolicy
{
    public static bool ShouldHideToTray(bool closeToTray, bool explicitExit) =>
        closeToTray && !explicitExit;
}
