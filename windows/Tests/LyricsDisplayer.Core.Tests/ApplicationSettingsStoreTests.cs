using System.Text.Json;
using LyricsDisplayer.Core.Settings;

namespace LyricsDisplayer.Core.Tests;

[TestFixture]
public sealed class ApplicationSettingsStoreTests
{
    private string _root = null!;
    private string _settingsPath = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "LyricsDisplayerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _settingsPath = Path.Combine(_root, "settings.json");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Test]
    public void AbsentSettingsUseDefaults()
    {
        var result = Store().Load();
        Assert.Multiple(() =>
        {
            Assert.That(result.Settings.LyricsLibraryPath, Is.Null);
            Assert.That(result.Settings.OverlayPosition, Is.Null);
            Assert.That(result.Settings.OverlayPreferences, Is.EqualTo(OverlayPreferences.Default));
            Assert.That(result.Warning, Is.Null);
        });
    }

    [Test]
    public void LibraryPathOnlyIsRead()
    {
        Write("""{"lyricsLibraryPath":"C:\\Lyrics"}""");
        var result = Store().Load().Settings;
        Assert.Multiple(() =>
        {
            Assert.That(result.LyricsLibraryPath, Is.EqualTo("C:\\Lyrics"));
            Assert.That(result.OverlayPosition, Is.Null);
        });
    }

    [Test]
    public void OverlayOnlyIsRead()
    {
        Write("""{"overlay":{"left":125.5,"top":720}}""");
        var result = Store().Load().Settings;
        Assert.Multiple(() =>
        {
            Assert.That(result.LyricsLibraryPath, Is.Null);
            Assert.That(result.OverlayPosition, Is.EqualTo(new OverlayPosition(125.5, 720)));
        });
    }

    [Test]
    public void BothSettingsAreRead()
    {
        Write("""{"lyricsLibraryPath":"C:\\Lyrics","overlay":{"left":5,"top":6}}""");
        var result = Store().Load().Settings;
        Assert.Multiple(() =>
        {
            Assert.That(result.LyricsLibraryPath, Is.EqualTo("C:\\Lyrics"));
            Assert.That(result.OverlayPosition, Is.EqualTo(new OverlayPosition(5, 6)));
        });
    }

    [Test]
    public void OverlayPositionRoundTripsAndPreservesLibraryAndUnknownSettings()
    {
        Write("""{"lyricsLibraryPath":"C:\\Lyrics","futureSetting":{"enabled":true}}""");
        var store = Store();
        store.SaveOverlayPosition(new(501.25, -42.5));

        var loaded = store.Load().Settings;
        using var document = JsonDocument.Parse(File.ReadAllText(_settingsPath));
        Assert.Multiple(() =>
        {
            Assert.That(loaded.OverlayPosition, Is.EqualTo(new OverlayPosition(501.25, -42.5)));
            Assert.That(loaded.LyricsLibraryPath, Is.EqualTo("C:\\Lyrics"));
            Assert.That(document.RootElement.GetProperty("futureSetting").GetProperty("enabled").GetBoolean(), Is.True);
            Assert.That(Directory.GetFiles(_root, "*.tmp"), Is.Empty);
        });
    }

    [Test]
    public void OverlayPreferencesRoundTripAndPreservePositionAndUnknownSettings()
    {
        Write("""{"overlay":{"left":12,"top":34,"futureOverlayValue":"kept"},"futureRootValue":7}""");
        var store = Store();
        var preferences = new OverlayPreferences(true, true, false, OverlayDisplayMode.OneLine, 1230);
        store.SaveOverlayPreferences(preferences);

        var loaded = store.Load().Settings;
        using var document = JsonDocument.Parse(File.ReadAllText(_settingsPath));
        Assert.Multiple(() =>
        {
            Assert.That(loaded.OverlayPreferences, Is.EqualTo(preferences));
            Assert.That(loaded.OverlayPosition, Is.EqualTo(new OverlayPosition(12, 34)));
            Assert.That(document.RootElement.GetProperty("overlay").GetProperty("futureOverlayValue").GetString(),
                Is.EqualTo("kept"));
            Assert.That(document.RootElement.GetProperty("futureRootValue").GetInt32(), Is.EqualTo(7));
        });
    }

    [Test]
    public void PositionSavePreservesOverlayPreferencesAndUnknownOverlaySettings()
    {
        Write("""{"overlay":{"locked":true,"clickThrough":true,"topmost":false,"displayMode":"oneLine","width":1100,"future":1}}""");
        var store = Store();
        store.SaveOverlayPosition(new(50, 60));

        var loaded = store.Load().Settings;
        using var document = JsonDocument.Parse(File.ReadAllText(_settingsPath));
        Assert.Multiple(() =>
        {
            Assert.That(loaded.OverlayPreferences,
                Is.EqualTo(new OverlayPreferences(true, true, false, OverlayDisplayMode.OneLine, 1100)));
            Assert.That(document.RootElement.GetProperty("overlay").GetProperty("future").GetInt32(), Is.EqualTo(1));
        });
    }

    [Test]
    public void MissingPreferenceFieldsUseCompatibilityDefaults()
    {
        Write("""{"overlay":{"left":1,"top":2}}""");
        Assert.That(Store().Load().Settings.OverlayPreferences, Is.EqualTo(OverlayPreferences.Default));
    }

    [Test]
    public void MalformedPreferencesFallBackIndependentlyWithoutDiscardingPosition()
    {
        Write("""{"overlay":{"left":1,"top":2,"locked":"yes","clickThrough":true,"topmost":4,"displayMode":"many","width":12}}""");
        var result = Store().Load();
        Assert.Multiple(() =>
        {
            Assert.That(result.Settings.OverlayPosition, Is.EqualTo(new OverlayPosition(1, 2)));
            Assert.That(result.Settings.OverlayPreferences,
                Is.EqualTo(OverlayPreferences.Default with { ClickThrough = true }));
            Assert.That(result.Warning, Does.Contain("overlay.locked"));
            Assert.That(result.Warning, Does.Contain("overlay.width"));
        });
    }

    [TestCase("{\"overlay\":42}")]
    [TestCase("{\"overlay\":{}}")]
    [TestCase("{\"overlay\":{\"left\":1}}")]
    [TestCase("{\"overlay\":{\"left\":\"x\",\"top\":2}}")]
    [TestCase("{\"overlay\":{\"left\":1e999,\"top\":2}}")]
    public void MalformedMissingOrInvalidOverlayUsesDefaultSafely(string json)
    {
        Write(json);
        var result = Store().Load();
        Assert.Multiple(() =>
        {
            Assert.That(result.Settings.OverlayPosition, Is.Null);
            Assert.That(result.Warning, Is.Not.Null);
        });
    }

    [Test]
    public void InvalidOverlayDoesNotDiscardValidLibraryPath()
    {
        Write("""{"lyricsLibraryPath":"C:\\Lyrics","overlay":{"left":null,"top":2}}""");
        var result = Store().Load().Settings;
        Assert.Multiple(() =>
        {
            Assert.That(result.LyricsLibraryPath, Is.EqualTo("C:\\Lyrics"));
            Assert.That(result.OverlayPosition, Is.Null);
        });
    }

    [Test]
    public void CompletelyMalformedSettingsAreHandledAndCanBeReplacedSafely()
    {
        Write("{");
        var store = Store();
        Assert.That(store.Load().Warning, Is.Not.Null);
        store.SaveOverlayPosition(new(10, 20));
        Assert.That(store.Load().Settings.OverlayPosition, Is.EqualTo(new OverlayPosition(10, 20)));
    }

    private ApplicationSettingsStore Store() => new(_settingsPath);
    private void Write(string json) => File.WriteAllText(_settingsPath, json);
}
