using System.Drawing;
using LyricsDisplayer.Resources;
using Forms = System.Windows.Forms;

namespace LyricsDisplayer;

public sealed class WindowsTrayIcon : ITrayIconAdapter
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _toggleOverlayItem;
    private Action? _openControlPanel;
    private Func<bool>? _toggleOverlay;
    private Action? _exitApplication;
    private bool _disposed;

    public WindowsTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(Strings.OpenLyricsWindow, null, (_, _) => _openControlPanel?.Invoke());
        _toggleOverlayItem = new Forms.ToolStripMenuItem(Strings.ShowDesktopLyrics);
        _toggleOverlayItem.Click += (_, _) => _toggleOverlay?.Invoke();
        menu.Items.Add(_toggleOverlayItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Strings.ExitLyricsDisplayer, null, (_, _) => _exitApplication?.Invoke());
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = Strings.AppName,
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += (_, _) => _openControlPanel?.Invoke();
        menu.Opening += (_, _) => _toggleOverlayItem.Text = _toggleOverlayItem.Checked
            ? Strings.HideDesktopLyrics
            : Strings.ShowDesktopLyrics;
    }

    public void Show(Action openControlPanel, Func<bool> toggleOverlay, Action exitApplication, bool overlayVisible)
    {
        if (_disposed) return;
        _openControlPanel = openControlPanel;
        _toggleOverlay = toggleOverlay;
        _exitApplication = exitApplication;
        SetOverlayVisible(overlayVisible);
        _notifyIcon.Visible = true;
    }

    public void SetOverlayVisible(bool visible)
    {
        _toggleOverlayItem.Checked = visible;
        _toggleOverlayItem.Text = visible ? Strings.HideDesktopLyrics : Strings.ShowDesktopLyrics;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
