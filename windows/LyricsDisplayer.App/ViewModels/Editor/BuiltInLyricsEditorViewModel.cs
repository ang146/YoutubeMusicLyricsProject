using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Core.Protocol;
using LyricsDisplayer.Core.Timeline;
using LyricsDisplayer.Infrastructure.Commands;
using LyricsDisplayer.Resources;
using Microsoft.Extensions.Logging;

namespace LyricsDisplayer;

public sealed class BuiltInLyricsEditorViewModel : ViewModelBase, IBuiltInLyricsEditorViewModel
{
    private readonly LyricsLibrary _library;
    private readonly PlaybackStateCoordinator _playback;
    private readonly ILyricsTimingAdjustmentService _timingAdjustments;
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
    public string PlaybackLiveText
    {
        get
        {
            var position = _playback.HasClockState
                ? EditorDocumentCodec.FormatTimestamp(Math.Max(0, _playbackPosition))
                : "--:--.---";
            var lyric = _playbackMatches ? GetCurrentEditorLyric() : string.Empty;
            return $"{position} | {lyric}";
        }
    }
    public int TimestampColumnCount => Math.Max(1,
        _buffer.Document.Rows.Select(row => row.Timestamps.Count).DefaultIfEmpty(0).Max() + 1);
    public EditorSelection Selection => _selection;
    public IReadOnlyList<EditorValidationDiagnostic> Diagnostics => _diagnostics;
    public string? ValidationSummaryToolTip { get; private set; }
    public int WarningCount => _diagnostics.Count(item => item.Severity == EditorValidationSeverity.Warning);
    public int ErrorCount => _diagnostics.Count(item => item.Severity == EditorValidationSeverity.Error);
    public bool HasValidationDiagnostics => _diagnostics.Count > 0;

    public IRelayCommand SaveCommand { get; }
    public IRelayCommand UndoCommand { get; }
    public IRelayCommand RedoCommand { get; }
    public IRelayCommand InsertRowAboveCommand { get; }
    public IRelayCommand InsertRowBelowCommand { get; }
    public IRelayCommand AppendRowCommand { get; }
    public IRelayCommand DeleteRowCommand { get; }
    public IRelayCommand ClearCellCommand { get; }
    public IRelayCommand SetTimestampFromPlaybackCommand { get; }
    public IRelayCommand ShiftAllTimestampsCommand { get; }
    public IRelayCommand ShiftSelectedLineTimingCommand { get; }
    public IRelayCommand CommitMetadataCommand { get; }
    public IRelayCommand ClearTitleOverrideCommand { get; }
    public IRelayCommand ClearArtistOverrideCommand { get; }
    public IRelayCommand ReloadExternalCommand { get; }
    public event Action? ConflictRequiresChoice;
    public event Action? SaveCompleted;

    public BuiltInLyricsEditorViewModel(EditorAssetSnapshot asset, LyricsLibrary library,
        PlaybackStateCoordinator playback, ILyricsTimingAdjustmentService timingAdjustments,
        ICommandFactory commandFactory,
        ILogger<BuiltInLyricsEditorViewModel> logger) : base(logger)
    {
        _asset = asset ?? throw new ArgumentNullException(nameof(asset));
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _playback = playback ?? throw new ArgumentNullException(nameof(playback));
        _timingAdjustments = timingAdjustments ?? throw new ArgumentNullException(nameof(timingAdjustments));
        ArgumentNullException.ThrowIfNull(commandFactory);
        _buffer = new(EditorDocumentCodec.Load(asset.LrcContent, asset.LrcHash), asset.Sidecar.UserMetadata);
        _titleOverride = asset.Sidecar.UserMetadata.Title ?? string.Empty;
        _artistOverride = asset.Sidecar.UserMetadata.Artist ?? string.Empty;

        SaveCommand = commandFactory.Create(ApplicationCommandIds.Editor.Save, parameter => Save(parameter is EditorSaveAction action ? action : EditorSaveAction.Save),
            _ => IsDirty, CommandScope.Editor);
        UndoCommand = commandFactory.Create(ApplicationCommandIds.Editor.Undo, _ =>
        {
            CommitStagedEdits();
            CommitMetadata();
            _buffer.Undo();
            RefreshFromBuffer(keepSelection: true);
        }, _ => CanUndo || IsDirty);
        RedoCommand = commandFactory.Create(ApplicationCommandIds.Editor.Redo, _ => { _buffer.Redo(); RefreshFromBuffer(keepSelection: true); }, _ => CanRedo);
        InsertRowAboveCommand = commandFactory.Create(ApplicationCommandIds.Editor.InsertRowAbove, _ => InsertRelative(true), _ => SelectedRowIdInDocument is not null);
        InsertRowBelowCommand = commandFactory.Create(ApplicationCommandIds.Editor.InsertRowBelow, _ => InsertRelative(false), _ => SelectedRowIdInDocument is not null);
        AppendRowCommand = commandFactory.Create(ApplicationCommandIds.Editor.AppendRow, _ =>
        {
            CommitStagedEdits();
            CommitMetadata();
            var inserted = _buffer.Append();
            RefreshFromBuffer(keepSelection: true);
            SelectRow(inserted);
        }, _ => true);
        DeleteRowCommand = commandFactory.Create(ApplicationCommandIds.Editor.DeleteRow, _ => DeleteSelected(), _ => SelectedRowIdInDocument is not null);
        ClearCellCommand = commandFactory.Create(ApplicationCommandIds.Editor.ClearCell, ClearCell,
            parameter => CanClearCell(parameter as EditorSelection ?? Selection));
        SetTimestampFromPlaybackCommand = commandFactory.Create(ApplicationCommandIds.Editor.SetTimestampFromPlayback, _ => SetTimestampFromPlayback(), CanSetTimestampFromPlayback);
        ShiftAllTimestampsCommand = commandFactory.Create(ApplicationCommandIds.Editor.ShiftAllTimestamps, ShiftAllTimestamps, CanShiftAllTimestamps);
        ShiftSelectedLineTimingCommand = commandFactory.Create(ApplicationCommandIds.Editor.ShiftSelectedLineTiming, ShiftSelectedLineTiming,
            CanShiftSelectedLineTiming);
        CommitMetadataCommand = commandFactory.Create(ApplicationCommandIds.Editor.CommitMetadata, _ => CommitMetadata());
        ClearTitleOverrideCommand = commandFactory.Create(ApplicationCommandIds.Editor.ClearTitleOverride, _ =>
        {
            _buffer.ClearTitleOverride();
            TitleOverride = string.Empty;
            RefreshFromBuffer();
        });
        ClearArtistOverrideCommand = commandFactory.Create(ApplicationCommandIds.Editor.ClearArtistOverride, _ =>
        {
            _buffer.ClearArtistOverride();
            ArtistOverride = string.Empty;
            RefreshFromBuffer();
        });
        ReloadExternalCommand = commandFactory.Create(ApplicationCommandIds.Editor.ReloadExternal, _ => ReloadExternalVersion());
        RefreshFromBuffer();
        RefreshPlayback();
        Logger.LogDebug("Built-in Lyrics Editor ViewModel initialized for {LocalTrackId}.", EditorTrack.LocalTrackId);
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

        var timestampValues = row.Timestamps.ToArray();
        for (var index = 0; index < timestampValues.Length; index++)
        {
            var committedValue = index < current.Timestamps.Count ? current.Timestamps[index].Value : string.Empty;
            var stagedValue = timestampValues[index] ?? string.Empty;
            if (string.Equals(committedValue, stagedValue, StringComparison.Ordinal)) continue;

            var normalizedValue = EditorTimestampInputParser.NormalizeCommittedValue(stagedValue);
            timestampValues[index] = normalizedValue;
            if (!string.Equals(stagedValue, normalizedValue, StringComparison.Ordinal))
                row.Timestamps[index] = normalizedValue;
        }

        _buffer.SetTimestamps(row.EditorLineId, timestampValues);
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
        OnPropertyChanged(nameof(PlaybackLiveText));
        InvalidateCommands();
    }

    public void NotifyExternalChange()
    {
        var result = _library.LoadForEditing(EditorTrack);
        if (result.Status == EditorAssetStatus.Missing)
        {
            ExternalFileMissing = true;
            ExternalConflict = Strings.EditorAuthoritativeLrcMissing;
            if (!IsDirty) Status = Strings.EditorLrcMissingBufferPreserved;
            return;
        }
        if (!result.Succeeded || result.Asset is null)
        {
            Status = WithDetails(Strings.EditorCurrentLrcCannotBeChecked, result.Error);
            return;
        }
        if (string.Equals(result.Asset.LrcHash, _asset.LrcHash, StringComparison.Ordinal) &&
            string.Equals(result.Asset.SidecarHash, _asset.SidecarHash, StringComparison.Ordinal)) return;

        ExternalFileMissing = false;
        ExternalConflict = Strings.EditorAuthoritativeFilesChanged;
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
            ExternalConflict = WithDetails(Strings.EditorFilesChangedExternally, result.Error);
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
            _playback.ReloadExternalLocalLyrics(saved.Record.LocalTrackId, saved.LrcHash);
            ExternalConflict = null;
            ExternalFileMissing = false;
            Status = Strings.EditorSaved;
            RefreshFromBuffer(keepSelection: true);
            SaveCompleted?.Invoke();
            return;
        }
        if (result.Asset is { } partial) _asset = partial;
        Status = WithDetails(Strings.EditorSaveFailedDirty, result.Error);
    }

    private void ReloadExternalVersion()
    {
        var result = _library.LoadForEditing(EditorTrack);
        if (result.Status != EditorAssetStatus.Ready || result.Asset is null)
        {
            ExternalFileMissing = result.Status == EditorAssetStatus.Missing;
            Status = WithDetails(Strings.EditorExternalLrcUnavailable, result.Error);
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
        Status = Strings.EditorReloadedExternalVersion;
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

    private bool CanClearCell(EditorSelection selection)
    {
        if (selection.SelectedRowId is not { } rowId || FindDocumentRow(rowId) is null) return false;
        var row = Rows.FirstOrDefault(item => item.EditorLineId == rowId);
        if (row is null) return false;

        return selection.SelectedColumn switch
        {
            EditorColumn.Timestamp => selection.SelectedTimestampIndex is { } index &&
                index >= 0 && index < row.Timestamps.Count && !string.IsNullOrEmpty(row.Timestamps[index]),
            EditorColumn.Lyrics => !string.IsNullOrEmpty(row.LyricsText),
            _ => false
        };
    }

    private void ClearCell(object? parameter)
    {
        var selection = parameter as EditorSelection ?? Selection;
        if (!CanClearCell(selection)) return;

        CommitStagedEdits();
        if (!CanClearCell(selection) || selection.SelectedRowId is not { } rowId) return;

        var changed = selection.SelectedColumn switch
        {
            EditorColumn.Timestamp when selection.SelectedTimestampIndex is { } index =>
                _buffer.SetTimestamp(rowId, index, string.Empty),
            EditorColumn.Lyrics => ClearLyrics(rowId),
            _ => false
        };
        if (changed) RefreshFromBuffer(keepSelection: true);
    }

    private bool ClearLyrics(Guid rowId)
    {
        var row = FindDocumentRow(rowId);
        if (row is null || row.LyricsText.Length == 0) return false;
        _buffer.SetLyrics(rowId, string.Empty);
        return true;
    }

    private void SetTimestampFromPlayback()
    {
        RefreshPlayback();
        if (!CanSetTimestampFromPlayback(null))
        {
            Status = Strings.EditorSetTimeUnavailable;
            return;
        }
        var nextAvailableIndex = FindNextAvailableTimestampIndex(
            Rows.First(row => row.EditorLineId == _selection.SelectedRowId));
        if (nextAvailableIndex is null) return;
        CommitStagedEdits();
        CommitMetadata();
        RefreshPlayback();
        if (!_buffer.SetTimestampFromPlayback(_selection, _playbackMatches, _playbackPosition,
                nextAvailableIndex))
        {
            Status = Strings.EditorPlaybackTimeCannotBeAdded;
            return;
        }
        Status = string.Empty;
        RefreshFromBuffer(keepSelection: true);
    }

    private void ShiftAllTimestamps(object? parameter)
    {
        if (!TryGetTimingDelta(parameter, out var deltaMs)) return;
        CommitStagedEdits();
        var result = _timingAdjustments.ShiftAll(_buffer.Document, deltaMs);
        if (!result.Succeeded || result.Document is null)
        {
            Status = FormatTimingFailure(result, Strings.EditorDocumentTimestampsCouldNotShift);
            return;
        }
        if (!_buffer.ApplyTimingAdjustment(result.Document))
        {
            Status = Strings.EditorDocumentTimestampsCouldNotShift;
            return;
        }

        Status = string.Empty;
        RefreshFromBuffer(keepSelection: true);
    }

    private bool CanShiftAllTimestamps(object? parameter) =>
        TryGetTimingDelta(parameter, out _) && Rows.Any(row =>
            row.Timestamps.Any(value => !string.IsNullOrWhiteSpace(value)));

    private void ShiftSelectedLineTiming(object? parameter)
    {
        if (!TryGetTimingDelta(parameter, out var deltaMs) || _selection.SelectedRowId is not { } rowId) return;

        var selectedRow = Rows.FirstOrDefault(candidate => candidate.EditorLineId == rowId);
        if (selectedRow is null || !selectedRow.Timestamps.Any(value => !string.IsNullOrWhiteSpace(value))) return;

        CommitStagedEdits();
        var result = _timingAdjustments.ShiftLine(_buffer.Document, rowId, deltaMs);
        if (!result.Succeeded || result.Document is null)
        {
            Status = FormatTimingFailure(result, Strings.EditorSelectedRowTimestampsCouldNotShift);
            return;
        }
        if (!_buffer.ApplyTimingAdjustment(result.Document))
        {
            Status = Strings.EditorSelectedRowTimestampsCouldNotShift;
            return;
        }

        Status = string.Empty;
        RefreshFromBuffer(keepSelection: true);
    }

    private bool CanShiftSelectedLineTiming(object? parameter)
    {
        if (!TryGetTimingDelta(parameter, out _) || _selection.SelectedRowId is not { } rowId)
            return false;

        var row = Rows.FirstOrDefault(candidate => candidate.EditorLineId == rowId);
        return row is not null && row.Timestamps.Any(value => !string.IsNullOrWhiteSpace(value));
    }

    private static bool TryGetTimingDelta(object? parameter, out long deltaMs)
    {
        switch (parameter)
        {
            case long value:
                deltaMs = value;
                return true;
            case int value:
                deltaMs = value;
                return true;
            case string value:
                return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out deltaMs);
            default:
                deltaMs = 0;
                return false;
        }
    }

    private static string WithDetails(string message, string? details) =>
        string.IsNullOrWhiteSpace(details)
            ? message
            : string.Format(CultureInfo.CurrentCulture, Strings.EditorStatusWithDetails, message, details);

    private static string FormatTimingFailure(EditorDocumentTimingAdjustmentResult result, string fallbackMessage) =>
        string.Equals(result.Error, LrcTimestampRewriter.NegativeTimestampError, StringComparison.Ordinal)
            ? Strings.EditorNegativeTimestampShift
            : WithDetails(fallbackMessage, result.Error);

    private string GetCurrentEditorLyric()
    {
        var evaluationPosition = _playbackPosition;
        long latestStartMs = long.MinValue;
        var currentText = string.Empty;
        foreach (var row in Rows)
        foreach (var value in row.Timestamps)
        {
            if (!EditorDocumentCodec.TryParseTimestamp(value, out var startMs) ||
                startMs > evaluationPosition || startMs < latestStartMs)
                continue;
            latestStartMs = startMs;
            currentText = row.LyricsText;
        }
        return currentText;
    }

    private void PublishEditorPreview()
    {
        var occurrences = new List<(long StartMs, int Order, string Text)>();
        var untimedLines = new List<string>();
        var order = 0;
        foreach (var row in Rows)
        {
            var hadTimedOccurrence = false;
            foreach (var value in row.Timestamps)
            {
                if (!EditorDocumentCodec.TryParseTimestamp(value, out var startMs)) continue;
                occurrences.Add((startMs, order++, row.LyricsText));
                hadTimedOccurrence = true;
            }
            if (!hadTimedOccurrence && row.LyricsText.Length > 0)
                untimedLines.Add(row.LyricsText);
        }

        var ordered = occurrences.OrderBy(item => item.StartMs).ThenBy(item => item.Order).ToArray();
        var durationMs = EditorTrack.SourceAssociations
            .Select(association => association.Metadata.DurationMs)
            .DefaultIfEmpty(0).Max();
        var lines = ordered.Select((item, index) =>
        {
            var endMs = index + 1 < ordered.Length
                ? ordered[index + 1].StartMs
                : durationMs > item.StartMs ? durationMs : item.StartMs;
            return new LyricsLine(item.StartMs, Math.Max(item.StartMs, endMs), item.Text);
        }).ToArray();
        _playback.SetEditorPreview(EditorTrack, lines, untimedLines);
        OnPropertyChanged(nameof(PlaybackLiveText));
    }

    private bool CanSetTimestampFromPlayback(object? _)
    {
        if (!_playbackMatches || _selection.SelectedRowId is not { } rowId) return false;
        var row = _buffer.Document.Rows.FirstOrDefault(item => item.Id == rowId);
        if (row is null) return false;
        var viewRow = Rows.FirstOrDefault(item => item.EditorLineId == rowId);
        if (viewRow is null || FindNextAvailableTimestampIndex(viewRow) is null) return false;

        return _playbackPosition >= 0;
    }

    private static int? FindNextAvailableTimestampIndex(EditorRowViewModel row)
    {
        var limit = EditorDocumentBuffer.MaximumPlaybackTimestampOccurrences;
        for (var index = 0; index < Math.Min(row.Timestamps.Count, limit); index++)
            if (string.IsNullOrWhiteSpace(row.Timestamps[index])) return index;

        var populatedCount = row.Timestamps.Count(value => !string.IsNullOrWhiteSpace(value));
        return populatedCount < limit ? populatedCount : null;
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
            var viewRow = new EditorRowViewModel(row, columns, Rows.Count + 1);
            viewRow.PropertyChanged += (_, args) =>
            {
                OnPropertyChanged(nameof(IsDirty));
                SaveCommand.RaiseCanExecuteChanged();
                ClearCellCommand.RaiseCanExecuteChanged();
                if (args.PropertyName == nameof(EditorRowViewModel.LyricsText))
                    PublishEditorPreview();
            };
            viewRow.Timestamps.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(IsDirty));
                SaveCommand.RaiseCanExecuteChanged();
                ClearCellCommand.RaiseCanExecuteChanged();
                SetTimestampFromPlaybackCommand.RaiseCanExecuteChanged();
                InvalidateDocumentTimingCommands();
                PublishEditorPreview();
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
        PublishEditorPreview();
    }

    private void RefreshValidationProjection()
    {
        _diagnostics = _buffer.Document.Validate().ToArray();
        OnPropertyChanged(nameof(Diagnostics));
        OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(HasValidationDiagnostics));

        var lineNumbers = _buffer.Document.Rows
            .Select((row, index) => (row.Id, VisibleLineNumber: index + 1))
            .ToDictionary(item => item.Id, item => item.VisibleLineNumber);
        ValidationSummaryToolTip = EditorValidationPresentation.BuildSummaryTooltip(_diagnostics, lineNumbers);
        OnPropertyChanged(nameof(ValidationSummaryToolTip));

        foreach (var row in Rows)
        {
            var rowDiagnostics = _diagnostics.Where(item => item.RowId == row.EditorLineId).ToArray();
            if (lineNumbers.TryGetValue(row.EditorLineId, out var visibleLineNumber))
                row.UpdateDiagnosticPresentation(visibleLineNumber, rowDiagnostics);
        }
    }

    private void InvalidateCommands()
    {
        SaveCommand.RaiseCanExecuteChanged(); UndoCommand.RaiseCanExecuteChanged(); RedoCommand.RaiseCanExecuteChanged();
        InsertRowAboveCommand.RaiseCanExecuteChanged(); InsertRowBelowCommand.RaiseCanExecuteChanged(); AppendRowCommand.RaiseCanExecuteChanged();
        DeleteRowCommand.RaiseCanExecuteChanged(); ClearCellCommand.RaiseCanExecuteChanged(); SetTimestampFromPlaybackCommand.RaiseCanExecuteChanged();
        ShiftAllTimestampsCommand.RaiseCanExecuteChanged(); ShiftSelectedLineTimingCommand.RaiseCanExecuteChanged();
    }

    private void InvalidateDocumentTimingCommands()
    {
        ShiftAllTimestampsCommand.RaiseCanExecuteChanged();
        ShiftSelectedLineTimingCommand.RaiseCanExecuteChanged();
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
        if (!SetProperty(ref field, value, name)) return false;
        if (name is nameof(TitleOverride) or nameof(ArtistOverride))
        {
            OnPropertyChanged(nameof(IsDirty));
            UndoCommand?.RaiseCanExecuteChanged();
        }
        SaveCommand?.RaiseCanExecuteChanged();
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _playback.ClearEditorPreview(EditorTrack.LocalTrackId);
    }
}
