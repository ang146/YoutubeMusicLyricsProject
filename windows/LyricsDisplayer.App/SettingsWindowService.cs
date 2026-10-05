namespace LyricsDisplayer;

public sealed class SettingsWindowService
{
    private readonly Func<SettingsWindow> _createWindow;
    private SettingsWindow? _window;

    public SettingsWindowService(Func<SettingsWindow>? createWindow = null) =>
        _createWindow = createWindow ?? (() => new SettingsWindow());

    public void Open()
    {
        if (_window is { } existing)
        {
            if (existing.WindowState == System.Windows.WindowState.Minimized)
                existing.WindowState = System.Windows.WindowState.Normal;
            if (!existing.IsVisible) existing.Show();
            existing.Activate();
            return;
        }

        var window = _createWindow();
        _window = window;
        window.Closed += OnWindowClosed;
        window.Show();
    }

    public void CloseForApplicationExit() => _window?.Close();

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (ReferenceEquals(_window, sender)) _window = null;
    }
}
