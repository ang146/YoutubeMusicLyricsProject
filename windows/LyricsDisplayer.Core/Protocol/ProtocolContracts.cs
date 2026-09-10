using System.Text.Json.Serialization;

namespace LyricsDisplayer.Core.Protocol;

public static class ProtocolConstants
{
    public const int Version = 1;
    public const string PlaybackSnapshot = "playbackSnapshot";
    public const string LyricsSnapshot = "lyricsSnapshot";
    public const string DiagnosticLog = "diagnosticLog";
    public const string YouTubeMusicSource = "youtubeMusic";
    public const string PipeName = "LyricsDisplayer.NativeHost.v1";
}

public sealed record ProtocolEnvelope<TPayload>(
    [property: JsonPropertyName("protocolVersion")] int ProtocolVersion,
    [property: JsonPropertyName("messageType")] string MessageType,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("sourceSessionId")] string SourceSessionId,
    [property: JsonPropertyName("sequence")] long Sequence,
    [property: JsonPropertyName("sentAtUtc")] DateTimeOffset SentAtUtc,
    [property: JsonPropertyName("payload")] TPayload Payload);

public sealed record EnvelopeMetadata(
    int ProtocolVersion,
    string MessageType,
    string Source,
    string SourceSessionId,
    long Sequence,
    DateTimeOffset SentAtUtc);

public sealed record TrackInfo(
    [property: JsonPropertyName("sourceTrackId")] string SourceTrackId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("artist")] string Artist,
    [property: JsonPropertyName("album")] string? Album,
    [property: JsonPropertyName("durationMs")] long DurationMs);

public sealed record PlaybackState(
    [property: JsonPropertyName("positionMs")] long PositionMs,
    [property: JsonPropertyName("playing")] bool Playing,
    [property: JsonPropertyName("playbackRate")] double PlaybackRate);

public sealed record LyricsLine(
    [property: JsonPropertyName("startMs")] long StartMs,
    [property: JsonPropertyName("endMs")] long EndMs,
    [property: JsonPropertyName("text")] string Text);

public sealed record LyricsInfo(
    [property: JsonPropertyName("available")] bool Available,
    [property: JsonPropertyName("timed")] bool Timed,
    [property: JsonPropertyName("source")] string? Source,
    [property: JsonPropertyName("lines")] IReadOnlyList<LyricsLine> Lines);

public sealed record PlaybackSnapshotPayload(
    [property: JsonPropertyName("track")] TrackInfo Track,
    [property: JsonPropertyName("playback")] PlaybackState Playback,
    [property: JsonPropertyName("lyrics")] LyricsInfo Lyrics);

public sealed record DiagnosticLogPayload(
    [property: JsonPropertyName("level")] string Level,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("message")] string Message);

public sealed record LyricsSnapshotPayload(
    [property: JsonPropertyName("sourceTrackId")] string SourceTrackId,
    [property: JsonPropertyName("available")] bool Available,
    [property: JsonPropertyName("timed")] bool Timed,
    [property: JsonPropertyName("source")] string? Source,
    [property: JsonPropertyName("lines")] IReadOnlyList<LyricsLine> Lines,
    [property: JsonPropertyName("attribution")] string? Attribution = null);

public abstract record ProtocolMessage(EnvelopeMetadata Envelope, string RawJson);

public sealed record PlaybackSnapshotMessage(
    EnvelopeMetadata Envelope,
    PlaybackSnapshotPayload Payload,
    string RawJson) : ProtocolMessage(Envelope, RawJson);

public sealed record DiagnosticLogMessage(
    EnvelopeMetadata Envelope,
    DiagnosticLogPayload Payload,
    string RawJson) : ProtocolMessage(Envelope, RawJson);

public sealed record LyricsSnapshotMessage(
    EnvelopeMetadata Envelope,
    LyricsSnapshotPayload Payload,
    string RawJson) : ProtocolMessage(Envelope, RawJson);
