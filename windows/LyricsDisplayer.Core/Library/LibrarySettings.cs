using LyricsDisplayer.Core.Settings;

namespace LyricsDisplayer.Core.Library;

public sealed record LibraryPaths(
    string ApplicationDataPath,
    string SettingsPath,
    string LibraryPath,
    string IndexPath,
    bool UsesConfiguredPath,
    string? SettingsWarning = null)
{
    public static LibraryPaths Resolve(string applicationDataPath)
    {
        var settingsPath = Path.Combine(applicationDataPath, "settings.json");
        var defaultPath = Path.Combine(applicationDataPath, "Lyrics");
        var read = new ApplicationSettingsStore(settingsPath).Load();
        var configuredPath = read.Settings.LyricsLibraryPath;
        if (string.IsNullOrWhiteSpace(configuredPath))
            return new(applicationDataPath, settingsPath, defaultPath,
                Path.Combine(applicationDataPath, "library-index.db"), false, read.Warning);
        if (!Path.IsPathFullyQualified(configuredPath))
            return new(applicationDataPath, settingsPath, defaultPath,
                Path.Combine(applicationDataPath, "library-index.db"), false,
                "lyricsLibraryPath must be an absolute local or UNC path; the default is in use.");
        try
        {
            return new(applicationDataPath, settingsPath, Path.GetFullPath(configuredPath),
                Path.Combine(applicationDataPath, "library-index.db"), true, read.Warning);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new(applicationDataPath, settingsPath, defaultPath,
                Path.Combine(applicationDataPath, "library-index.db"), false,
                $"lyricsLibraryPath could not be resolved; the default is in use ({exception.GetType().Name}).");
        }
    }
}
