namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class TrayLifecycleServiceTests
{
    [Test]
    public void StartIsSingletonAndCallbacksReachTheExistingApplicationActions()
    {
        var icon = new FakeTrayIcon();
        var opened = 0;
        var toggles = 0;
        var exits = 0;
        using var tray = new TrayLifecycleService(icon, () => opened++, () => { toggles++; return true; },
            () => exits++);

        tray.Start();
        tray.Start();
        icon.Open();
        Assert.That(icon.Toggle(), Is.True);
        icon.Exit();

        Assert.Multiple(() =>
        {
            Assert.That(icon.ShowCalls, Is.EqualTo(1));
            Assert.That(opened, Is.EqualTo(1));
            Assert.That(toggles, Is.EqualTo(1));
            Assert.That(exits, Is.EqualTo(1));
        });
    }

    [Test]
    public void DisposeIsIdempotentAndNoUpdatesReachDisposedIcon()
    {
        var icon = new FakeTrayIcon();
        var tray = new TrayLifecycleService(icon, () => { }, () => false, () => { });
        tray.Start();
        tray.Dispose();
        tray.Dispose();
        tray.SetOverlayVisible(true);

        Assert.Multiple(() =>
        {
            Assert.That(icon.DisposeCalls, Is.EqualTo(1));
            Assert.That(icon.OverlayVisibilityUpdates, Is.Empty);
        });
    }

    [TestCase(false, false, false)]
    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    public void ExplicitExitAlwaysBypassesCloseToTray(bool closeToTray, bool explicitExit, bool hide)
    {
        Assert.That(ControlPanelClosePolicy.ShouldHideToTray(closeToTray, explicitExit), Is.EqualTo(hide));
    }

    private sealed class FakeTrayIcon : ITrayIconAdapter
    {
        private Action? _open;
        private Func<bool>? _toggle;
        private Action? _exit;
        public int ShowCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public List<bool> OverlayVisibilityUpdates { get; } = [];

        public void Show(Action openControlPanel, Func<bool> toggleOverlay, Action exitApplication, bool overlayVisible)
        {
            ShowCalls++;
            _open = openControlPanel;
            _toggle = toggleOverlay;
            _exit = exitApplication;
        }

        public void SetOverlayVisible(bool visible) => OverlayVisibilityUpdates.Add(visible);
        public void Open() => _open?.Invoke();
        public bool Toggle() => _toggle?.Invoke() ?? false;
        public void Exit() => _exit?.Invoke();
        public void Dispose() => DisposeCalls++;
    }
}
