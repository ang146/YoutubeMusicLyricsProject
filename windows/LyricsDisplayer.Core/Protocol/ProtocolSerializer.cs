using System.Globalization;
using System.Text.Json;

namespace LyricsDisplayer.Core.Protocol;

public static class ProtocolSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private static readonly string[] EnvelopeFields =
    [
        "protocolVersion", "messageType", "source", "sourceSessionId", "sequence", "sentAtUtc", "payload"
    ];

    public static string Serialize<TPayload>(ProtocolEnvelope<TPayload> envelope) =>
        JsonSerializer.Serialize(envelope, SerializerOptions);

    public static string Serialize(PlaybackSnapshotMessage message) => Serialize(
        new ProtocolEnvelope<PlaybackSnapshotPayload>(
            message.Envelope.ProtocolVersion,
            message.Envelope.MessageType,
            message.Envelope.Source,
            message.Envelope.SourceSessionId,
            message.Envelope.Sequence,
            message.Envelope.SentAtUtc,
            message.Payload));

    public static string Serialize(LyricsSnapshotMessage message) => Serialize(
        new ProtocolEnvelope<LyricsSnapshotPayload>(
            message.Envelope.ProtocolVersion, message.Envelope.MessageType, message.Envelope.Source,
            message.Envelope.SourceSessionId, message.Envelope.Sequence, message.Envelope.SentAtUtc,
            message.Payload));

    public static bool TryParse(string json, out ProtocolMessage? message, out string error)
    {
        message = null;
        error = string.Empty;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            error = $"Malformed JSON: {exception.Message}";
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "Protocol message must be a JSON object.";
                return false;
            }

            foreach (var field in EnvelopeFields)
            {
                if (!root.TryGetProperty(field, out _))
                {
                    error = $"Required envelope field '{field}' is missing.";
                    return false;
                }
            }

            var versionElement = root.GetProperty("protocolVersion");
            if (versionElement.ValueKind != JsonValueKind.Number || !versionElement.TryGetInt32(out var version))
            {
                error = "protocolVersion must be an integer.";
                return false;
            }

            if (version != ProtocolConstants.Version)
            {
                error = $"Unsupported protocolVersion {version}; expected {ProtocolConstants.Version}.";
                return false;
            }

            if (!TryRequiredString(root, "messageType", out var messageType, out error) ||
                !TryRequiredString(root, "source", out var source, out error) ||
                !TryRequiredString(root, "sourceSessionId", out var sourceSessionId, out error))
            {
                return false;
            }

            if (!Guid.TryParse(sourceSessionId, out _))
            {
                error = "sourceSessionId must be a UUID.";
                return false;
            }

            var sequenceElement = root.GetProperty("sequence");
            if (sequenceElement.ValueKind != JsonValueKind.Number ||
                !sequenceElement.TryGetInt64(out var sequence) || sequence < 0)
            {
                error = "sequence must be a non-negative integer.";
                return false;
            }

            var timestampElement = root.GetProperty("sentAtUtc");
            if (timestampElement.ValueKind != JsonValueKind.String ||
                !DateTimeOffset.TryParse(timestampElement.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var sentAtUtc) || sentAtUtc.Offset != TimeSpan.Zero)
            {
                error = "sentAtUtc must be an ISO-8601 UTC timestamp.";
                return false;
            }

            var payload = root.GetProperty("payload");
            if (payload.ValueKind != JsonValueKind.Object)
            {
                error = "payload must be a JSON object.";
                return false;
            }

            var metadata = new EnvelopeMetadata(version, messageType, source, sourceSessionId, sequence, sentAtUtc);
            try
            {
                switch (messageType)
                {
                    case ProtocolConstants.LyricsSnapshot:
                        if (!ValidateLyricsSnapshot(payload, out error))
                        {
                            return false;
                        }
                        message = new LyricsSnapshotMessage(metadata,
                            payload.Deserialize<LyricsSnapshotPayload>(SerializerOptions)!, json);
                        return true;

                    case ProtocolConstants.PlaybackSnapshot:
                        if (!ValidatePlaybackPayload(payload, out error))
                        {
                            return false;
                        }

                        var snapshot = payload.Deserialize<PlaybackSnapshotPayload>(SerializerOptions);
                        if (snapshot is null)
                        {
                            error = "playbackSnapshot payload could not be deserialised.";
                            return false;
                        }

                        message = new PlaybackSnapshotMessage(metadata, snapshot, json);
                        return true;

                    case ProtocolConstants.DiagnosticLog:
                        if (!ValidateDiagnosticPayload(payload, out error))
                        {
                            return false;
                        }

                        var diagnostic = payload.Deserialize<DiagnosticLogPayload>(SerializerOptions);
                        if (diagnostic is null)
                        {
                            error = "diagnosticLog payload could not be deserialised.";
                            return false;
                        }

                        message = new DiagnosticLogMessage(metadata, diagnostic, json);
                        return true;

                    default:
                        error = $"Unsupported messageType '{messageType}'.";
                        return false;
                }
            }
            catch (JsonException exception)
            {
                error = $"Invalid {messageType} payload: {exception.Message}";
                return false;
            }
        }
    }

    private static bool ValidatePlaybackPayload(JsonElement payload, out string error)
    {
        error = string.Empty;
        if (!TryObject(payload, "track", out var track, out error) ||
            !TryObject(payload, "playback", out var playback, out error) ||
            !TryObject(payload, "lyrics", out var lyrics, out error))
        {
            return false;
        }

        if (!TryRequiredString(track, "sourceTrackId", out _, out error) ||
            !TryRequiredString(track, "title", out _, out error) ||
            !TryRequiredString(track, "artist", out _, out error) ||
            !TryStringOrNull(track, "album", out error) ||
            !TryNonNegativeInteger(track, "durationMs", out error) ||
            !TryNonNegativeInteger(playback, "positionMs", out error) ||
            !TryBoolean(playback, "playing", out error) ||
            !TryFiniteNumber(playback, "playbackRate", out error) ||
            !TryBoolean(lyrics, "available", out error) ||
            !TryBoolean(lyrics, "timed", out error))
        {
            return false;
        }

        if (!lyrics.TryGetProperty("source", out var lyricsSource) ||
            lyricsSource.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
        {
            error = "Required field 'source' must be a string or null.";
            return false;
        }

        if (!lyrics.TryGetProperty("lines", out var lines) || lines.ValueKind != JsonValueKind.Array)
        {
            error = "Required field 'lines' must be an array.";
            return false;
        }

        foreach (var line in lines.EnumerateArray())
        {
            if (line.ValueKind != JsonValueKind.Object ||
                !TryNonNegativeInteger(line, "startMs", out error) ||
                !TryNonNegativeInteger(line, "endMs", out error) ||
                !TryRequiredString(line, "text", out _, out error))
            {
                error = string.IsNullOrEmpty(error) ? "Every lyrics line must be an object." : error;
                return false;
            }

            var start = line.GetProperty("startMs").GetInt64();
            var end = line.GetProperty("endMs").GetInt64();
            if (end < start)
            {
                error = "A lyrics line endMs must not be earlier than startMs.";
                return false;
            }
        }

        return true;
    }

    private static bool ValidateLyricsSnapshot(JsonElement payload, out string error)
    {
        if (!TryRequiredString(payload, "sourceTrackId", out _, out error) ||
            !TryBoolean(payload, "available", out error) ||
            !TryBoolean(payload, "timed", out error) ||
            !TryStringOrNull(payload, "source", out error))
        {
            return false;
        }
        if (payload.TryGetProperty("attribution", out _) &&
            !TryStringOrNull(payload, "attribution", out error)) return false;
        if (!payload.TryGetProperty("lines", out var lines) || lines.ValueKind != JsonValueKind.Array)
        {
            error = "Required field 'lines' must be an array.";
            return false;
        }
        var available = payload.GetProperty("available").GetBoolean();
        var timed = payload.GetProperty("timed").GetBoolean();
        if ((timed && (!available || lines.GetArrayLength() == 0)) ||
            (!timed && lines.GetArrayLength() != 0))
        {
            error = "Timed lyrics require available=true and lines; untimed/unavailable lyrics require empty lines.";
            return false;
        }
        long previousStart = -1;
        foreach (var line in lines.EnumerateArray())
        {
            if (line.ValueKind != JsonValueKind.Object)
            {
                error = "Every lyrics line must be an object.";
                return false;
            }
            if (!TryNonNegativeInteger(line, "startMs", out error) ||
                !TryNonNegativeInteger(line, "endMs", out error)) return false;
            // Empty text can represent a provider-supplied instrumental cue.
            if (!line.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String)
            {
                error = "Required field 'text' must be a string.";
                return false;
            }
            var start = line.GetProperty("startMs").GetInt64();
            if (start < previousStart || line.GetProperty("endMs").GetInt64() < start)
            {
                error = "Lyrics lines must be ordered by startMs and endMs must not precede startMs.";
                return false;
            }
            previousStart = start;
        }
        return true;
    }

    private static bool ValidateDiagnosticPayload(JsonElement payload, out string error)
    {
        if (!TryRequiredString(payload, "level", out var level, out error) ||
            !TryRequiredString(payload, "category", out _, out error) ||
            !TryRequiredString(payload, "message", out _, out error))
        {
            return false;
        }

        if (level is not ("Debug" or "Information" or "Warning" or "Error"))
        {
            error = $"Unsupported diagnostic log level '{level}'.";
            return false;
        }

        return true;
    }

    private static bool TryObject(JsonElement parent, string name, out JsonElement value, out string error)
    {
        if (!parent.TryGetProperty(name, out value) || value.ValueKind != JsonValueKind.Object)
        {
            error = $"Required field '{name}' must be an object.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryRequiredString(JsonElement parent, string name, out string value, out string error)
    {
        value = string.Empty;
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(element.GetString()))
        {
            error = $"Required field '{name}' must be a non-empty string.";
            return false;
        }

        value = element.GetString()!;
        error = string.Empty;
        return true;
    }

    private static bool TryStringOrNull(JsonElement parent, string name, out string error)
    {
        if (!parent.TryGetProperty(name, out var element) ||
            element.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
        {
            error = $"Required field '{name}' must be a string or null.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryNonNegativeInteger(JsonElement parent, string name, out string error)
    {
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Number ||
            !element.TryGetInt64(out var value) || value < 0)
        {
            error = $"Required field '{name}' must be a non-negative integer.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryBoolean(JsonElement parent, string name, out string error)
    {
        if (!parent.TryGetProperty(name, out var element) ||
            element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            error = $"Required field '{name}' must be a boolean.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryFiniteNumber(JsonElement parent, string name, out string error)
    {
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Number ||
            !element.TryGetDouble(out var value) ||
            !double.IsFinite(value) || value <= 0)
        {
            error = $"Required field '{name}' must be a positive finite number.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
