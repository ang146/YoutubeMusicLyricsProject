using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LyricsDisplayer.Core.Library;

namespace LyricsDisplayer;

public sealed class EditorRowViewModel : INotifyPropertyChanged
{
    private string _lyricsText;
    public Guid EditorLineId { get; }
    public ObservableCollection<string> Timestamps { get; }
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

    public EditorRowViewModel(EditorLyricRow row, int timestampColumnCount)
    {
        EditorLineId = row.Id;
        _lyricsText = row.LyricsText;
        Timestamps = new ObservableCollection<string>(Enumerable.Range(0, timestampColumnCount)
            .Select(index => index < row.Timestamps.Count ? row.Timestamps[index].Value : string.Empty));
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
    public IReadOnlyList<EditorValidationDiagnostic> Diagnostics => _buffer.Document.Validate();

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
        InsertRowAboveCommand = new("editor.row.insert-above", _ => InsertRelative(true), _ => FindSelectedRow() is not null);
        InsertRowBelowCommand = new("editor.row.insert-below", _ => InsertRelative(false), _ => FindSelectedRow() is not null);
        AppendRowCommand = new("editor.row.append", _ => { CommitStagedEdits(); CommitMetadata(); SelectRow(_buffer.Append()); }, _ => true);
        DeleteRowCommand = new("editor.row.delete", _ => DeleteSelected(), _ => FindSelectedRow() is not null);
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
        _selection = new(rowId, column, timestampIndex);
        InvalidateCommands();
        OnPropertyChanged(nameof(Selection));
    }

    public void CommitRowEdit(EditorRowViewModel row)
    {
        var current = _buffer.Document.Rows.FirstOrDefault(item => item.Id == row.EditorLineId);
        if (current is null) return;
        if (!string.Equals(current.LyricsText, row.LyricsText, StringComparison.Ordinal))
            _buffer.SetLyrics(row.EditorLineId, row.LyricsText);
        _buffer.SetTimestamps(row.EditorLineId, row.Timestamps.ToArray());
        RefreshFromBuffer(keepSelection: true);
        if (_buffer.Document.Validate().Count > 0) Status = "Validation warnings are advisory; Save remains available.";
    }

    public void CommitStagedEdits()
    {
        foreach (var row in Rows.ToArray()) CommitRowEdit(row);
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
        CommitStagedEdits();
        CommitMetadata();
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
            ExternalConflict = null;
            ExternalFileMissing = false;
            Status = "Saved. Runtime lyrics update through the active LRC watcher.";
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
        CommitStagedEdits();
        CommitMetadata();
        var rowId = FindSelectedRow()!.EditorLineId;
        var inserted = above ? _buffer.InsertAbove(rowId) : _buffer.InsertBelow(rowId);
        if (inserted is { } id) SelectRow(id);
        else RefreshFromBuffer();
    }

    private void DeleteSelected()
    {
        CommitStagedEdits();
        CommitMetadata();
        var row = FindSelectedRow();
        if (row is null) return;
        var index = Rows.IndexOf(row);
        var target = index + 1 < Rows.Count ? Rows[index + 1].EditorLineId
            : index > 0 ? Rows[index - 1].EditorLineId : (Guid?)null;
        _buffer.Delete(row.EditorLineId);
        RefreshFromBuffer(keepSelection: false);
        SelectCell(target, EditorColumn.Lyrics);
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
        _buffer.SetTitleOverride(TitleOverride);
        _buffer.SetArtistOverride(ArtistOverride);
        RefreshFromBuffer(keepSelection: true);
    }

    private EditorRowViewModel? FindSelectedRow() => _selection.SelectedRowId is { } id
        ? Rows.FirstOrDefault(row => row.EditorLineId == id) : null;

    private void SelectRow(Guid id) => SelectCell(id, _selection.SelectedColumn, _selection.SelectedTimestampIndex);

    private void RefreshFromBuffer(bool keepSelection = false)
    {
        var selectedId = keepSelection ? _selection.SelectedRowId : null;
        var columns = TimestampColumnCount;
        Rows.Clear();
        foreach (var row in _buffer.Document.Rows)
        {
            var viewRow = new EditorRowViewModel(row, columns);
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
        TitleOverride = _buffer.Metadata.Title ?? string.Empty;
        ArtistOverride = _buffer.Metadata.Artist ?? string.Empty;
        if (selectedId is { } id && Rows.Any(row => row.EditorLineId == id)) _selection = _selection with { SelectedRowId = id };
        else if (!keepSelection || _selection.SelectedRowId is not null) _selection = new(null, EditorColumn.Lyrics);
        OnPropertyChanged(nameof(PreservedEntries));
        OnPropertyChanged(nameof(TimestampColumnCount));
        OnPropertyChanged(nameof(Diagnostics));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(EffectiveTitle));
        OnPropertyChanged(nameof(EffectiveArtist));
        OnPropertyChanged(nameof(Selection));
        InvalidateCommands();
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
