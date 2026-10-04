using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using LyricsDisplayer.Core.Library;

namespace LyricsDisplayer;

public sealed class EditorRowViewModel : INotifyPropertyChanged
{
    private string _lyricsText;
    private int _physicalLineNumber;
    private IReadOnlyList<EditorValidationDiagnostic> _diagnostics = [];
    public Guid EditorLineId { get; }
    public ObservableCollection<string> Timestamps { get; }
    public int PhysicalLineNumber => _physicalLineNumber;
    public string LineDisplay => RowDiagnosticSeverity switch
    {
        EditorValidationSeverity.Warning => $"{_physicalLineNumber.ToString(CultureInfo.InvariantCulture)} W",
        EditorValidationSeverity.Error => $"{_physicalLineNumber.ToString(CultureInfo.InvariantCulture)} E",
        _ => _physicalLineNumber.ToString(CultureInfo.InvariantCulture)
    };
    public IReadOnlyList<EditorValidationDiagnostic> Diagnostics => _diagnostics;
    public EditorValidationSeverity? RowDiagnosticSeverity =>
        _diagnostics.Any(item => item.Severity == EditorValidationSeverity.Error)
            ? EditorValidationSeverity.Error
            : _diagnostics.Any() ? EditorValidationSeverity.Warning : null;
    public string? DiagnosticDetails => _diagnostics.Count == 0 ? null : string.Join(Environment.NewLine,
        _diagnostics.Select(item => $"{item.Severity} - Line {item.PhysicalLine}\n{item.Message}"));
    public string LyricsText
    {
        get => _lyricsText;
        set
        {
            if (_lyricsText == value) return;
            _lyricsText = value;
            PropertyChanged?.Invoke(this, new(nameof(LyricsText)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;

    public EditorRowViewModel(EditorLyricRow row, int timestampColumnCount, int physicalLineNumber)
    {
        EditorLineId = row.Id;
        _physicalLineNumber = physicalLineNumber;
        _lyricsText = row.LyricsText;
        Timestamps = new ObservableCollection<string>(Enumerable.Range(0, timestampColumnCount)
            .Select(index => index < row.Timestamps.Count ? row.Timestamps[index].Value : string.Empty));
    }

    internal void UpdateDiagnosticPresentation(int physicalLineNumber,
        IReadOnlyList<EditorValidationDiagnostic> diagnostics)
    {
        var lineNumberChanged = _physicalLineNumber != physicalLineNumber;
        _physicalLineNumber = physicalLineNumber;
        _diagnostics = diagnostics;
        if (lineNumberChanged) PropertyChanged?.Invoke(this, new(nameof(PhysicalLineNumber)));
        PropertyChanged?.Invoke(this, new(nameof(LineDisplay)));
        PropertyChanged?.Invoke(this, new(nameof(Diagnostics)));
        PropertyChanged?.Invoke(this, new(nameof(RowDiagnosticSeverity)));
        PropertyChanged?.Invoke(this, new(nameof(DiagnosticDetails)));
    }
}

public sealed class BuiltInLyricsEditorViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly LyricsLibrary _library;
    private readonly PlaybackStateCoordinator _playback;
    private EditorAssetSnapshot _asset;
    private EditorDocumentBuffer _buffer;
    private EditorSelection _selection = new(null, EditorColumn.Lyrics);
    private string _titleOverride;
    private string _artistOverride;
    private string _status = string.Empty;
    private string? _externalConflict;
    private bool _externalMissing;
    private long _playbackPosition;
    private bool _playbackMatches;
    private bool _disposed;
    private IReadOnlyList<EditorValidationDiagnostic> _diagnostics = [];

    public ObservableCollection<EditorRowViewModel> Rows { get; } = [];
    public IReadOnlyList<EditorPhysicalLine> PreservedEntries => _buffer.Document.PhysicalLines
        .Where(line => line.Kind != EditorEntryKind.Lyric).ToArray();
    public LocalTrackRecord EditorTrack => _asset.Record;
    public string SourceTitle => EditorTrack.SourceAssociations.FirstOrDefault()?.Metadata.Title ?? string.Empty;
    public string SourceArtist => EditorTrack.SourceAssociations.FirstOrDefault()?.Metadata.Artist ?? string.Empty;
    public string EffectiveTitle => _buffer.Metadata.Title ?? SourceTitle;
    public string EffectiveArtist => _buffer.Metadata.Artist ?? SourceArtist;
    public string TitleOverride { get => _titleOverride; set => Set(ref _titleOverride, value); }
    public string ArtistOverride { get => _artistOverride; set => Set(ref _artistOverride, value); }
    public bool IsDirty => _buffer.IsDirty ||
        UserTrackMetadata.Normalise(_titleOverride, _artistOverride) != _buffer.Metadata || HasStagedRowEdits();
    public bool CanUndo => _buffer.CanUndo;
    public bool CanRedo => _buffer.CanRedo;
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string? ExternalConflict { get => _externalConflict; private set => Set(ref _externalConflict, value); }
    public bool ExternalFileMissing { get => _externalMissing; private set => Set(ref _externalMissing, value); }
    public string PlaybackPositionText => _playback.HasClockState
        ? $"Playback: {EditorDocumentCodec.FormatTimestamp(_playbackPosition)}"
        : "Playback: unavailable";
    public bool PlaybackMatchesEditorTrack => _playbackMatches;
    public string PlaybackIdentityMessage => _playbackMatches
        ? "Playback matches this editor track."
        : "Set Current Time is disabled because another track is playing.";
    public int TimestampColumnCount => Math.Max(1,
        _buffer.Document.Rows.Select(row => row.Timestamps.Count).DefaultIfEmpty(0).Max() + 1);
    public EditorSelection Selection => _selection;
    public IReadOnlyList<EditorValidationDiagnostic> Diagnostics => _diagnostics;
    public int WarningCount => _diagnostics.Count(item => item.Severity == EditorValidationSeverity.Warning);
    public int ErrorCount => _diagnostics.Count(item => item.Severity == EditorValidationSeverity.Error);
    public bool HasValidationDiagnostics => _diagnostics.Count > 0;

    public EditorCommand SaveCommand { get; }
    public EditorCommand UndoCommand { get; }
    public EditorCommand RedoCommand { get; }
    public EditorCommand InsertRowAboveCommand { get; }
    public EditorCommand InsertRowBelowCommand { get; }
    public EditorCommand AppendRowCommand { get; }
    public EditorCommand DeleteRowCommand { get; }
    public EditorCommand SetTimestampFromPlaybackCommand { get; }
    public EditorCommand CommitMetadataCommand { get; }
    public EditorCommand ClearTitleOverrideCommand { get; }
    public EditorCommand ClearArtistOverrideCommand { get; }
    public EditorCommand ReloadExternalCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? ConflictRequiresChoice;
    public event Action? SaveCompleted;

    public BuiltInLyricsEditorViewModel(EditorAssetSnapshot asset, LyricsLibrary library,
        PlaybackStateCoordinator playback)
    {
        _asset = asset ?? throw new ArgumentNullException(nameof(asset));
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _playback = playback ?? throw new ArgumentNullException(nameof(playback));
        _buffer = new(EditorDocumentCodec.Load(asset.LrcContent, asset.LrcHash), asset.Sidecar.UserMetadata);
        _titleOverride = asset.Sidecar.UserMetadata.Title ?? string.Empty;
        _artistOverride = asset.Sidecar.UserMetadata.Artist ?? string.Empty;

        SaveCommand = new("editor.save", parameter => Save(parameter is EditorSaveAction action ? action : EditorSaveAction.Save),
            _ => IsDirty, EditorHotkeyScope.Editor);
        UndoCommand = new("editor.undo", _ =>
        {
            CommitStagedEdits();
            CommitMetadata();
            _buffer.Undo();
            RefreshFromBuffer();
        }, _ => CanUndo || IsDirty);
        RedoCommand = new("editor.redo", _ => { _buffer.Redo(); RefreshFromBuffer(); }, _ => CanRedo);
        InsertRowAboveCommand = new("editor.row.insert-above", _ => InsertRelative(true), _ => SelectedRowIdInDocument is not null);
        InsertRowBelowCommand = new("editor.row.insert-below", _ => InsertRelative(false), _ => SelectedRowIdInDocument is not null);
        AppendRowCommand = new("editor.row.append", _ =>
        {
            CommitStagedEdits();
            CommitMetadata();
            var inserted = _buffer.Append();
            RefreshFromBuffer(keepSelection: true);
            SelectRow(inserted);
        }, _ => true);
        DeleteRowCommand = new("editor.row.delete", _ => DeleteSelected(), _ => SelectedRowIdInDocument is not null);
        SetTimestampFromPlaybackCommand = new("editor.timestamp.from-playback", _ => SetTimestampFromPlayback(), CanSetTimestampFromPlayback);
        CommitMetadataCommand = new("editor.metadata.commit", _ => CommitMetadata());
        ClearTitleOverrideCommand = new("editor.metadata.clear-title", _ =>
        {
            _buffer.ClearTitleOverride();
            TitleOverride = string.Empty;
            RefreshFromBuffer();
        });
        ClearArtistOverrideCommand = new("editor.metadata.clear-artist", _ =>
        {
            _buffer.ClearArtistOverride();
            ArtistOverride = string.Empty;
            RefreshFromBuffer();
        });
        ReloadExternalCommand = new("editor.external.reload", _ => ReloadExternalVersion());
        RefreshFromBuffer();
        RefreshPlayback();
    }

    public void SelectCell(Guid? rowId, EditorColumn column, int? timestampIndex = null)
    {
        _selection = new(rowId is { } id && FindDocumentRow(id) is not null ? id : null, column, timestampIndex);
        InvalidateCommands();
        OnPropertyChanged(nameof(Selection));
    }

    public void CommitRowEdit(EditorRowViewModel row)
    {
        if (_buffer.Document.Rows.All(item => item.Id != row.EditorLineId)) return;
        // Capture header edits before committing a row that may expand the timestamp
        // projection and rebuild Rows (which refreshes the header bindings too).
        CommitMetadata();
        var previousColumnCount = TimestampColumnCount;
        if (!CommitRowToBuffer(row)) return;

        if (TimestampColumnCount != previousColumnCount)
            RefreshFromBuffer(keepSelection: true);
        else if (Rows.FirstOrDefault(current => current.EditorLineId == row.EditorLineId) is { } currentRow)
            SynchronizeRowFromBuffer(currentRow);

        NotifyAfterRowCommit();
    }

    public void CommitStagedEdits()
    {
        // Metadata text boxes are edited directly while row values are staged in the grid.
        // Commit metadata first because a row commit can rebuild the observable projection.
        CommitMetadata();
        var previousColumnCount = TimestampColumnCount;
        var stagedRows = Rows.ToArray();
        foreach (var row in stagedRows) CommitRowToBuffer(row);

        if (TimestampColumnCount != previousColumnCount)
            RefreshFromBuffer(keepSelection: true);
        else
            foreach (var row in Rows) SynchronizeRowFromBuffer(row);

        NotifyAfterRowCommit();
    }

    private bool CommitRowToBuffer(EditorRowViewModel row)
    {
        if (_buffer.Document.Rows.All(item => item.Id != row.EditorLineId)) return false;
        var current = _buffer.Document.Rows.First(item => item.Id == row.EditorLineId);
        if (!string.Equals(current.LyricsText, row.LyricsText, StringComparison.Ordinal))
            _buffer.SetLyrics(row.EditorLineId, row.LyricsText);
        _buffer.SetTimestamps(row.EditorLineId, row.Timestamps.ToArray());
        return true;
    }

    private void SynchronizeRowFromBuffer(EditorRowViewModel row)
    {
        var committed = _buffer.Document.Rows.FirstOrDefault(item => item.Id == row.EditorLineId);
        if (committed is null) return;
        row.LyricsText = committed.LyricsText;
        for (var index = 0; index < row.Timestamps.Count; index++)
        {
            var value = index < committed.Timestamps.Count ? committed.Timestamps[index].Value : string.Empty;
            if (!string.Equals(row.Timestamps[index], value, StringComparison.Ordinal))
                row.Timestamps[index] = value;
        }
    }

    private void NotifyAfterRowCommit()
    {
        RefreshValidationProjection();
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        InvalidateCommands();
    }

    public void RefreshPlayback()
    {
        _playbackPosition = _playback.HasClockState ? _playback.GetLocalPositionMs() : 0;
        var current = _playback.Current;
        var sourceTrackId = current?.Payload.Track.SourceTrackId;
        var source = current?.Envelope.Source;
        _playbackMatches = _playback.HasClockState && current is not null &&
            _playback.ActiveLocalLyricsRecord?.LocalTrackId == EditorTrack.LocalTrackId &&
            EditorTrack.SourceAssociations.Any(item => item.Source == source && item.SourceTrackId == sourceTrackId);
        OnPropertyChanged(nameof(PlaybackPositionText));
        OnPropertyChanged(nameof(PlaybackMatchesEditorTrack));
        OnPropertyChanged(nameof(PlaybackIdentityMessage));
        InvalidateCommands();
    }

    public void NotifyExternalChange()
    {
        var result = _library.LoadForEditing(EditorTrack);
        if (result.Status == EditorAssetStatus.Missing)
        {
            ExternalFileMissing = true;
            ExternalConflict = "The authoritative LRC is missing.";
            if (!IsDirty) Status = "LRC file missing; the editor buffer is preserved.";
            return;
        }
        if (!result.Succeeded || result.Asset is null)
        {
            Status = result.Error ?? "The current LRC cannot be checked.";
            return;
        }
        if (string.Equals(result.Asset.LrcHash, _asset.LrcHash, StringComparison.Ordinal) &&
            string.Equals(result.Asset.SidecarHash, _asset.SidecarHash, StringComparison.Ordinal)) return;

        ExternalFileMissing = false;
        ExternalConflict = "The authoritative LRC or sidecar changed outside the editor.";
        if (!IsDirty) Reload(result.Asset);
    }

    public void SetStatus(string status) => Status = status;

    private void Save(EditorSaveAction action)
    {
        if (action == EditorSaveAction.ReloadExternalVersion)
        {
            ReloadExternalVersion();
            return;
        }
        CommitMetadata();
        CommitStagedEdits();
        var content = EditorDocumentCodec.Serialize(_buffer.Document);
        var result = _library.SaveEditorAssets(_asset, content, _buffer.Metadata,
            overwriteExternalChanges: action == EditorSaveAction.OverwriteExternalChanges);
        if (result.Status == EditorAssetStatus.Conflict)
        {
            ExternalConflict = result.Error ?? "The authoritative files changed externally.";
            ExternalFileMissing = result.Error?.Contains("deleted", StringComparison.OrdinalIgnoreCase) == true;
            ConflictRequiresChoice?.Invoke();
            return;
        }
        if (result.Status == EditorAssetStatus.Saved && result.Asset is { } saved)
        {
            _asset = saved;
            _buffer.MarkSaved(content, saved.LrcHash, saved.Sidecar.UserMetadata);
            TitleOverride = saved.Sidecar.UserMetadata.Title ?? string.Empty;
            ArtistOverride = saved.Sidecar.UserMetadata.Artist ?? string.Empty;
            _playback.ApplySavedEditorMetadata(saved.Record, saved.Sidecar);
            ExternalConflict = null;
            ExternalFileMissing = false;
            Status = "Saved. Metadata overrides are active now; runtime lyrics update through the active LRC watcher.";
            RefreshFromBuffer(keepSelection: true);
            SaveCompleted?.Invoke();
            return;
        }
        if (result.Asset is { } partial) _asset = partial;
        Status = result.Error ?? "Save failed; the editor remains open and dirty.";
    }

    private void ReloadExternalVersion()
    {
        var result = _library.LoadForEditing(EditorTrack);
        if (result.Status != EditorAssetStatus.Ready || result.Asset is null)
        {
            ExternalFileMissing = result.Status == EditorAssetStatus.Missing;
            Status = result.Error ?? "The external LRC is unavailable; the editor buffer was not discarded.";
            return;
        }
        Reload(result.Asset);
    }

    private void Reload(EditorAssetSnapshot asset)
    {
        var oldSelection = _selection;
        _asset = asset;
        _buffer = new(EditorDocumentCodec.Load(asset.LrcContent, asset.LrcHash), asset.Sidecar.UserMetadata);
        TitleOverride = asset.Sidecar.UserMetadata.Title ?? string.Empty;
        ArtistOverride = asset.Sidecar.UserMetadata.Artist ?? string.Empty;
        ExternalConflict = null;
        ExternalFileMissing = false;
        Status = "Reloaded the external version.";
        RefreshFromBuffer(keepSelection: false);
        if (oldSelection.SelectedRowId is { } selected && Rows.Any(row => row.EditorLineId == selected))
            SelectCell(selected, oldSelection.SelectedColumn, oldSelection.SelectedTimestampIndex);
    }

    private void InsertRelative(bool above)
    {
        if (SelectedRowIdInDocument is not { } rowId) return;
        CommitStagedEdits();
        CommitMetadata();
        if (SelectedRowIdInDocument is not { } currentRowId) return;
        rowId = currentRowId;
        var inserted = above ? _buffer.InsertAbove(rowId) : _buffer.InsertBelow(rowId);
        if (inserted is { } id)
        {
            RefreshFromBuffer(keepSelection: true);
            SelectRow(id);
        }
        else RefreshFromBuffer();
    }

    private void DeleteSelected()
    {
        if (SelectedRowIdInDocument is not { } rowId) return;
        CommitStagedEdits();
        CommitMetadata();
        if (SelectedRowIdInDocument is not { } currentRowId) return;
        rowId = currentRowId;
        if (!_buffer.Delete(rowId)) return;
        RefreshFromBuffer(keepSelection: false);
    }

    private void SetTimestampFromPlayback()
    {
        CommitStagedEdits();
        CommitMetadata();
        RefreshPlayback();
        if (!_buffer.SetTimestampFromPlayback(_selection, _playbackMatches, _playbackPosition, _playback.GlobalOffsetMs))
        {
            Status = "Select a row and, when it has timestamps, select one occurrence; playback must match the editor track.";
            return;
        }
        Status = string.Empty;
        RefreshFromBuffer(keepSelection: true);
    }

    private bool CanSetTimestampFromPlayback(object? _)
    {
        if (!_playbackMatches || _selection.SelectedRowId is not { } rowId) return false;
        var row = _buffer.Document.Rows.FirstOrDefault(item => item.Id == rowId);
        if (row is null) return false;
        return row.Timestamps.Count == 0 ||
            (_selection.SelectedColumn == EditorColumn.Timestamp &&
             _selection.SelectedTimestampIndex is { } index && index >= 0 && index < row.Timestamps.Count);
    }

    private void CommitMetadata()
    {
        var previous = _buffer.Metadata;
        _buffer.SetTitleOverride(TitleOverride);
        _buffer.SetArtistOverride(ArtistOverride);
        if (previous == _buffer.Metadata) return;
        TitleOverride = _buffer.Metadata.Title ?? string.Empty;
        ArtistOverride = _buffer.Metadata.Artist ?? string.Empty;
        OnPropertyChanged(nameof(EffectiveTitle));
        OnPropertyChanged(nameof(EffectiveArtist));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        InvalidateCommands();
    }

    private Guid? SelectedRowIdInDocument => _selection.SelectedRowId is { } id && FindDocumentRow(id) is not null
        ? id : null;

    private EditorLyricRow? FindDocumentRow(Guid rowId) => _buffer.Document.Rows.FirstOrDefault(row => row.Id == rowId);

    private void SelectRow(Guid id) => SelectCell(id, _selection.SelectedColumn, _selection.SelectedTimestampIndex);

    private void RefreshFromBuffer(bool keepSelection = false)
    {
        var selectedId = keepSelection ? _selection.SelectedRowId : null;
        var columns = TimestampColumnCount;
        Rows.Clear();
        for (var index = 0; index < _buffer.Document.PhysicalLines.Count; index++)
        {
            if (_buffer.Document.PhysicalLines[index].LyricRow is not { } row) continue;
            var viewRow = new EditorRowViewModel(row, columns, index + 1);
            viewRow.PropertyChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(IsDirty));
                SaveCommand.Invalidate();
            };
            viewRow.Timestamps.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(IsDirty));
                SaveCommand.Invalidate();
            };
            Rows.Add(viewRow);
        }
        RefreshValidationProjection();
        TitleOverride = _buffer.Metadata.Title ?? string.Empty;
        ArtistOverride = _buffer.Metadata.Artist ?? string.Empty;
        if (selectedId is { } id && Rows.Any(row => row.EditorLineId == id)) _selection = _selection with { SelectedRowId = id };
        else if (!keepSelection || _selection.SelectedRowId is not null) _selection = new(null, EditorColumn.Lyrics);
        OnPropertyChanged(nameof(PreservedEntries));
        OnPropertyChanged(nameof(TimestampColumnCount));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(EffectiveTitle));
        OnPropertyChanged(nameof(EffectiveArtist));
        OnPropertyChanged(nameof(Selection));
        InvalidateCommands();
    }

    private void RefreshValidationProjection()
    {
        _diagnostics = _buffer.Document.Validate().ToArray();
        OnPropertyChanged(nameof(Diagnostics));
        OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(HasValidationDiagnostics));

        var lineNumbers = new Dictionary<Guid, int>();
        for (var index = 0; index < _buffer.Document.PhysicalLines.Count; index++)
        {
            if (_buffer.Document.PhysicalLines[index].LyricRow is { } row)
                lineNumbers[row.Id] = index + 1;
        }

        foreach (var row in Rows)
        {
            var rowDiagnostics = _diagnostics.Where(item => item.RowId == row.EditorLineId).ToArray();
            if (lineNumbers.TryGetValue(row.EditorLineId, out var physicalLineNumber))
                row.UpdateDiagnosticPresentation(physicalLineNumber, rowDiagnostics);
        }
    }

    private void InvalidateCommands()
    {
        SaveCommand.Invalidate(); UndoCommand.Invalidate(); RedoCommand.Invalidate();
        InsertRowAboveCommand.Invalidate(); InsertRowBelowCommand.Invalidate(); AppendRowCommand.Invalidate();
        DeleteRowCommand.Invalidate(); SetTimestampFromPlaybackCommand.Invalidate();
    }

    private bool HasStagedRowEdits()
    {
        foreach (var viewRow in Rows)
        {
            var row = _buffer.Document.Rows.FirstOrDefault(item => item.Id == viewRow.EditorLineId);
            if (row is null || !string.Equals(row.LyricsText, viewRow.LyricsText, StringComparison.Ordinal)) return true;
            for (var index = 0; index < viewRow.Timestamps.Count; index++)
            {
                var value = index < row.Timestamps.Count ? row.Timestamps[index].Value : string.Empty;
                if (!string.Equals(value, viewRow.Timestamps[index], StringComparison.Ordinal)) return true;
            }
        }
        return false;
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        if (name is nameof(TitleOverride) or nameof(ArtistOverride)) OnPropertyChanged(nameof(IsDirty));
        SaveCommand?.Invalidate();
        return true;
    }

    private void OnPropertyChanged(string? name) => PropertyChanged?.Invoke(this, new(name));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
