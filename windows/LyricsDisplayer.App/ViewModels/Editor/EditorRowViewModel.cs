using System.Collections.ObjectModel;
using LyricsDisplayer.Core.Library;

namespace LyricsDisplayer;

/// <summary>Observable presentation state for one editable lyric row.</summary>
public sealed class EditorRowViewModel : ObservableObjectBase
{
    private string _lyricsText;
    private int _visibleLineNumber;
    private IReadOnlyList<EditorValidationDiagnostic> _diagnostics = [];

    public Guid EditorLineId { get; }
    public ObservableCollection<string> Timestamps { get; }
    public int VisibleLineNumber => _visibleLineNumber;
    public ObservableCollection<EditorValidationSeverity?> TimestampDiagnosticSeverities { get; }
    public IReadOnlyList<EditorValidationDiagnostic> Diagnostics => _diagnostics;
    public EditorValidationSeverity? LyricsDiagnosticSeverity =>
        EditorValidationPresentation.GetCellSeverity(_diagnostics, EditorColumn.Lyrics);

    public string LyricsText
    {
        get => _lyricsText;
        set => SetProperty(ref _lyricsText, value);
    }

    public EditorRowViewModel(EditorLyricRow row, int timestampColumnCount, int visibleLineNumber)
    {
        EditorLineId = row.Id;
        _visibleLineNumber = visibleLineNumber;
        _lyricsText = row.LyricsText;
        Timestamps = new ObservableCollection<string>(Enumerable.Range(0, timestampColumnCount)
            .Select(index => index < row.Timestamps.Count ? row.Timestamps[index].Value : string.Empty));
        TimestampDiagnosticSeverities = new ObservableCollection<EditorValidationSeverity?>(
            Enumerable.Repeat<EditorValidationSeverity?>(null, timestampColumnCount));
    }

    internal void UpdateDiagnosticPresentation(int visibleLineNumber,
        IReadOnlyList<EditorValidationDiagnostic> diagnostics)
    {
        var lineNumberChanged = _visibleLineNumber != visibleLineNumber;
        _visibleLineNumber = visibleLineNumber;
        _diagnostics = diagnostics;
        if (lineNumberChanged) OnPropertyChanged(nameof(VisibleLineNumber));
        OnPropertyChanged(nameof(Diagnostics));
        OnPropertyChanged(nameof(LyricsDiagnosticSeverity));
        for (var index = 0; index < TimestampDiagnosticSeverities.Count; index++)
        {
            var severity = EditorValidationPresentation.GetCellSeverity(_diagnostics,
                EditorColumn.Timestamp, index);
            if (TimestampDiagnosticSeverities[index] != severity)
                TimestampDiagnosticSeverities[index] = severity;
        }
    }
}
