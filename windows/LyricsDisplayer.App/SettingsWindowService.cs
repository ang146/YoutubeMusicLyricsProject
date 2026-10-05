using LyricsDisplayer.Infrastructure.Factories;
using Microsoft.Extensions.Logging;

namespace LyricsDisplayer;

public sealed class SettingsWindowService
{
    private readonly ISettingsWindowFactory _factory;
    private readonly ILogger<SettingsWindowService> _logger;
    private SettingsWindow? _window;

    public SettingsWindowService(ISettingsWindowFactory factory, ILogger<SettingsWindowService> logger)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

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

        _logger.LogDebug("Creating the Settings window.");
        var window = _factory.Create();
        _window = window;
        window.Closed += OnWindowClosed;
        window.Show();
        _logger.LogInformation("Settings window shown.");
    }

    public void CloseForApplicationExit() => _window?.Close();

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(_window, sender)) return;
        _window = null;
        _logger.LogDebug("Settings window closed and released.");
    }
}
