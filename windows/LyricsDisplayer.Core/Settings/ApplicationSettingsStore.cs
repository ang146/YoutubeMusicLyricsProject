using System.Text.Json;
using System.Text.Json.Nodes;
using LyricsDisplayer.Core.Library;

namespace LyricsDisplayer.Core.Settings;

public readonly record struct OverlayPosition(double Left, double Top)
{
    public bool IsFinite => double.IsFinite(Left) && double.IsFinite(Top);
}

public enum OverlayDisplayMode
{
    OneLine,
    TwoLines
}

public sealed record OverlayPreferences(
    bool Locked,
    bool ClickThrough,
    bool Topmost,
    OverlayDisplayMode DisplayMode,
    double Width)
{
    public const double DefaultWidth = 900;
    public const double MinimumWidth = 420;
    public const double MaximumWidth = 1800;

    public static OverlayPreferences Default { get; } =
        new(false, false, true, OverlayDisplayMode.TwoLines, DefaultWidth);

    public bool IsValid =>
        Enum.IsDefined(DisplayMode) && double.IsFinite(Width) &&
        Width >= MinimumWidth && Width <= MaximumWidth;
}

public sealed record ApplicationSettings(
    string? LyricsLibraryPath,
    OverlayPosition? OverlayPosition,
    OverlayPreferences OverlayPreferences);

public sealed record ApplicationSettingsReadResult(ApplicationSettings Settings, string? Warning = null);

public interface IOverlayPositionStore
{
    OverlayPosition? LoadOverlayPosition();
    void SaveOverlayPosition(OverlayPosition position);
}

public interface IOverlaySettingsStore : IOverlayPositionStore
{
    OverlayPreferences LoadOverlayPreferences();
    void SaveOverlayPreferences(OverlayPreferences preferences);
}

public sealed class ApplicationSettingsStore(string settingsPath) : IOverlaySettingsStore
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private readonly object _sync = new();

    public string SettingsPath { get; } = Path.GetFullPath(settingsPath);

    public ApplicationSettingsReadResult Load()
    {
        lock (_sync)
        {
            if (!File.Exists(SettingsPath))
                return new(new(null, null, OverlayPreferences.Default));

            try
            {
                var root = JsonNode.Parse(File.ReadAllText(SettingsPath)) as JsonObject;
                if (root is null)
                    return new(new(null, null, OverlayPreferences.Default),
                        "settings.json must contain a JSON object; defaults are in use.");

                var warnings = new List<string>();
                var libraryPath = ReadLibraryPath(root, warnings);
                var overlayPosition = ReadOverlayPosition(root, warnings);
                var overlayPreferences = ReadOverlayPreferences(root, warnings);
                return new(new(libraryPath, overlayPosition, overlayPreferences),
                    warnings.Count == 0 ? null : string.Join(" ", warnings));
            }
            catch (Exception exception) when (exception is JsonException or IOException or
                                               UnauthorizedAccessException or ArgumentException)
            {
                return new(new(null, null, OverlayPreferences.Default),
                    $"settings.json could not be read; defaults are in use ({exception.GetType().Name}).");
            }
        }
    }

    public OverlayPosition? LoadOverlayPosition() => Load().Settings.OverlayPosition;

    public OverlayPreferences LoadOverlayPreferences() => Load().Settings.OverlayPreferences;

    public void SaveOverlayPosition(OverlayPosition position)
    {
        if (!position.IsFinite)
            throw new ArgumentOutOfRangeException(nameof(position), "Overlay coordinates must be finite numbers.");

        lock (_sync)
        {
            var root = ReadObjectForUpdate();
            var overlay = GetOverlayForUpdate(root);
            overlay["left"] = position.Left;
            overlay["top"] = position.Top;
            AtomicFile.Replace(SettingsPath, root.ToJsonString(WriteOptions) + Environment.NewLine);
        }
    }

    public void SaveOverlayPreferences(OverlayPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (!preferences.IsValid)
            throw new ArgumentOutOfRangeException(nameof(preferences), "Overlay preferences are invalid.");

        lock (_sync)
        {
            var root = ReadObjectForUpdate();
            var overlay = GetOverlayForUpdate(root);
            overlay["locked"] = preferences.Locked;
            overlay["clickThrough"] = preferences.ClickThrough;
            overlay["topmost"] = preferences.Topmost;
            overlay["displayMode"] = preferences.DisplayMode == OverlayDisplayMode.OneLine
                ? "oneLine"
                : "twoLines";
            overlay["width"] = preferences.Width;
            AtomicFile.Replace(SettingsPath, root.ToJsonString(WriteOptions) + Environment.NewLine);
        }
    }

    private static JsonObject GetOverlayForUpdate(JsonObject root)
    {
        if (root["overlay"] is JsonObject overlay) return overlay;
        overlay = new JsonObject();
        root["overlay"] = overlay;
        return overlay;
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

    private static OverlayPreferences ReadOverlayPreferences(JsonObject root, ICollection<string> warnings)
    {
        if (!root.TryGetPropertyValue("overlay", out var value) || value is null)
            return OverlayPreferences.Default;
        if (value is not JsonObject overlay)
            return OverlayPreferences.Default;

        var defaults = OverlayPreferences.Default;
        var locked = ReadBoolean(overlay, "locked", defaults.Locked, warnings);
        var clickThrough = ReadBoolean(overlay, "clickThrough", defaults.ClickThrough, warnings);
        var topmost = ReadBoolean(overlay, "topmost", defaults.Topmost, warnings);
        var displayMode = ReadDisplayMode(overlay, warnings);
        var width = ReadWidth(overlay, warnings);
        return new(locked, clickThrough, topmost, displayMode, width);
    }

    private static bool ReadBoolean(
        JsonObject overlay,
        string propertyName,
        bool defaultValue,
        ICollection<string> warnings)
    {
        if (!overlay.TryGetPropertyValue(propertyName, out var node)) return defaultValue;
        if (node is JsonValue value && value.TryGetValue<bool>(out var result)) return result;
        warnings.Add($"overlay.{propertyName} must be a boolean; the default is in use.");
        return defaultValue;
    }

    private static OverlayDisplayMode ReadDisplayMode(JsonObject overlay, ICollection<string> warnings)
    {
        if (!overlay.TryGetPropertyValue("displayMode", out var node))
            return OverlayPreferences.Default.DisplayMode;
        if (node is JsonValue value && value.TryGetValue<string>(out var mode))
        {
            if (string.Equals(mode, "oneLine", StringComparison.OrdinalIgnoreCase))
                return OverlayDisplayMode.OneLine;
            if (string.Equals(mode, "twoLines", StringComparison.OrdinalIgnoreCase))
                return OverlayDisplayMode.TwoLines;
        }
        warnings.Add("overlay.displayMode must be 'oneLine' or 'twoLines'; the default is in use.");
        return OverlayPreferences.Default.DisplayMode;
    }

    private static double ReadWidth(JsonObject overlay, ICollection<string> warnings)
    {
        if (!overlay.TryGetPropertyValue("width", out var node)) return OverlayPreferences.Default.Width;
        if (TryReadFiniteDouble(node, out var width) &&
            width >= OverlayPreferences.MinimumWidth && width <= OverlayPreferences.MaximumWidth)
            return width;
        warnings.Add($"overlay.width must be between {OverlayPreferences.MinimumWidth} and " +
                     $"{OverlayPreferences.MaximumWidth}; the default is in use.");
        return OverlayPreferences.Default.Width;
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
