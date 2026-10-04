using System.Text.Json;
using System.Text.Json.Nodes;
using LyricsDisplayer.Core.Library;

namespace LyricsDisplayer.Core.Settings;

public readonly record struct OverlayPosition(double Left, double Top)
{
    public bool IsFinite => double.IsFinite(Left) && double.IsFinite(Top);
}

public enum LyricsContentMode
{
    OneLine,
    TwoLines,
    AllLyrics
}

public sealed record OverlayPreferences(
    bool Locked,
    bool ClickThrough,
    LyricsContentMode ContentMode,
    double Width,
    double Height)
{
    public const double DefaultWidth = 900;
    public const double DefaultHeight = 220;
    public const double MinimumWidth = 420;
    public const double MinimumHeight = 120;

    public static OverlayPreferences Default { get; } =
        new(false, false, LyricsContentMode.TwoLines, DefaultWidth, DefaultHeight);

    public bool IsValid =>
        Enum.IsDefined(ContentMode) && double.IsFinite(Width) &&
        Width > 0 && double.IsFinite(Height) && Height > 0;
}

public sealed record ApplicationSettings(
    string? LyricsLibraryPath,
    OverlayPosition? OverlayPosition,
    OverlayPreferences OverlayPreferences,
    bool CloseControlPanelToTray);

public sealed record ApplicationSettingsReadResult(ApplicationSettings Settings, string? Warning = null);

public interface IOverlayPositionStore
{
    OverlayPosition? LoadOverlayPosition();
    void SaveOverlayPosition(OverlayPosition position);
    void SaveOverlayGeometry(OverlayPosition position, OverlayPreferences preferences);
}

public interface IOverlaySettingsStore : IOverlayPositionStore
{
    OverlayPreferences LoadOverlayPreferences();
    void SaveOverlayPreferences(OverlayPreferences preferences);
    bool LoadCloseControlPanelToTray();
    void SaveCloseControlPanelToTray(bool value);
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
                return new(new(null, null, OverlayPreferences.Default, false));

            try
            {
                var root = JsonNode.Parse(File.ReadAllText(SettingsPath)) as JsonObject;
                if (root is null)
                    return new(new(null, null, OverlayPreferences.Default, false),
                        "settings.json must contain a JSON object; defaults are in use.");

                var warnings = new List<string>();
                var libraryPath = ReadLibraryPath(root, warnings);
                var overlayPosition = ReadOverlayPosition(root, warnings);
                var overlayPreferences = ReadOverlayPreferences(root, warnings);
                var closeToTray = ReadCloseControlPanelToTray(root, warnings);
                return new(new(libraryPath, overlayPosition, overlayPreferences, closeToTray),
                    warnings.Count == 0 ? null : string.Join(" ", warnings));
            }
            catch (Exception exception) when (exception is JsonException or IOException or
                                               UnauthorizedAccessException or ArgumentException)
            {
                return new(new(null, null, OverlayPreferences.Default, false),
                    $"settings.json could not be read; defaults are in use ({exception.GetType().Name}).");
            }
        }
    }

    public OverlayPosition? LoadOverlayPosition() => Load().Settings.OverlayPosition;

    public OverlayPreferences LoadOverlayPreferences() => Load().Settings.OverlayPreferences;

    public bool LoadCloseControlPanelToTray() => Load().Settings.CloseControlPanelToTray;

    public void SaveOverlayPosition(OverlayPosition position)
    {
        if (!position.IsFinite)
            throw new ArgumentOutOfRangeException(nameof(position), "Overlay coordinates must be finite numbers.");

        lock (_sync)
        {
            var root = ReadObjectForUpdate();
            var overlay = GetOverlayForUpdate(root);
            overlay.Remove("topmost");
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
            overlay.Remove("topmost");
            overlay["locked"] = preferences.Locked;
            overlay["clickThrough"] = preferences.ClickThrough;
            overlay["contentMode"] = preferences.ContentMode switch
            {
                LyricsContentMode.OneLine => "oneLine",
                LyricsContentMode.TwoLines => "twoLines",
                LyricsContentMode.AllLyrics => "allLyrics",
                _ => throw new ArgumentOutOfRangeException(nameof(preferences))
            };
            overlay.Remove("displayMode");
            overlay["width"] = preferences.Width;
            overlay["height"] = preferences.Height;
            AtomicFile.Replace(SettingsPath, root.ToJsonString(WriteOptions) + Environment.NewLine);
        }
    }

    public void SaveOverlayGeometry(OverlayPosition position, OverlayPreferences preferences)
    {
        if (!position.IsFinite)
            throw new ArgumentOutOfRangeException(nameof(position), "Overlay coordinates must be finite numbers.");
        ArgumentNullException.ThrowIfNull(preferences);
        if (!preferences.IsValid)
            throw new ArgumentOutOfRangeException(nameof(preferences), "Overlay preferences are invalid.");

        lock (_sync)
        {
            var root = ReadObjectForUpdate();
            var overlay = GetOverlayForUpdate(root);
            overlay.Remove("topmost");
            overlay["left"] = position.Left;
            overlay["top"] = position.Top;
            overlay["locked"] = preferences.Locked;
            overlay["clickThrough"] = preferences.ClickThrough;
            overlay["contentMode"] = preferences.ContentMode switch
            {
                LyricsContentMode.OneLine => "oneLine",
                LyricsContentMode.TwoLines => "twoLines",
                LyricsContentMode.AllLyrics => "allLyrics",
                _ => throw new ArgumentOutOfRangeException(nameof(preferences))
            };
            overlay.Remove("displayMode");
            overlay["width"] = preferences.Width;
            overlay["height"] = preferences.Height;
            AtomicFile.Replace(SettingsPath, root.ToJsonString(WriteOptions) + Environment.NewLine);
        }
    }

    public void SaveCloseControlPanelToTray(bool value)
    {
        lock (_sync)
        {
            var root = ReadObjectForUpdate();
            if (root["overlay"] is JsonObject overlay) overlay.Remove("topmost");
            if (root["application"] is not JsonObject application)
            {
                application = new JsonObject();
                root["application"] = application;
            }
            application["closeControlPanelToTray"] = value;
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
        var contentMode = ReadContentMode(overlay, warnings);
        var width = ReadDimension(overlay, "width", OverlayPreferences.Default.Width,
            double.Epsilon, warnings);
        var height = ReadDimension(overlay, "height", OverlayPreferences.Default.Height,
            double.Epsilon, warnings);
        return new(locked, clickThrough, contentMode, width, height);
    }

    private static bool ReadBoolean(
        JsonObject overlay,
        string propertyName,
        bool defaultValue,
        ICollection<string> warnings,
        string prefix = "overlay")
    {
        if (!overlay.TryGetPropertyValue(propertyName, out var node)) return defaultValue;
        if (node is JsonValue value && value.TryGetValue<bool>(out var result)) return result;
        warnings.Add($"{prefix}.{propertyName} must be a boolean; the default is in use.");
        return defaultValue;
    }

    private static LyricsContentMode ReadContentMode(JsonObject overlay, ICollection<string> warnings)
    {
        if (!overlay.TryGetPropertyValue("contentMode", out var node) &&
            !overlay.TryGetPropertyValue("displayMode", out node))
            return OverlayPreferences.Default.ContentMode;
        if (node is JsonValue value && value.TryGetValue<string>(out var mode))
        {
            if (string.Equals(mode, "oneLine", StringComparison.OrdinalIgnoreCase))
                return LyricsContentMode.OneLine;
            if (string.Equals(mode, "twoLines", StringComparison.OrdinalIgnoreCase))
                return LyricsContentMode.TwoLines;
            if (string.Equals(mode, "allLyrics", StringComparison.OrdinalIgnoreCase))
                return LyricsContentMode.AllLyrics;
        }
        warnings.Add("overlay.contentMode must be 'oneLine', 'twoLines', or 'allLyrics'; the default is in use.");
        return OverlayPreferences.Default.ContentMode;
    }

    private static bool ReadCloseControlPanelToTray(JsonObject root, ICollection<string> warnings)
    {
        if (!root.TryGetPropertyValue("application", out var node) || node is null) return false;
        if (node is not JsonObject application)
        {
            warnings.Add("application must be an object; the default is in use.");
            return false;
        }
        return ReadBoolean(application, "closeControlPanelToTray", false, warnings, "application");
    }

    private static double ReadDimension(
        JsonObject overlay,
        string propertyName,
        double defaultValue,
        double minimum,
        ICollection<string> warnings)
    {
        if (!overlay.TryGetPropertyValue(propertyName, out var node)) return defaultValue;
        if (TryReadFiniteDouble(node, out var dimension) && dimension >= minimum)
            return dimension;
        warnings.Add($"overlay.{propertyName} must be a finite number of at least {minimum}; the default is in use.");
        return defaultValue;
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
