using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Infrastructure.Commands;

namespace LyricsDisplayer;

public interface IBuiltInLyricsEditorViewModel : INotifyPropertyChanged, IDisposable
{
    ObservableCollection<EditorRowViewModel> Rows { get; }
    IReadOnlyList<EditorPhysicalLine> PreservedEntries { get; }
    LocalTrackRecord EditorTrack { get; }
    string SourceTitle { get; }
    string SourceArtist { get; }
    string EffectiveTitle { get; }
    string EffectiveArtist { get; }
    string TitleOverride { get; set; }
    string ArtistOverride { get; set; }
    bool IsDirty { get; }
    bool CanUndo { get; }
    bool CanRedo { get; }
    string Status { get; }
    string? ExternalConflict { get; }
    bool ExternalFileMissing { get; }
    string PlaybackLiveText { get; }
    int TimestampColumnCount { get; }
    EditorSelection Selection { get; }
    IReadOnlyList<EditorValidationDiagnostic> Diagnostics { get; }
    string? ValidationSummaryToolTip { get; }
    int WarningCount { get; }
    int ErrorCount { get; }
    bool HasValidationDiagnostics { get; }
    IRelayCommand SaveCommand { get; }
    IRelayCommand UndoCommand { get; }
    IRelayCommand RedoCommand { get; }
    IRelayCommand InsertRowAboveCommand { get; }
    IRelayCommand InsertRowBelowCommand { get; }
    IRelayCommand AppendRowCommand { get; }
    IRelayCommand DeleteRowCommand { get; }
    IRelayCommand ClearCellCommand { get; }
    IRelayCommand SetTimestampFromPlaybackCommand { get; }
    IRelayCommand ShiftAllTimestampsCommand { get; }
    IRelayCommand ShiftSelectedLineTimingCommand { get; }
    IRelayCommand CommitMetadataCommand { get; }
    IRelayCommand ClearTitleOverrideCommand { get; }
    IRelayCommand ClearArtistOverrideCommand { get; }
    IRelayCommand ReloadExternalCommand { get; }
    event Action? ConflictRequiresChoice;
    event Action? SaveCompleted;
    void SelectCell(Guid? rowId, EditorColumn column, int? timestampIndex = null);
    void CommitRowEdit(EditorRowViewModel row);
    void CommitStagedEdits();
    void RefreshPlayback();
    void NotifyExternalChange();
    void SetStatus(string status);
}
