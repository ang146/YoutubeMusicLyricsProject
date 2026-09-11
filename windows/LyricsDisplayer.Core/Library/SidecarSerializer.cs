using System.Text;
using System.Text.Json;

namespace LyricsDisplayer.Core.Library;

public static class SidecarSerializer
{
    public const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static string Serialize(LyricsSidecar sidecar)
    {
        if (!TryValidate(sidecar, out var error)) throw new InvalidDataException(error);
        return JsonSerializer.Serialize(Normalise(sidecar), Options) + Environment.NewLine;
    }

    public static bool TryDeserialize(string json, out LyricsSidecar? sidecar, out string error)
    {
        sidecar = null;
        try
        {
            sidecar = JsonSerializer.Deserialize<LyricsSidecar>(json, Options);
        }
        catch (JsonException exception)
        {
            error = $"Invalid sidecar JSON: {exception.Message}";
            return false;
        }
        if (sidecar is null)
        {
            error = "Sidecar JSON produced no object.";
            return false;
        }
        if (!TryValidate(sidecar, out error))
        {
            sidecar = null;
            return false;
        }
        sidecar = Normalise(sidecar);
        return true;
    }

    public static bool TryRead(string path, out LyricsSidecar? sidecar, out string error)
    {
        try
        {
            return TryDeserialize(File.ReadAllText(path, Encoding.UTF8), out sidecar, out error);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            sidecar = null;
            error = $"Could not read sidecar: {exception.Message}";
            return false;
        }
    }

    private static LyricsSidecar Normalise(LyricsSidecar sidecar) => sidecar with
    {
        LocalTrackId = Guid.Parse(sidecar.LocalTrackId).ToString("D"),
        UserMetadata = UserTrackMetadata.Normalise(sidecar.UserMetadata.Title, sidecar.UserMetadata.Artist)
    };

    private static bool TryValidate(LyricsSidecar? sidecar, out string error)
    {
        if (sidecar is null) { error = "Sidecar is null."; return false; }
        if (sidecar.SchemaVersion != CurrentSchemaVersion)
        {
            error = sidecar.SchemaVersion > CurrentSchemaVersion
                ? $"Unsupported future sidecar schema {sidecar.SchemaVersion}."
                : $"Unsupported sidecar schema {sidecar.SchemaVersion}.";
            return false;
        }
        if (!Guid.TryParseExact(sidecar.LocalTrackId, "D", out _))
        { error = "localTrackId must be a UUID in D format."; return false; }
        if (sidecar.SourceAssociations is null || sidecar.SourceAssociations.Count == 0)
        { error = "At least one source association is required."; return false; }
        if (sidecar.UserMetadata is null || sidecar.Lyrics is null)
        { error = "userMetadata and lyrics are required."; return false; }
        foreach (var association in sidecar.SourceAssociations)
        {
            if (association?.Metadata is null || string.IsNullOrWhiteSpace(association.Source) ||
                string.IsNullOrWhiteSpace(association.SourceTrackId) ||
                string.IsNullOrWhiteSpace(association.Metadata.Title) ||
                string.IsNullOrWhiteSpace(association.Metadata.Artist) || association.Metadata.DurationMs < 0)
            { error = "Source association identity and metadata are invalid."; return false; }
        }
        if (string.IsNullOrWhiteSpace(sidecar.Lyrics.File) ||
            Path.GetFileName(sidecar.Lyrics.File) != sidecar.Lyrics.File ||
            sidecar.Lyrics.ImportedAtUtc.Offset != TimeSpan.Zero)
        { error = "Lyrics filename must be local to the track directory and importedAtUtc must be UTC."; return false; }
        error = string.Empty;
        return true;
    }
}
