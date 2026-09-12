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

    public static LrcTimestampRewriteResult Rewrite(string content, long deltaMs)
    {
        ArgumentNullException.ThrowIfNull(content);
        var replacements = new List<(int Index, int Length, string Value)>();
        foreach (Match match in TimestampRegex().Matches(content))
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
            if (adjusted < 0) return new(false, Error: NegativeTimestampError);
            replacements.Add((match.Index, match.Length, Format(adjusted)));
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
