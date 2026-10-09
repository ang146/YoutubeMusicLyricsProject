using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Resources;
using System.Globalization;

namespace LyricsDisplayer;

/// <summary>Maps advisory diagnostics to visible editor rows and their owning cells.</summary>
public static class EditorValidationPresentation
{
    public static EditorValidationSeverity? GetCellSeverity(
        IReadOnlyList<EditorValidationDiagnostic> diagnostics,
        EditorColumn column,
        int? timestampIndex = null)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        var hasError = diagnostics.Any(diagnostic => IsForCell(diagnostic, column, timestampIndex) &&
            diagnostic.Severity == EditorValidationSeverity.Error);
        if (hasError) return EditorValidationSeverity.Error;

        return diagnostics.Any(diagnostic => IsForCell(diagnostic, column, timestampIndex) &&
            diagnostic.Severity == EditorValidationSeverity.Warning)
            ? EditorValidationSeverity.Warning
            : null;
    }

    public static string? BuildSummaryTooltip(
        IReadOnlyList<EditorValidationDiagnostic> diagnostics,
        IReadOnlyDictionary<Guid, int> visibleLineNumbers)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(visibleLineNumbers);
        if (diagnostics.Count == 0) return null;

        return string.Join(Environment.NewLine, diagnostics
            .Select((diagnostic, index) => new
            {
                Diagnostic = diagnostic,
                Index = index,
                VisibleLine = diagnostic.RowId is { } rowId && visibleLineNumbers.TryGetValue(rowId, out var line)
                    ? (int?)line
                    : null
            })
            .OrderBy(item => item.VisibleLine ?? int.MaxValue)
            .ThenBy(item => item.Diagnostic.Severity == EditorValidationSeverity.Error ? 0 : 1)
            .ThenBy(item => item.Index)
            .Select(item =>
            {
                var location = item.VisibleLine is { } line
                    ? string.Format(CultureInfo.CurrentCulture, Strings.EditorLineLocation, line)
                    : Strings.EditorDocumentLocation;
                var severity = item.Diagnostic.Severity == EditorValidationSeverity.Error
                    ? Strings.EditorValidationError
                    : Strings.EditorValidationWarning;
                return string.Format(CultureInfo.CurrentCulture, Strings.EditorValidationDiagnosticLine,
                    severity, location, item.Diagnostic.Message);
            }));
    }

    private static bool IsForCell(EditorValidationDiagnostic diagnostic, EditorColumn column,
        int? timestampIndex) => diagnostic.TargetColumn == column &&
        (column != EditorColumn.Timestamp || diagnostic.TimestampIndex == timestampIndex);
}
