using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LyricsDisplayer.Core.Library;

public enum EditorEntryKind
{
    Lyric,
    Metadata,
    UnknownTag,
    Blank,
    Unsupported
}

public enum EditorColumn
{
    Timestamp,
    Lyrics
}

public enum EditorHotkeyScope
{
    Global,
    Application,
    Editor
}

public sealed record EditorSelection(Guid? SelectedRowId, EditorColumn SelectedColumn, int? SelectedTimestampIndex = null);

public sealed record EditorTimestamp(Guid Id, string Value)
{
    public bool IsValid => EditorDocumentCodec.TryParseTimestamp(Value, out _);
}

public sealed record EditorLyricRow(Guid Id, string LyricsText, IReadOnlyList<EditorTimestamp> Timestamps,
    string LeadingWhitespace = "");

public sealed record EditorPhysicalLine(
    Guid Id,
    EditorEntryKind Kind,
    string RawText,
    string LineEnding,
    EditorLyricRow? LyricRow = null,
    string? Diagnostic = null,
    bool IsModified = false);

public sealed record EditorValidationDiagnostic(Guid? RowId, int? PhysicalLine, int? TimestampIndex, string Message);

public enum EditorAssetStatus
{
    Ready,
    Missing,
    Conflict,
    Saved,
    PartialFailure,
    Failed
}

public sealed record EditorAssetSnapshot(
    LocalTrackRecord Record,
    LyricsSidecar Sidecar,
    string LyricsPath,
    string SidecarPath,
    string LrcContent,
    string LrcHash,
    string SidecarHash,
    bool HasUtf8Bom);

public sealed record EditorAssetResult(
    EditorAssetStatus Status,
    EditorAssetSnapshot? Asset = null,
    string? Error = null)
{
    public bool Succeeded => Status is EditorAssetStatus.Ready or EditorAssetStatus.Saved;
}

public sealed record EditorDocument(
    IReadOnlyList<EditorPhysicalLine> PhysicalLines,
    string OriginalContent,
    string? SourceHash,
    string DefaultLineEnding)
{
    public IReadOnlyList<EditorLyricRow> Rows => PhysicalLines
        .Where(line => line.Kind == EditorEntryKind.Lyric && line.LyricRow is not null)
        .Select(line => line.LyricRow!)
        .ToArray();

    public IReadOnlyList<EditorValidationDiagnostic> Validate()
    {
        var diagnostics = new List<EditorValidationDiagnostic>();
        long? previous = null;
        for (var lineIndex = 0; lineIndex < PhysicalLines.Count; lineIndex++)
        {
            var line = PhysicalLines[lineIndex];
            if (line.Diagnostic is not null)
                diagnostics.Add(new(line.LyricRow?.Id, lineIndex + 1, null, line.Diagnostic));
            if (line.LyricRow is not { } row) continue;

            for (var index = 0; index < row.Timestamps.Count; index++)
            {
                var timestamp = row.Timestamps[index];
                if (!EditorDocumentCodec.TryParseTimestamp(timestamp.Value, out var value))
                {
                    diagnostics.Add(new(row.Id, lineIndex + 1, index, $"Timestamp '{timestamp.Value}' is invalid."));
                    continue;
                }
                if (previous is not null && value < previous.Value)
                    diagnostics.Add(new(row.Id, lineIndex + 1, index, "Timestamp is earlier than the preceding document timestamp."));
                previous = value;
            }
        }
        return diagnostics;
    }
}

/// <summary>Lossless physical-line LRC reader/writer used by the built-in editing buffer.</summary>
public static partial class EditorDocumentCodec
{
    [GeneratedRegex(@"^(?<leading>\s*)(?<tokens>(?:\[[^\]\r\n]*\])+)(?<text>.*)$")]
    private static partial Regex TimestampPrefixRegex();

    [GeneratedRegex(@"\[(?<value>[^\]\r\n]*)\]")]
    private static partial Regex BracketTokenRegex();

    [GeneratedRegex(@"^\s*\[(?<name>[^\]:]+):[^\]]*\]")]
    private static partial Regex TagPrefixRegex();

    [GeneratedRegex(@"^(?<minutes>\d+):(?<seconds>\d{2})\.(?<fraction>\d{2,3})$")]
    private static partial Regex TimestampValueRegex();

    private static readonly HashSet<string> KnownMetadata = new(StringComparer.OrdinalIgnoreCase)
        { "ar", "ti", "al", "by", "offset", "re", "ve", "length" };

    public static EditorDocument Load(string content, string? sourceHash = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        var split = SplitPhysicalLines(content);
        var lines = new List<EditorPhysicalLine>(split.Count);
        var defaultEnding = split.Select(item => item.Ending).FirstOrDefault(item => item.Length > 0) ?? "\n";
        foreach (var (text, ending, lineNumber) in split)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                lines.Add(new(Guid.NewGuid(), EditorEntryKind.Blank, text, ending));
                continue;
            }

            var tag = TagPrefixRegex().Match(text);
            if (tag.Success && KnownMetadata.Contains(tag.Groups["name"].Value))
            {
                lines.Add(new(Guid.NewGuid(), EditorEntryKind.Metadata, text, ending));
                continue;
            }

            if (tag.Success && !KnownMetadata.Contains(tag.Groups["name"].Value) &&
                !tag.Groups["name"].Value.All(char.IsDigit))
            {
                var firstBracket = BracketTokenRegex().Match(text);
                if (firstBracket.Success && !TryParseTimestamp(firstBracket.Groups["value"].Value, out _))
                {
                    lines.Add(new(Guid.NewGuid(), EditorEntryKind.UnknownTag, text, ending));
                    continue;
                }
            }

            var prefix = TimestampPrefixRegex().Match(text);
            if (prefix.Success)
            {
                var tokens = BracketTokenRegex().Matches(prefix.Groups["tokens"].Value);
                var timestamps = new List<EditorTimestamp>(tokens.Count);
                var allValid = tokens.Count > 0;
                foreach (Match token in tokens)
                {
                    var value = token.Groups["value"].Value;
                    allValid &= TryParseTimestamp(value, out _);
                    timestamps.Add(new(Guid.NewGuid(), value));
                }
                if (allValid)
                {
                    var row = new EditorLyricRow(Guid.NewGuid(), prefix.Groups["text"].Value, timestamps,
                        prefix.Groups["leading"].Value);
                    lines.Add(new(Guid.NewGuid(), EditorEntryKind.Lyric, text, ending, row));
                    continue;
                }

                lines.Add(new(Guid.NewGuid(), EditorEntryKind.Unsupported, text, ending,
                    Diagnostic: $"Physical line {lineNumber} contains malformed timestamp syntax and was preserved unchanged."));
                continue;
            }

            if (text.Contains('[') && text.Contains(':'))
            {
                lines.Add(new(Guid.NewGuid(), EditorEntryKind.Unsupported, text, ending,
                    Diagnostic: $"Physical line {lineNumber} contains unsupported bracketed content and was preserved unchanged."));
                continue;
            }

            var untimedRow = new EditorLyricRow(Guid.NewGuid(), text, []);
            lines.Add(new(Guid.NewGuid(), EditorEntryKind.Lyric, text, ending, untimedRow));
        }
        return new(lines, content, sourceHash, defaultEnding);
    }

    public static string Serialize(EditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var builder = new StringBuilder();
        foreach (var line in document.PhysicalLines)
        {
            if (line.Kind == EditorEntryKind.Lyric && line.IsModified && line.LyricRow is { } row)
            {
                builder.Append(SerializeLyricRow(row));
            }
            else builder.Append(line.RawText);
            builder.Append(line.LineEnding);
        }
        return builder.ToString();
    }

    public static bool TryParseTimestamp(string value, out long milliseconds)
    {
        milliseconds = 0;
        var match = TimestampValueRegex().Match(RemoveOuterBrackets(value.Trim()));
        if (!match.Success ||
            !long.TryParse(match.Groups["minutes"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) ||
            !int.TryParse(match.Groups["seconds"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) || seconds >= 60)
            return false;
        var fractionText = match.Groups["fraction"].Value;
        var fraction = int.Parse(fractionText, CultureInfo.InvariantCulture) * (fractionText.Length == 2 ? 10 : 1);
        try
        {
            milliseconds = checked(minutes * 60_000 + seconds * 1_000 + fraction);
            return true;
        }
        catch (OverflowException) { return false; }
    }

    public static string FormatTimestamp(long milliseconds)
    {
        if (milliseconds < 0) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        return string.Create(CultureInfo.InvariantCulture,
            $"{milliseconds / 60_000:00}:{milliseconds % 60_000 / 1_000:00}.{milliseconds % 1_000:000}");
    }

    private static string RemoveOuterBrackets(string value) =>
        value.Length >= 2 && value[0] == '[' && value[^1] == ']' ? value[1..^1] : value;

    internal static string SerializeLyricRow(EditorLyricRow row)
    {
        var builder = new StringBuilder();
        builder.Append(row.LeadingWhitespace);
        foreach (var timestamp in row.Timestamps)
            builder.Append('[').Append(RemoveOuterBrackets(timestamp.Value)).Append(']');
        return builder.Append(row.LyricsText).ToString();
    }

    private static List<(string Text, string Ending, int LineNumber)> SplitPhysicalLines(string content)
    {
        var result = new List<(string, string, int)>();
        var start = 0;
        var lineNumber = 1;
        for (var index = 0; index < content.Length; index++)
        {
            if (content[index] is not ('\r' or '\n')) continue;
            var end = index + 1;
            if (content[index] == '\r' && end < content.Length && content[end] == '\n') end++;
            result.Add((content[start..index], content[index..end], lineNumber++));
            start = end;
            index = end - 1;
        }
        if (start < content.Length) result.Add((content[start..], string.Empty, lineNumber));
        return result;
    }
}

/// <summary>Command-oriented mutable editor buffer with snapshot undo/redo and clean-baseline dirty tracking.</summary>
public sealed class EditorDocumentBuffer
{
    private sealed record State(EditorDocument Document, UserTrackMetadata Metadata);
    private readonly Stack<State> _undo = new();
    private readonly Stack<State> _redo = new();
    private string _baselineContent;
    private UserTrackMetadata _baselineMetadata;

    public EditorDocument Document { get; private set; }
    public UserTrackMetadata Metadata { get; private set; }
    public bool IsDirty => EditorDocumentCodec.Serialize(Document) != _baselineContent || Metadata != _baselineMetadata;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public EditorDocumentBuffer(EditorDocument document, UserTrackMetadata metadata)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Metadata = UserTrackMetadata.Normalise(metadata?.Title, metadata?.Artist);
        _baselineContent = EditorDocumentCodec.Serialize(document);
        _baselineMetadata = Metadata;
    }

    public void SetLyrics(Guid rowId, string value) => Change(document => UpdateRow(document, rowId,
        row => row with { LyricsText = value ?? string.Empty }));

    public bool SetTimestamp(Guid rowId, int timestampIndex, string value)
    {
        if (timestampIndex < 0) return false;
        var row = FindRow(Document, rowId);
        if (row is null || timestampIndex > row.Timestamps.Count) return false;
        if (timestampIndex == row.Timestamps.Count && string.IsNullOrWhiteSpace(value)) return false;
        Change(document => UpdateRow(document, rowId, existing =>
        {
            var timestamps = existing.Timestamps.ToList();
            if (string.IsNullOrWhiteSpace(value)) timestamps.RemoveAt(timestampIndex);
            else if (timestampIndex == timestamps.Count) timestamps.Add(new(Guid.NewGuid(), value.Trim()));
            else timestamps[timestampIndex] = timestamps[timestampIndex] with { Value = value.Trim() };
            return existing with { Timestamps = timestamps };
        }));
        return true;
    }

    public bool SetTimestamps(Guid rowId, IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var row = FindRow(Document, rowId);
        if (row is null) return false;
        var timestamps = new List<EditorTimestamp>();
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index]?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value)) continue;
            var id = index < row.Timestamps.Count ? row.Timestamps[index].Id : Guid.NewGuid();
            timestamps.Add(new(id, value));
        }
        if (row.Timestamps.SequenceEqual(timestamps)) return false;
        Change(document => UpdateRow(document, rowId, existing => existing with { Timestamps = timestamps }));
        return true;
    }

    public void SetTitleOverride(string? title) => SetMetadata(UserTrackMetadata.Normalise(title, Metadata.Artist));
    public void SetArtistOverride(string? artist) => SetMetadata(UserTrackMetadata.Normalise(Metadata.Title, artist));
    public void ClearTitleOverride() => SetTitleOverride(null);
    public void ClearArtistOverride() => SetArtistOverride(null);

    public Guid? InsertAbove(Guid rowId) => InsertRelative(rowId, before: true);
    public Guid? InsertBelow(Guid rowId) => InsertRelative(rowId, before: false);

    public Guid Append()
    {
        var entry = NewLine(Document.DefaultLineEnding);
        Change(document =>
        {
            var lines = document.PhysicalLines.ToList();
            var rowIndex = lines.FindLastIndex(line => line.Kind == EditorEntryKind.Lyric);
            var insertAt = rowIndex >= 0 ? rowIndex + 1 : LeadingMetadataCount(lines);
            EnsureSeparatorBefore(lines, insertAt, document.DefaultLineEnding);
            lines.Insert(insertAt, entry);
            return document with { PhysicalLines = lines };
        });
        return entry.LyricRow!.Id;
    }

    public bool Delete(Guid rowId)
    {
        var index = FindLineIndex(Document, rowId);
        if (index < 0) return false;
        Change(document =>
        {
            var lines = document.PhysicalLines.ToList();
            lines.RemoveAt(index);
            return document with { PhysicalLines = lines };
        });
        return true;
    }

    public bool SetTimestampFromPlayback(EditorSelection selection, bool trackMatches, long playbackPositionMs, long globalOffsetMs)
    {
        if (!trackMatches || selection.SelectedRowId is not { } rowId || playbackPositionMs < 0) return false;
        var row = FindRow(Document, rowId);
        if (row is null) return false;
        var index = 0;
        if (row.Timestamps.Count > 0)
        {
            if (selection.SelectedTimestampIndex is not { } selected || selected < 0 || selected >= row.Timestamps.Count)
                return false;
            index = selected;
        }
        long rawTime;
        try { rawTime = checked(playbackPositionMs - globalOffsetMs); }
        catch (OverflowException) { return false; }
        if (rawTime < 0) return false;
        SetTimestamp(rowId, index, EditorDocumentCodec.FormatTimestamp(rawTime));
        return true;
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Push(Capture());
        Restore(_undo.Pop());
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Push(Capture());
        Restore(_redo.Pop());
    }

    public void MarkSaved(string savedContent, string? savedHash, UserTrackMetadata? savedMetadata = null)
    {
        var metadata = UserTrackMetadata.Normalise((savedMetadata ?? Metadata).Title, (savedMetadata ?? Metadata).Artist);
        Document = Document with { OriginalContent = savedContent, SourceHash = savedHash };
        Metadata = metadata;
        _baselineContent = savedContent;
        _baselineMetadata = metadata;
        _undo.Clear();
        _redo.Clear();
    }

    private Guid? InsertRelative(Guid rowId, bool before)
    {
        var index = FindLineIndex(Document, rowId);
        if (index < 0) return null;
        var inserted = NewLine(Document.PhysicalLines[index].LineEnding.Length == 0
            ? Document.DefaultLineEnding : Document.PhysicalLines[index].LineEnding);
        Change(document =>
        {
            var lines = document.PhysicalLines.ToList();
            var insertAt = before ? index : index + 1;
            EnsureSeparatorBefore(lines, insertAt, document.DefaultLineEnding);
            lines.Insert(insertAt, inserted);
            return document with { PhysicalLines = lines };
        });
        return inserted.LyricRow!.Id;
    }

    private void SetMetadata(UserTrackMetadata metadata)
    {
        metadata = UserTrackMetadata.Normalise(metadata.Title, metadata.Artist);
        if (metadata == Metadata) return;
        _undo.Push(Capture());
        _redo.Clear();
        Metadata = metadata;
    }

    private void Change(Func<EditorDocument, EditorDocument> update)
    {
        var next = update(Document);
        if (next == Document) return;
        _undo.Push(Capture());
        _redo.Clear();
        Document = next;
    }

    private State Capture() => new(Document, Metadata);
    private void Restore(State state) { Document = state.Document; Metadata = state.Metadata; }

    private static EditorPhysicalLine NewLine(string ending)
    {
        var row = new EditorLyricRow(Guid.NewGuid(), string.Empty, []);
        return new(Guid.NewGuid(), EditorEntryKind.Lyric, string.Empty, ending, row, IsModified: true);
    }

    private static EditorLyricRow? FindRow(EditorDocument document, Guid rowId) =>
        document.PhysicalLines.FirstOrDefault(line => line.LyricRow?.Id == rowId)?.LyricRow;

    private static int FindLineIndex(EditorDocument document, Guid rowId)
    {
        for (var index = 0; index < document.PhysicalLines.Count; index++)
            if (document.PhysicalLines[index].LyricRow?.Id == rowId) return index;
        return -1;
    }

    private static EditorDocument UpdateRow(EditorDocument document, Guid rowId, Func<EditorLyricRow, EditorLyricRow> update)
    {
        var lines = document.PhysicalLines.ToList();
        var index = lines.FindIndex(line => line.LyricRow?.Id == rowId);
        if (index < 0) return document;
        var entry = lines[index];
        var updated = update(entry.LyricRow!);
        if (updated == entry.LyricRow) return document;
        lines[index] = entry with
        {
            LyricRow = updated,
            IsModified = !string.Equals(EditorDocumentCodec.SerializeLyricRow(updated), entry.RawText, StringComparison.Ordinal)
        };
        return document with { PhysicalLines = lines };
    }

    private static int LeadingMetadataCount(IReadOnlyList<EditorPhysicalLine> lines)
    {
        var count = 0;
        while (count < lines.Count && lines[count].Kind is EditorEntryKind.Metadata or EditorEntryKind.Blank) count++;
        return count;
    }

    private static void EnsureSeparatorBefore(List<EditorPhysicalLine> lines, int insertAt, string defaultEnding)
    {
        if (insertAt <= 0 || lines[insertAt - 1].LineEnding.Length > 0) return;
        lines[insertAt - 1] = lines[insertAt - 1] with { LineEnding = defaultEnding };
    }
}
