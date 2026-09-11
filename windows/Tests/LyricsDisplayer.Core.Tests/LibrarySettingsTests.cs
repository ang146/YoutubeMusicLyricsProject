using LyricsDisplayer.Core.Library;

namespace LyricsDisplayer.Core.Tests;

[TestFixture]
public sealed class LibrarySettingsTests
{
    private string _root = null!;
    [SetUp] public void SetUp() { _root = Path.Combine(Path.GetTempPath(), "LyricsDisplayerTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_root); }
    [TearDown] public void TearDown() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [TestCase(null)]
    [TestCase("{}")]
    [TestCase("{\"lyricsLibraryPath\":null}")]
    [TestCase("{\"lyricsLibraryPath\":\"   \"}")]
    public void MissingNullOrBlankUsesDefault(string? json)
    {
        if (json is not null) File.WriteAllText(Path.Combine(_root, "settings.json"), json);
        var paths = LibraryPaths.Resolve(_root);
        Assert.Multiple(() => { Assert.That(paths.LibraryPath, Is.EqualTo(Path.Combine(_root, "Lyrics"))); Assert.That(paths.UsesConfiguredPath, Is.False); });
    }

    [Test]
    public void AbsoluteLocalAndUncPathsAreAccepted()
    {
        var alternate = Path.Combine(_root, "alternate");
        File.WriteAllText(Path.Combine(_root, "settings.json"), System.Text.Json.JsonSerializer.Serialize(new { lyricsLibraryPath = alternate }));
        Assert.That(LibraryPaths.Resolve(_root).LibraryPath, Is.EqualTo(alternate));
        File.WriteAllText(Path.Combine(_root, "settings.json"), "{\"lyricsLibraryPath\":\"\\\\\\\\server\\\\share\\\\Lyrics\"}");
        Assert.That(LibraryPaths.Resolve(_root).LibraryPath, Does.StartWith("\\\\server\\share"));
    }

    [Test]
    public void MalformedOrRelativeConfigurationFallsBackSafely()
    {
        File.WriteAllText(Path.Combine(_root, "settings.json"), "{");
        Assert.That(LibraryPaths.Resolve(_root).SettingsWarning, Is.Not.Null);
        File.WriteAllText(Path.Combine(_root, "settings.json"), "{\"lyricsLibraryPath\":\"relative\"}");
        Assert.Multiple(() =>
        {
            Assert.That(LibraryPaths.Resolve(_root).UsesConfiguredPath, Is.False);
            Assert.That(LibraryPaths.Resolve(_root).SettingsWarning, Is.Not.Null);
        });
    }
}
