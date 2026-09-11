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
