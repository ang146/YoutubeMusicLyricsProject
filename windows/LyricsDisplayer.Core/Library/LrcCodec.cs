using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LyricsDisplayer.Core.Protocol;

namespace LyricsDisplayer.Core.Library;

public sealed record LrcParseResult(
    bool Success,
    IReadOnlyList<LyricsLine> Lines,
    int SkippedPhysicalLines,
    string? Error = null)
{
    public IReadOnlyList<LrcTimestampOccurrence> TimestampOccurrences { get; init; } = [];
    public IReadOnlyList<LrcParseDiagnostic> Diagnostics { get; init; } = [];
    public IReadOnlyList<string> UntimedLines { get; init; } = [];
    public bool HasTimedLyrics => Lines.Count > 0;
}

public sealed record LrcTimestampOccurrence(int CharacterIndex, int Length, long StartMs);
public sealed record LrcParseDiagnostic(int PhysicalLineNumber, string Message);

public static partial class LrcCodec
{
    [GeneratedRegex(@"\[(?<minutes>\d+):(?<seconds>\d{2})\.(?<fraction>\d{2,3})\]")]
    private static partial Regex TimestampRegex();

    [GeneratedRegex(@"^\[(ar|ti|al|by|offset|re|ve|length):", RegexOptions.IgnoreCase)]
    private static partial Regex MetadataRegex();

    public static string Serialize(IEnumerable<LyricsLine> lines)
    {
        var builder = new StringBuilder();
        foreach (var line in lines.OrderBy(line => line.StartMs))
        {
            if (line.StartMs < 0) throw new ArgumentOutOfRangeException(nameof(lines), "LRC timestamps cannot be negative.");
            var minutes = line.StartMs / 60_000;
            var seconds = line.StartMs % 60_000 / 1_000;
            var milliseconds = line.StartMs % 1_000;
            var text = line.Text.Replace("\r", " ").Replace("\n", " ");
            builder.Append('[').Append(minutes.ToString("00", CultureInfo.InvariantCulture)).Append(':')
                .Append(seconds.ToString("00", CultureInfo.InvariantCulture)).Append('.')
                .Append(milliseconds.ToString("000", CultureInfo.InvariantCulture)).Append(']')
                .Append(text).Append('\n');
        }
        return builder.ToString();
    }

    public static string SerializeUntimed(IEnumerable<string> lines)
    {
        var builder = new StringBuilder();
        foreach (var line in lines)
            builder.Append(line).Append('\n');
        return builder.ToString();
    }

    public static LrcParseResult Parse(string text, long? durationMs = null)
    {
        var parsed = new List<(long StartMs, string Text, int Order, LrcTimestampOccurrence Occurrence)>();
        var diagnostics = new List<LrcParseDiagnostic>();
        var untimedLines = new List<string>();
        var skipped = 0;
        var order = 0;
        var lineNumber = 1;
        var scannedThrough = 0;
        foreach (Match physicalMatch in Regex.Matches(text, @"[^\r\n]+"))
        {
            while (scannedThrough < physicalMatch.Index)
            {
                if (text[scannedThrough++] == '\r')
                {
                    if (scannedThrough < physicalMatch.Index && text[scannedThrough] == '\n') scannedThrough++;
                    lineNumber++;
                }
                else if (text[scannedThrough - 1] == '\n') lineNumber++;
            }
            var physicalLine = physicalMatch.Value;
            var syntaxError = FindMalformedTimestampSyntax(physicalLine);
            if (syntaxError is not null)
            {
                diagnostics.Add(new(lineNumber, syntaxError));
                skipped++;
                scannedThrough = physicalMatch.Index + physicalMatch.Length;
                continue;
            }
            if (physicalLine.Length == 0 || MetadataRegex().IsMatch(physicalLine)) continue;
            var matches = TimestampRegex().Matches(physicalLine);
            if (matches.Count == 0)
            {
                skipped++;
                untimedLines.Add(physicalLine);
                scannedThrough = physicalMatch.Index + physicalMatch.Length;
                continue;
            }
            var last = matches[^1];
            var lyricText = physicalLine[(last.Index + last.Length)..];
            var any = false;
            foreach (Match match in matches)
            {
                if (!long.TryParse(match.Groups["minutes"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) ||
                    !int.TryParse(match.Groups["seconds"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) ||
                    seconds >= 60)
                {
                    diagnostics.Add(new(lineNumber, "Timestamp fields are outside the supported LRC format."));
                    any = true;
                    continue;
                }
                var fractionText = match.Groups["fraction"].Value;
                var fraction = int.Parse(fractionText, CultureInfo.InvariantCulture) * (fractionText.Length == 2 ? 10 : 1);
                try
                {
                    var start = checked(minutes * 60_000 + seconds * 1_000 + fraction);
                    parsed.Add((start, lyricText, order++,
                        new(physicalMatch.Index + match.Index, match.Length, start)));
                    any = true;
                }
                catch (OverflowException)
                {
                    diagnostics.Add(new(lineNumber, "Timestamp value is too large."));
                    any = true;
                }
            }
            if (!any) skipped++;
            scannedThrough = physicalMatch.Index + physicalMatch.Length;
        }

        if (diagnostics.Count > 0)
        {
            var first = diagnostics[0];
            return new LrcParseResult(false, [], skipped,
                $"Line {first.PhysicalLineNumber}: {first.Message}") { Diagnostics = diagnostics };
        }

        if (parsed.Count == 0)
            return new LrcParseResult(true, [], skipped) { UntimedLines = untimedLines };

        var ordered = parsed.OrderBy(item => item.StartMs).ThenBy(item => item.Order).ToArray();
        var result = new List<LyricsLine>(ordered.Length);
        for (var index = 0; index < ordered.Length; index++)
        {
            var start = ordered[index].StartMs;
            var end = index + 1 < ordered.Length
                ? ordered[index + 1].StartMs
                : durationMs is > 0 && durationMs > start ? durationMs.Value : start;
            result.Add(new LyricsLine(start, Math.Max(start, end), ordered[index].Text));
        }
        return new LrcParseResult(true, result, skipped)
        {
            TimestampOccurrences = ordered.Select(item => item.Occurrence).ToArray(),
            UntimedLines = untimedLines
        };
    }

    private static string? FindMalformedTimestampSyntax(string line)
    {
        var matches = TimestampRegex().Matches(line);
        var matchStarts = matches.Select(match => match.Index).ToHashSet();
        for (var index = 0; index < line.Length; index++)
        {
            if (line[index] != '[') continue;
            var end = line.IndexOf(']', index + 1);
            var tokenEnd = end < 0 ? line.Length : end;
            var token = line[(index + 1)..tokenEnd];
            if (LooksLikeTimestampToken(token) && !matchStarts.Contains(index))
                return end < 0 ? "timestamp tag is missing its closing bracket." : "timestamp tag is malformed.";
        }

        var cursor = 0;
        while (cursor < line.Length && char.IsWhiteSpace(line[cursor])) cursor++;
        var foundTimestamp = false;
        while (cursor < line.Length && line[cursor] == '[')
        {
            var end = line.IndexOf(']', cursor + 1);
            if (end < 0)
            {
                if (foundTimestamp) return "timestamp sequence contains an unclosed bracketed token.";
                break;
            }

            var tokenLength = end - cursor + 1;
            var match = TimestampRegex().Match(line, cursor);
            if (match.Success && match.Index == cursor && match.Length == tokenLength)
            {
                foundTimestamp = true;
                cursor = end + 1;
                continue;
            }

            if (foundTimestamp) return "timestamp sequence contains a malformed timestamp token.";
            break;
        }

        return null;
    }

    private static bool LooksLikeTimestampToken(string token)
    {
        if (token.Length == 0 || !char.IsDigit(token[0])) return false;
        if (token.Contains(':')) return true;

        // A missing-colon timestamp still has a minute/second-sized numeric field
        // followed by the supported fractional separator (for example 0055.000).
        var fractionSeparator = token.IndexOf('.');
        return fractionSeparator >= 4 && token[..fractionSeparator].All(char.IsDigit);
    }
}
