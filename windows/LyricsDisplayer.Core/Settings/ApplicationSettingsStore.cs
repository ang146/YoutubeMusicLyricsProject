using System.Text.Json;
using System.Text.Json.Nodes;
using LyricsDisplayer.Core.Library;

namespace LyricsDisplayer.Core.Settings;

public readonly record struct OverlayPosition(double Left, double Top)
{
    public bool IsFinite => double.IsFinite(Left) && double.IsFinite(Top);
}

public sealed record ApplicationSettings(string? LyricsLibraryPath, OverlayPosition? OverlayPosition);

public sealed record ApplicationSettingsReadResult(ApplicationSettings Settings, string? Warning = null);

public interface IOverlayPositionStore
{
    OverlayPosition? LoadOverlayPosition();
    void SaveOverlayPosition(OverlayPosition position);
}

public sealed class ApplicationSettingsStore(string settingsPath) : IOverlayPositionStore
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private readonly object _sync = new();

    public string SettingsPath { get; } = Path.GetFullPath(settingsPath);

    public ApplicationSettingsReadResult Load()
    {
        lock (_sync)
        {
            if (!File.Exists(SettingsPath))
                return new(new(null, null));

            try
            {
                var root = JsonNode.Parse(File.ReadAllText(SettingsPath)) as JsonObject;
                if (root is null)
                    return new(new(null, null), "settings.json must contain a JSON object; defaults are in use.");

                var warnings = new List<string>();
                var libraryPath = ReadLibraryPath(root, warnings);
                var overlayPosition = ReadOverlayPosition(root, warnings);
                return new(new(libraryPath, overlayPosition),
                    warnings.Count == 0 ? null : string.Join(" ", warnings));
            }
            catch (Exception exception) when (exception is JsonException or IOException or
                                               UnauthorizedAccessException or ArgumentException)
            {
                return new(new(null, null),
                    $"settings.json could not be read; defaults are in use ({exception.GetType().Name}).");
            }
        }
    }

    public OverlayPosition? LoadOverlayPosition() => Load().Settings.OverlayPosition;

    public void SaveOverlayPosition(OverlayPosition position)
    {
        if (!position.IsFinite)
            throw new ArgumentOutOfRangeException(nameof(position), "Overlay coordinates must be finite numbers.");

        lock (_sync)
        {
            var root = ReadObjectForUpdate();
            root["overlay"] = new JsonObject
            {
                ["left"] = position.Left,
                ["top"] = position.Top
            };
            AtomicFile.Replace(SettingsPath, root.ToJsonString(WriteOptions) + Environment.NewLine);
        }
    }

    private JsonObject ReadObjectForUpdate()
    {
        if (!File.Exists(SettingsPath)) return new JsonObject();
        try
        {
            return JsonNode.Parse(File.ReadAllText(SettingsPath)) as JsonObject ?? new JsonObject();
        }
        catch (Exception exception) when (exception is JsonException or IOException or
                                           UnauthorizedAccessException or ArgumentException)
        {
            return new JsonObject();
        }
    }

    private static string? ReadLibraryPath(JsonObject root, ICollection<string> warnings)
    {
        if (!root.TryGetPropertyValue("lyricsLibraryPath", out var value) || value is null)
            return null;
        if (value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var path))
            return path;
        warnings.Add("lyricsLibraryPath must be a string or null.");
        return null;
    }

    private static OverlayPosition? ReadOverlayPosition(JsonObject root, ICollection<string> warnings)
    {
        if (!root.TryGetPropertyValue("overlay", out var value) || value is null)
            return null;
        if (value is not JsonObject overlay ||
            !TryReadFiniteDouble(overlay["left"], out var left) ||
            !TryReadFiniteDouble(overlay["top"], out var top))
        {
            warnings.Add("overlay must contain finite numeric left and top values; the default position is in use.");
            return null;
        }
        return new(left, top);
    }

    private static bool TryReadFiniteDouble(JsonNode? value, out double number)
    {
        number = default;
        try
        {
            return value is JsonValue jsonValue && jsonValue.TryGetValue<double>(out number) && double.IsFinite(number);
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException or OverflowException)
        {
            return false;
        }
    }
}
