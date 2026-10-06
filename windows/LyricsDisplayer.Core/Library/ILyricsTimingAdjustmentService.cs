namespace LyricsDisplayer.Core.Library;

public sealed record EditorDocumentTimingAdjustmentResult(
    bool Succeeded,
    EditorDocument? Document = null,
    string? Error = null);

/// <summary>Pure, reusable timestamp mutation rules for lossless editor documents.</summary>
public interface ILyricsTimingAdjustmentService
{
    EditorDocumentTimingAdjustmentResult ShiftAll(EditorDocument document, long deltaMs);

    /// <summary>Shifts every timestamp on a stable editor lyric-row identity.</summary>
    EditorDocumentTimingAdjustmentResult ShiftLine(EditorDocument document, Guid lineId, long deltaMs);

    /// <summary>Shifts one stable timestamp occurrence within a lyric row.</summary>
    EditorDocumentTimingAdjustmentResult ShiftOccurrence(
        EditorDocument document, Guid lineId, Guid timestampId, long deltaMs);
}

/// <summary>Applies the Editor's safe, atomic timestamp-shift rules without owning document lifecycle.</summary>
public sealed class LyricsTimingAdjustmentService : ILyricsTimingAdjustmentService
{
    public EditorDocumentTimingAdjustmentResult ShiftAll(EditorDocument document, long deltaMs)
    {
        ArgumentNullException.ThrowIfNull(document);
        var updatedRows = new Dictionary<Guid, EditorLyricRow>();
        var foundTimestamp = false;

        foreach (var row in document.Rows)
        {
            var result = ShiftTimestamps(row, timestamp => true, deltaMs, ref foundTimestamp);
            if (!result.Succeeded) return Failure(result.Error ?? "The document timestamps could not be shifted safely.");
            if (result.Row is { } updated) updatedRows.Add(row.Id, updated);
        }

        if (!foundTimestamp)
            return Failure("The document contains no usable timestamps to shift.");
        if (updatedRows.Count == 0) return Success(document);

        var lines = document.PhysicalLines.Select(line =>
            line.LyricRow is { } row && updatedRows.TryGetValue(row.Id, out var updated)
                ? line with
                {
                    LyricRow = updated,
                    IsModified = !string.Equals(EditorDocumentCodec.SerializeLyricRow(updated),
                        line.RawText, StringComparison.Ordinal)
                }
                : line).ToArray();
        return Success(document with { PhysicalLines = lines });
    }

    public EditorDocumentTimingAdjustmentResult ShiftLine(EditorDocument document, Guid lineId, long deltaMs)
    {
        ArgumentNullException.ThrowIfNull(document);
        var line = document.PhysicalLines.FirstOrDefault(item => item.LyricRow?.Id == lineId);
        if (line?.LyricRow is not { } row)
            return Failure("The selected lyric row no longer exists.");

        var foundTimestamp = false;
        var result = ShiftTimestamps(row, timestamp => true, deltaMs, ref foundTimestamp);
        if (!result.Succeeded) return Failure(result.Error ?? "The selected row timestamps could not be shifted safely.");
        if (!foundTimestamp)
            return Failure("The selected lyric row contains no usable timestamps to shift.");
        if (result.Row is not { } updated) return Success(document);

        var lines = document.PhysicalLines.ToArray();
        var index = Array.IndexOf(lines, line);
        lines[index] = line with
        {
            LyricRow = updated,
            IsModified = !string.Equals(EditorDocumentCodec.SerializeLyricRow(updated),
                line.RawText, StringComparison.Ordinal)
        };
        return Success(document with { PhysicalLines = lines });
    }

    public EditorDocumentTimingAdjustmentResult ShiftOccurrence(
        EditorDocument document, Guid lineId, Guid timestampId, long deltaMs)
    {
        ArgumentNullException.ThrowIfNull(document);
        var physicalLines = document.PhysicalLines.ToArray();
        var lineIndex = Array.FindIndex(physicalLines, item => item.LyricRow?.Id == lineId);
        if (lineIndex < 0 || physicalLines[lineIndex].LyricRow is not { } row)
            return Failure("The selected lyric row no longer exists.");
        var timestampIndex = -1;
        for (var index = 0; index < row.Timestamps.Count; index++)
        {
            if (row.Timestamps[index].Id == timestampId)
            {
                timestampIndex = index;
                break;
            }
        }
        if (timestampIndex < 0)
            return Failure("The selected timestamp occurrence no longer exists.");

        var timestamp = row.Timestamps[timestampIndex];
        var parsed = ShiftTimestamp(timestamp, row.LyricsText, deltaMs);
        if (!parsed.Succeeded) return Failure(parsed.Error!);
        if (parsed.Timestamp == timestamp) return Success(document);

        var adjustedMs = ParseTimestamp(parsed.Timestamp.Value)!.Value;
        var neighbours = GetTimelineNeighbours(document, row.Id, timestamp.Id);
        if (neighbours.PreviousMs is { } previous && adjustedMs < previous)
            return Failure("Cannot move this lyric past the previous lyric.");
        if (neighbours.NextMs is { } next && adjustedMs > next)
            return Failure("Cannot move this lyric past the next lyric.");

        var timestamps = row.Timestamps.ToArray();
        timestamps[timestampIndex] = parsed.Timestamp;
        var updated = row with { Timestamps = timestamps };
        var line = physicalLines[lineIndex];
        physicalLines[lineIndex] = line with
        {
            LyricRow = updated,
            IsModified = !string.Equals(EditorDocumentCodec.SerializeLyricRow(updated),
                line.RawText, StringComparison.Ordinal)
        };
        return Success(document with { PhysicalLines = physicalLines });
    }

    private static (bool Succeeded, EditorLyricRow? Row, string? Error) ShiftTimestamps(
        EditorLyricRow row, Func<EditorTimestamp, bool> include, long deltaMs, ref bool foundTimestamp)
    {
        var timestamps = row.Timestamps.ToArray();
        var changed = false;
        for (var index = 0; index < timestamps.Length; index++)
        {
            var timestamp = timestamps[index];
            if (!include(timestamp) || string.IsNullOrWhiteSpace(timestamp.Value)) continue;
            foundTimestamp = true;
            var result = ShiftTimestamp(timestamp, row.LyricsText, deltaMs);
            if (!result.Succeeded) return (false, null, result.Error);
            if (result.Timestamp == timestamp) continue;
            timestamps[index] = result.Timestamp;
            changed = true;
        }
        return changed ? (true, row with { Timestamps = timestamps }, null) : (true, null, null);
    }

    private static (bool Succeeded, EditorTimestamp Timestamp, string? Error) ShiftTimestamp(
        EditorTimestamp timestamp, string lyricsText, long deltaMs)
    {
        if (!EditorDocumentCodec.TryParseTimestamp(timestamp.Value, out var timestampMs))
            return (false, timestamp, $"Cannot shift invalid timestamp '{timestamp.Value}'.");
        if (!LrcTimestampRewriter.TryShiftTimestamp(timestampMs, deltaMs, lyricsText,
                out var adjustedMs, out var error))
            return (false, timestamp, error);
        return (true, timestamp with { Value = EditorDocumentCodec.FormatTimestamp(adjustedMs) }, null);
    }

    private static (long? PreviousMs, long? NextMs) GetTimelineNeighbours(
        EditorDocument document, Guid rowId, Guid timestampId)
    {
        var ordered = document.Rows.SelectMany((row, rowOrder) => row.Timestamps
                .Select((timestamp, timestampOrder) => new
                {
                    row.Id,
                    Timestamp = timestamp,
                    Order = (rowOrder, timestampOrder),
                    ParsedValue = ParseTimestamp(timestamp.Value)
                }))
            .Where(item => item.ParsedValue is not null)
            .OrderBy(item => item.ParsedValue)
            .ThenBy(item => item.Order.rowOrder)
            .ThenBy(item => item.Order.timestampOrder)
            .ToArray();
        var targetIndex = Array.FindIndex(ordered, item => item.Id == rowId && item.Timestamp.Id == timestampId);
        return targetIndex < 0
            ? (null, null)
            : (targetIndex > 0 ? ordered[targetIndex - 1].ParsedValue!.Value : null,
                targetIndex + 1 < ordered.Length ? ordered[targetIndex + 1].ParsedValue!.Value : null);
    }

    private static long? ParseTimestamp(string value)
    {
        return EditorDocumentCodec.TryParseTimestamp(value, out var timestamp) ? timestamp : null;
    }

    private static EditorDocumentTimingAdjustmentResult Success(EditorDocument document) => new(true, document);
    private static EditorDocumentTimingAdjustmentResult Failure(string error) => new(false, Error: error);
}
