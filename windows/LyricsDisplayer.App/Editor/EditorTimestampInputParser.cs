using System.Text.RegularExpressions;
using LyricsDisplayer.Core.Library;

namespace LyricsDisplayer;

/// <summary>Normalizes timestamp text from editor cells without changing the authoritative LRC grammar.</summary>
public static partial class EditorTimestampInputParser
{
    [GeneratedRegex(@"^(?<minutes>[0-9]+):(?<seconds>[0-9]{1,2})(?:\.(?<fraction>[0-9]{1,3}))?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex FlexibleTimestampPattern();

    /// <summary>
    /// Normalizes a committed cell value to the editor's canonical timestamp format.
    /// Empty input stays empty and invalid input is returned unchanged for advisory validation.
    /// </summary>
    public static string NormalizeCommittedValue(string? input)
    {
        var original = input ?? string.Empty;
        var trimmed = original.Trim();
        if (trimmed.Length == 0) return string.Empty;
        if (trimmed == "0") return EditorDocumentCodec.FormatTimestamp(0);

        var match = FlexibleTimestampPattern().Match(trimmed);
        if (!match.Success) return original;

        var minutes = match.Groups["minutes"].Value;
        var seconds = match.Groups["seconds"].Value.PadLeft(2, '0');
        var fraction = match.Groups["fraction"].Success
            ? match.Groups["fraction"].Value.PadRight(3, '0')
            : "000";
        var candidate = $"{minutes}:{seconds}.{fraction}";
        if (!EditorDocumentCodec.TryParseTimestamp(candidate, out var milliseconds)) return original;

        return EditorDocumentCodec.FormatTimestamp(milliseconds);
    }
}
