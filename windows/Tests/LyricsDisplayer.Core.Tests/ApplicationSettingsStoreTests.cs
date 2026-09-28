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
        var preferences = new OverlayPreferences(true, true, false, LyricsContentMode.OneLine, 1230, 377);
        store.SaveOverlayPreferences(preferences);

        var loaded = store.Load().Settings;
        using var document = JsonDocument.Parse(File.ReadAllText(_settingsPath));
        Assert.Multiple(() =>
        {
            Assert.That(loaded.OverlayPreferences, Is.EqualTo(preferences));
            Assert.That(document.RootElement.GetProperty("overlay").GetProperty("height").GetDouble(), Is.EqualTo(377));
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
                Is.EqualTo(new OverlayPreferences(true, true, false, LyricsContentMode.OneLine, 1100, OverlayPreferences.DefaultHeight)));
            Assert.That(document.RootElement.GetProperty("overlay").GetProperty("future").GetInt32(), Is.EqualTo(1));
        });
    }

    [Test]
    public void GeometrySaveAtomicallyPersistsPositionWidthHeightAndPreferences()
    {
        Write("""{"lyricsLibraryPath":"C:\\Lyrics","overlay":{"future":"kept"},"application":{"keep":true}}""");
        var store = Store();
        var preferences = OverlayPreferences.Default with
        {
            Width = 913,
            Height = 377,
            ContentMode = LyricsContentMode.AllLyrics,
            ClickThrough = true
        };
        store.SaveOverlayGeometry(new(48.5, 112), preferences);

        var loaded = store.Load().Settings;
        using var json = JsonDocument.Parse(File.ReadAllText(_settingsPath));
        Assert.Multiple(() =>
        {
            Assert.That(loaded.OverlayPosition, Is.EqualTo(new OverlayPosition(48.5, 112)));
            Assert.That(loaded.OverlayPreferences, Is.EqualTo(preferences));
            Assert.That(json.RootElement.GetProperty("lyricsLibraryPath").GetString(), Is.EqualTo("C:\\Lyrics"));
            Assert.That(json.RootElement.GetProperty("overlay").GetProperty("future").GetString(), Is.EqualTo("kept"));
            Assert.That(json.RootElement.GetProperty("application").GetProperty("keep").GetBoolean(), Is.True);
        });
    }

    [TestCase("{\"overlay\":{\"width\":12,\"height\":377}}", 900, 377)]
    [TestCase("{\"overlay\":{\"width\":913,\"height\":2}}", 913, 220)]
    [TestCase("{\"overlay\":{\"width\":1e999,\"height\":377}}", 900, 377)]
    public void InvalidDimensionsFallBackIndependently(string json, double expectedWidth, double expectedHeight)
    {
        Write(json);
        var preferences = Store().Load().Settings.OverlayPreferences;
        Assert.Multiple(() =>
        {
            Assert.That(preferences.Width, Is.EqualTo(expectedWidth));
            Assert.That(preferences.Height, Is.EqualTo(expectedHeight));
        });
    }

    [Test]
    public void MissingPreferenceFieldsUseCompatibilityDefaults()
    {
        Write("""{"overlay":{"left":1,"top":2}}""");
        Assert.That(Store().Load().Settings.OverlayPreferences, Is.EqualTo(OverlayPreferences.Default));
    }

    [Test]
    public void AllLyricsContentModeAndCloseToTrayRoundTripWithoutLosingOtherSettings()
    {
        Write("""{"lyricsLibraryPath":"C:\\Lyrics","overlay":{"left":100,"top":200,"future":true},"application":{"futureApp":9}}""");
        var store = Store();
        store.SaveOverlayPreferences(OverlayPreferences.Default with { ContentMode = LyricsContentMode.AllLyrics, Width = 1111 });
        store.SaveCloseControlPanelToTray(true);

        var loaded = store.Load();
        using var document = JsonDocument.Parse(File.ReadAllText(_settingsPath));
        Assert.Multiple(() =>
        {
            Assert.That(loaded.Settings.OverlayPreferences.ContentMode, Is.EqualTo(LyricsContentMode.AllLyrics));
            Assert.That(loaded.Settings.OverlayPreferences.Width, Is.EqualTo(1111));
            Assert.That(loaded.Settings.OverlayPosition, Is.EqualTo(new OverlayPosition(100, 200)));
            Assert.That(loaded.Settings.CloseControlPanelToTray, Is.True);
            Assert.That(loaded.Settings.LyricsLibraryPath, Is.EqualTo("C:\\Lyrics"));
            Assert.That(document.RootElement.GetProperty("overlay").GetProperty("future").GetBoolean(), Is.True);
            Assert.That(document.RootElement.GetProperty("application").GetProperty("futureApp").GetInt32(), Is.EqualTo(9));
            Assert.That(document.RootElement.GetProperty("overlay").GetProperty("contentMode").GetString(),
                Is.EqualTo("allLyrics"));
        });
    }

    [Test]
    public void ExistingDisplayModeAndMissingCloseToTrayUseSafeMigrationDefaults()
    {
        Write("""{"overlay":{"left":1,"top":2,"displayMode":"oneLine"}}""");
        var settings = Store().Load().Settings;
        Assert.Multiple(() =>
        {
            Assert.That(settings.OverlayPreferences.ContentMode, Is.EqualTo(LyricsContentMode.OneLine));
            Assert.That(settings.CloseControlPanelToTray, Is.False);
        });
    }

    [Test]
    public void MalformedCloseToTrayPreferenceDefaultsToApplicationExitBehavior()
    {
        Write("""{"application":{"closeControlPanelToTray":"yes"}}""");
        var result = Store().Load();
        Assert.Multiple(() =>
        {
            Assert.That(result.Settings.CloseControlPanelToTray, Is.False);
            Assert.That(result.Warning, Does.Contain("application.closeControlPanelToTray"));
        });
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
