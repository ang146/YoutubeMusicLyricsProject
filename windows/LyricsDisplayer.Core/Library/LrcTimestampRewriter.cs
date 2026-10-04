using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LyricsDisplayer.Core.Library;

public sealed record LrcTimestampRewriteResult(bool Success, string? Content = null, string? Error = null)
{
    public bool ContainsNegativeTimestamp => !Success && Error == LrcTimestampRewriter.NegativeTimestampError;
    public bool ContainsOverflow => !Success && Error == LrcTimestampRewriter.TimestampOverflowError;
}

public static partial class LrcTimestampRewriter
{
    public const string NegativeTimestampError = "The adjusted LRC would contain a negative timestamp.";
    public const string TimestampOverflowError = "An adjusted LRC timestamp cannot be represented safely.";

    [GeneratedRegex(@"\[(?<minutes>\d+):(?<seconds>\d{2})\.(?<fraction>\d{2,3})\]")]
    private static partial Regex TimestampRegex();

    public static LrcTimestampRewriteResult RewriteOccurrence(
        string content, LrcTimestampOccurrence occurrence, long adjustedTimestampMs)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(occurrence);
        if (adjustedTimestampMs < 0) return new(false, Error: NegativeTimestampError);
        if (occurrence.CharacterIndex < 0 || occurrence.Length <= 0 ||
            occurrence.CharacterIndex > content.Length - occurrence.Length)
            return new(false, Error: "The selected timestamp occurrence is no longer valid.");
        var token = content.Substring(occurrence.CharacterIndex, occurrence.Length);
        var match = TimestampRegex().Match(token);
        if (!match.Success || match.Index != 0 || match.Length != token.Length ||
            !TryParse(match, out var start, out _) || start != occurrence.StartMs)
            return new(false, Error: "The selected timestamp occurrence is no longer valid.");
        return new(true, content[..occurrence.CharacterIndex] + Format(adjustedTimestampMs) +
                         content[(occurrence.CharacterIndex + occurrence.Length)..]);
    }

    public static LrcTimestampRewriteResult Rewrite(string content, long deltaMs)
    {
        ArgumentNullException.ThrowIfNull(content);
        var replacements = new List<(int Index, int Length, string Value)>();
        for (var lineStart = 0; lineStart < content.Length;)
        {
            var lineEnd = content.IndexOfAny(['\r', '\n'], lineStart);
            if (lineEnd < 0) lineEnd = content.Length;
            var line = content[lineStart..lineEnd];
            var matches = TimestampRegex().Matches(line);
            if (matches.Count > 0)
            {
                var lastTimestamp = matches[^1];
                var lyricsText = line[(lastTimestamp.Index + lastTimestamp.Length)..];
                var isZeroTimeAnchor = LrcLineSemantics.IsZeroTimeIntroOrBreakAnchor(lyricsText);

                foreach (Match match in matches)
                {
                    if (!TryParse(match, out var timestampMs, out var overflow))
                    {
                        if (overflow) return new(false, Error: TimestampOverflowError);
                        continue;
                    }
                    long adjusted;
                    try
                    {
                        adjusted = checked(timestampMs + deltaMs);
                    }
                    catch (OverflowException)
                    {
                        return new(false, Error: TimestampOverflowError);
                    }
                    if (adjusted < 0)
                    {
                        if (deltaMs < 0 && timestampMs == 0 && isZeroTimeAnchor)
                            adjusted = 0;
                        else
                            return new(false, Error: NegativeTimestampError);
                    }
                    replacements.Add((lineStart + match.Index, match.Length, Format(adjusted)));
                }
            }

            if (lineEnd == content.Length) break;
            lineStart = lineEnd + 1;
            if (content[lineEnd] == '\r' && lineStart < content.Length && content[lineStart] == '\n')
                lineStart++;
        }

        if (replacements.Count == 0)
            return new(false, Error: "The LRC contains no usable timestamp tokens.");

        var builder = new StringBuilder(content.Length + replacements.Count);
        var cursor = 0;
        foreach (var replacement in replacements)
        {
            builder.Append(content, cursor, replacement.Index - cursor);
            builder.Append(replacement.Value);
            cursor = replacement.Index + replacement.Length;
        }
        builder.Append(content, cursor, content.Length - cursor);
        return new(true, builder.ToString());
    }

    private static bool TryParse(Match match, out long timestampMs, out bool overflow)
    {
        timestampMs = default;
        overflow = false;
        if (!long.TryParse(match.Groups["minutes"].Value, NumberStyles.None, CultureInfo.InvariantCulture,
                out var minutes))
        {
            overflow = true;
            return false;
        }
        if (!int.TryParse(match.Groups["seconds"].Value, NumberStyles.None, CultureInfo.InvariantCulture,
                out var seconds) || seconds >= 60)
            return false;
        var fractionText = match.Groups["fraction"].Value;
        var fraction = int.Parse(fractionText, CultureInfo.InvariantCulture) * (fractionText.Length == 2 ? 10 : 1);
        try
        {
            timestampMs = checked(minutes * 60_000 + seconds * 1_000 + fraction);
            return true;
        }
        catch (OverflowException)
        {
            overflow = true;
            return false;
        }
    }

    private static string Format(long timestampMs)
    {
        var minutes = timestampMs / 60_000;
        var seconds = timestampMs % 60_000 / 1_000;
        var milliseconds = timestampMs % 1_000;
        return $"[{minutes.ToString("00", CultureInfo.InvariantCulture)}:{seconds.ToString("00", CultureInfo.InvariantCulture)}.{milliseconds.ToString("000", CultureInfo.InvariantCulture)}]";
    }
}
