namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class GlobalHotkeyServiceTests
{
    [Test]
    public void StartRegistersFixedShortcutsOnceAndPressesInvokeActions()
    {
        var platform = new FakePlatform();
        var overlayToggles = 0;
        var clickThroughToggles = 0;
        using var service = new GlobalHotkeyService(NullLogger<GlobalHotkeyService>.Instance,
            platform, () => overlayToggles++, () => clickThroughToggles++);

        service.Start();
        service.Start();
        platform.Raise(GlobalHotkeyService.ToggleOverlayId);
        platform.Raise(GlobalHotkeyService.ToggleClickThroughId);

        Assert.Multiple(() =>
        {
            Assert.That(platform.Registrations, Has.Count.EqualTo(2));
            Assert.That(platform.Registrations[GlobalHotkeyService.ToggleOverlayId],
                Is.EqualTo(GlobalHotkeyService.ToggleOverlayGesture));
            Assert.That(platform.Registrations[GlobalHotkeyService.ToggleClickThroughId],
                Is.EqualTo(GlobalHotkeyService.ToggleClickThroughGesture));
            Assert.That(overlayToggles, Is.EqualTo(1));
            Assert.That(clickThroughToggles, Is.EqualTo(1));
        });
    }

    [Test]
    public void RegistrationFailureIsNonFatalAndFailedShortcutIsIgnored()
    {
        var platform = new FakePlatform { FailedId = GlobalHotkeyService.ToggleClickThroughId };
        var logger = new RecordingLogger<GlobalHotkeyService>();
        var clickThroughToggles = 0;
        using var service = new GlobalHotkeyService(logger, platform, () => { }, () => clickThroughToggles++);

        Assert.DoesNotThrow(service.Start);
        platform.Raise(GlobalHotkeyService.ToggleClickThroughId);

        Assert.Multiple(() =>
        {
            Assert.That(service.RegisteredIds, Is.EqualTo(new[] { GlobalHotkeyService.ToggleOverlayId }));
            Assert.That(clickThroughToggles, Is.Zero);
            Assert.That(logger.Entries.Any(entry =>
                entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning && entry.Message.Contains("unavailable")),
                Is.True);
        });
    }

    [Test]
    public void StopUnregistersOnlySuccessfulRegistrationsAndCanBeCalledAgain()
    {
        var platform = new FakePlatform { FailedId = GlobalHotkeyService.ToggleClickThroughId };
        using var service = new GlobalHotkeyService(NullLogger<GlobalHotkeyService>.Instance, platform, () => { }, () => { });
        service.Start();
        service.Stop();
        service.Stop();

        Assert.That(platform.Unregistered, Is.EqualTo(new[] { GlobalHotkeyService.ToggleOverlayId }));
    }

    [Test]
    public void ClickThroughShortcutProvidesRecoveryWithoutOverlayMouseInput()
    {
        var platform = new FakePlatform();
        var clickThrough = true;
        using var service = new GlobalHotkeyService(NullLogger<GlobalHotkeyService>.Instance,
            platform, () => { }, () => clickThrough = !clickThrough);
        service.Start();

        platform.Raise(GlobalHotkeyService.ToggleClickThroughId);

        Assert.That(clickThrough, Is.False);
    }

    private sealed class FakePlatform : IGlobalHotkeyPlatform
    {
        public event Action<int>? Pressed;
        public int? FailedId { get; init; }
        public Dictionary<int, GlobalHotkeyGesture> Registrations { get; } = [];
        public List<int> Unregistered { get; } = [];

        public bool Register(int id, GlobalHotkeyGesture gesture)
        {
            Registrations[id] = gesture;
            return id != FailedId;
        }

        public void Unregister(int id) => Unregistered.Add(id);
        public void Raise(int id) => Pressed?.Invoke(id);
        public void Dispose() { }
    }
}
