using System.Text.Json.Serialization;
using LyricsDisplayer.Core.Protocol;

namespace LyricsDisplayer.Core.Library;

public sealed record SourceTrackMetadata(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("artist")] string Artist,
    [property: JsonPropertyName("album")] string? Album,
    [property: JsonPropertyName("durationMs")] long DurationMs);

public sealed record SourceTrackAssociation(
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("sourceTrackId")] string SourceTrackId,
    [property: JsonPropertyName("metadata")] SourceTrackMetadata Metadata);

public sealed record UserTrackMetadata(
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("artist")] string? Artist)
{
    public static UserTrackMetadata Normalise(string? title, string? artist) =>
        new(NormaliseOverride(title), NormaliseOverride(artist));

    private static string? NormaliseOverride(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record EffectiveTrackMetadata(string Title, string Artist)
{
    public static EffectiveTrackMetadata From(
        SourceTrackMetadata storedSource,
        UserTrackMetadata user,
        SourceTrackMetadata? currentSource = null) =>
        new(user.Title ?? currentSource?.Title ?? storedSource.Title,
            user.Artist ?? currentSource?.Artist ?? storedSource.Artist);
}

public sealed record LyricsAsset(
    [property: JsonPropertyName("file")] string File,
    [property: JsonPropertyName("source")] string? Source,
    [property: JsonPropertyName("attribution")] string? Attribution,
    [property: JsonPropertyName("importedAtUtc")] DateTimeOffset ImportedAtUtc);

public sealed record LyricsSidecar(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("localTrackId")] string LocalTrackId,
    [property: JsonPropertyName("sourceAssociations")] IReadOnlyList<SourceTrackAssociation> SourceAssociations,
    [property: JsonPropertyName("userMetadata")] UserTrackMetadata UserMetadata,
    [property: JsonPropertyName("lyrics")] LyricsAsset Lyrics);

public sealed record LocalTrackRecord(
    string LocalTrackId,
    UserTrackMetadata UserMetadata,
    string LyricsRelativePath,
    string SidecarRelativePath,
    string? LyricsSource,
    string? Attribution,
    IReadOnlyList<SourceTrackAssociation> SourceAssociations);

public sealed record LocalLyricsDocument(
    LocalTrackRecord Record,
    LyricsSidecar Sidecar,
    IReadOnlyList<LyricsLine> Lines,
    EffectiveTrackMetadata EffectiveMetadata);

public enum LocalLyricsLookupStatus
{
    Found,
    NotFound,
    LibraryUnavailable,
    IndexUnavailable,
    BrokenRecord,
    DuplicateAssociation
}

public sealed record LocalLyricsLookupResult(
    LocalLyricsLookupStatus Status,
    LocalLyricsDocument? Document = null,
    string? Error = null);

public enum LyricsImportStatus
{
    Imported,
    AlreadyLocal,
    NotTimed,
    LibraryUnavailable,
    IndexUnavailable,
    BlockedByBrokenRecord,
    FilesPersistedIndexFailed,
    Failed
}

public sealed record LyricsImportResult(
    LyricsImportStatus Status,
    LocalLyricsDocument? Document = null,
    string? LocalTrackId = null,
    string? Error = null);

public sealed record LibraryScanResult(
    bool Completed,
    int IndexedTracks,
    int InvalidRecords,
    int DuplicateAssociations,
    string? Error = null);
