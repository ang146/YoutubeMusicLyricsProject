using System.Text.Json;

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
        if (!File.Exists(settingsPath))
            return new(applicationDataPath, settingsPath, defaultPath,
                Path.Combine(applicationDataPath, "library-index.db"), false);

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
            if (!document.RootElement.TryGetProperty("lyricsLibraryPath", out var value) ||
                value.ValueKind == JsonValueKind.Null ||
                value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()))
                return new(applicationDataPath, settingsPath, defaultPath,
                    Path.Combine(applicationDataPath, "library-index.db"), false);
            if (value.ValueKind != JsonValueKind.String || !Path.IsPathFullyQualified(value.GetString()!))
                return new(applicationDataPath, settingsPath, defaultPath,
                    Path.Combine(applicationDataPath, "library-index.db"), false,
                    "lyricsLibraryPath must be an absolute local or UNC path; the default is in use.");
            return new(applicationDataPath, settingsPath, Path.GetFullPath(value.GetString()!),
                Path.Combine(applicationDataPath, "library-index.db"), true);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new(applicationDataPath, settingsPath, defaultPath,
                Path.Combine(applicationDataPath, "library-index.db"), false,
                $"settings.json could not be read; the default is in use ({exception.GetType().Name}).");
        }
    }
}
