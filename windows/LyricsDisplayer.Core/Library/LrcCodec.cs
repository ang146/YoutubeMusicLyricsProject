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
}

public sealed record LrcTimestampOccurrence(int CharacterIndex, int Length, long StartMs);

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

    public static LrcParseResult Parse(string text, long? durationMs = null)
    {
        var parsed = new List<(long StartMs, string Text, int Order, LrcTimestampOccurrence Occurrence)>();
        var skipped = 0;
        var order = 0;
        foreach (Match physicalMatch in Regex.Matches(text, @"[^\r\n]+"))
        {
            var physicalLine = physicalMatch.Value;
            if (physicalLine.Length == 0 || MetadataRegex().IsMatch(physicalLine)) continue;
            var matches = TimestampRegex().Matches(physicalLine);
            if (matches.Count == 0)
            {
                skipped++;
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
                    // Isolate an invalid timestamp without discarding usable physical lines.
                }
            }
            if (!any) skipped++;
        }

        if (parsed.Count == 0)
            return new LrcParseResult(false, [], skipped, "The LRC contains no usable timestamped lines.");

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
            TimestampOccurrences = ordered.Select(item => item.Occurrence).ToArray()
        };
    }
}
