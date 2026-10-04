using System.ComponentModel;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using LyricsDisplayer.Core.Library;
using WpfApplication = System.Windows.Application;
using WpfBinding = System.Windows.Data.Binding;
using WpfControl = System.Windows.Controls.Control;
using WpfDataGridCell = System.Windows.Controls.DataGridCell;
using WpfMessageBox = System.Windows.MessageBox;

namespace LyricsDisplayer;

public partial class BuiltInLyricsEditorWindow : Window
{
    private readonly BuiltInLyricsEditorViewModel _viewModel;
    private readonly Dictionary<DataGridColumn, EditorGridColumn> _logicalGridColumns = [];
    private readonly FileSystemWatcher? _fileWatcher;
    private readonly FileSystemWatcher? _sidecarWatcher;
    private readonly DispatcherTimer _externalChangeTimer;
    private readonly DispatcherTimer _playbackRefreshTimer;
    private int _builtTimestampColumnCount = -1;
    private bool _committingGridEdits;
    private bool _gridRowsResetInProgress;
    private bool _closed;

    public BuiltInLyricsEditorWindow(BuiltInLyricsEditorViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        // Subscribe before the ItemsSource binding so this guard is armed before the
        // DataGrid reacts to the collection Reset emitted by RefreshFromBuffer.
        _viewModel.Rows.CollectionChanged += OnViewModelRowsCollectionChanged;
        InitializeComponent();
        DataContext = viewModel;
        BuildTimestampColumns(viewModel.TimestampColumnCount);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.ConflictRequiresChoice += OnConflictRequiresChoice;
        Closing += OnClosing;
        Closed += OnClosed;
        LyricsGrid.PreviewMouseRightButtonDown += OnGridPreviewMouseRightButtonDown;

        _externalChangeTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _externalChangeTimer.Tick += (_, _) =>
        {
            _externalChangeTimer.Stop();
            _viewModel.NotifyExternalChange();
        };
        _playbackRefreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _playbackRefreshTimer.Tick += (_, _) => _viewModel.RefreshPlayback();
        _playbackRefreshTimer.Start();

        var fullDirectory = Path.GetDirectoryName(((App)WpfApplication.Current).LyricsLibrary.ResolveLyricsPath(viewModel.EditorTrack));
        if (!string.IsNullOrWhiteSpace(fullDirectory) && Directory.Exists(fullDirectory))
        {
            try
            {
                _fileWatcher = new FileSystemWatcher(fullDirectory, Path.GetFileName(viewModel.EditorTrack.LyricsRelativePath))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };
                _fileWatcher.Changed += OnExternalFileChanged;
                _fileWatcher.Created += OnExternalFileChanged;
                _fileWatcher.Deleted += OnExternalFileChanged;
                _fileWatcher.Renamed += OnExternalFileRenamed;
                _fileWatcher.Error += OnExternalWatcherError;
                _sidecarWatcher = new FileSystemWatcher(fullDirectory, Path.GetFileName(viewModel.EditorTrack.SidecarRelativePath))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };
                _sidecarWatcher.Changed += OnExternalFileChanged;
                _sidecarWatcher.Created += OnExternalFileChanged;
                _sidecarWatcher.Deleted += OnExternalFileChanged;
                _sidecarWatcher.Renamed += OnExternalFileRenamed;
                _sidecarWatcher.Error += OnExternalWatcherError;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or
                                                NotSupportedException or System.Security.SecurityException)
            {
                _viewModel.SetStatus("External change notifications are unavailable; Save still checks file identity.");
            }
        }
    }

    public bool RequestCloseFromApplication()
    {
        if (_closed) return true;
        Close();
        return _closed;
    }

    private void BuildTimestampColumns(int count)
    {
        LyricsGrid.Columns.Clear();
        _logicalGridColumns.Clear();

        for (var index = 0; index < count; index++)
        {
            var column = new DataGridTextColumn
            {
                Header = $"Timestamp {index + 1}",
                Binding = new WpfBinding($"Timestamps[{index}]")
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                },
                Width = new DataGridLength(112),
                CellStyle = CreateDiagnosticCellStyle(
                    $"TimestampDiagnosticSeverities[{index}]")
            };
            LyricsGrid.Columns.Add(column);
            _logicalGridColumns.Add(column, new(EditorColumn.Timestamp, index));
        }
        var lyricsColumn = new DataGridTextColumn
        {
            Header = "Lyrics",
            Binding = new WpfBinding(nameof(EditorRowViewModel.LyricsText))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            CellStyle = CreateDiagnosticCellStyle(nameof(EditorRowViewModel.LyricsDiagnosticSeverity))
        };
        LyricsGrid.Columns.Add(lyricsColumn);
        _logicalGridColumns.Add(lyricsColumn, new(EditorColumn.Lyrics));
        _builtTimestampColumnCount = count;
    }

    private Style CreateDiagnosticCellStyle(string severityBindingPath)
    {
        var style = new Style(typeof(WpfDataGridCell), (Style)FindResource("EditorDiagnosticCellBaseStyle"));
        AddSeverityTrigger(style, severityBindingPath, EditorValidationSeverity.Warning,
            (System.Windows.Media.Brush)FindResource("EditorWarningCellBackground"),
            (System.Windows.Media.Brush)FindResource("EditorWarningCellForeground"));
        AddSeverityTrigger(style, severityBindingPath, EditorValidationSeverity.Error,
            (System.Windows.Media.Brush)FindResource("EditorErrorCellBackground"),
            (System.Windows.Media.Brush)FindResource("EditorErrorCellForeground"));
        return style;
    }

    private static void AddSeverityTrigger(Style style, string bindingPath,
        EditorValidationSeverity severity, System.Windows.Media.Brush background,
        System.Windows.Media.Brush foreground)
    {
        var trigger = new DataTrigger
        {
            Binding = new WpfBinding(bindingPath),
            Value = severity
        };
        trigger.Setters.Add(new Setter(WpfControl.BackgroundProperty, background));
        trigger.Setters.Add(new Setter(WpfControl.ForegroundProperty, foreground));
        style.Triggers.Add(trigger);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BuiltInLyricsEditorViewModel.TimestampColumnCount))
        {
            try
            {
                var selected = _viewModel.Selection;
                if (_builtTimestampColumnCount != _viewModel.TimestampColumnCount)
                    BuildTimestampColumns(_viewModel.TimestampColumnCount);
                if (selected.SelectedRowId is { } rowId)
                {
                    RestoreSelectedCell(rowId, selected);
                }
            }
            finally
            {
                _gridRowsResetInProgress = false;
            }
        }
    }

    private void OnViewModelRowsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
            _gridRowsResetInProgress = true;
    }

    private void OnGridLoadingRow(object? sender, DataGridRowEventArgs e) =>
        e.Row.SetBinding(DataGridRow.HeaderProperty,
            new WpfBinding(nameof(EditorRowViewModel.VisibleLineNumber)));

    private void OnGridPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualAncestor<DataGridRowHeader>(e.OriginalSource as DependencyObject) is not null)
            e.Handled = true;
    }

    private static T? FindVisualAncestor<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null)
        {
            if (element is T match) return match;
            element = element switch
            {
                System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D =>
                    System.Windows.Media.VisualTreeHelper.GetParent(element),
                FrameworkContentElement content => content.Parent ?? content.TemplatedParent as DependencyObject,
                _ => LogicalTreeHelper.GetParent(element)
            };
        }
        return null;
    }

    private void OnCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.Row.Item is not EditorRowViewModel row) return;
        if (_committingGridEdits)
        {
            return;
        }
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (!_viewModel.Rows.Any(current => ReferenceEquals(current, row)))
                return;
            _viewModel.CommitRowEdit(row);
        }));
    }

    private void OnBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Row.Item is EditorRowViewModel row) UpdateSelection(row, e.Column);
    }

    private void OnSelectedCellsChanged(object? sender, SelectedCellsChangedEventArgs e)
    {
        SyncSelection();
    }

    private void OnCurrentCellChanged(object? sender, EventArgs e)
    {
        SyncSelection();
    }

    private void OnGridPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var element = e.OriginalSource as DependencyObject;
        while (element is not null && element is not WpfDataGridCell) element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        if (element is WpfDataGridCell cell && cell.DataContext is EditorRowViewModel row)
        {
            LyricsGrid.CurrentCell = new DataGridCellInfo(row, cell.Column);
            LyricsGrid.SelectedCells.Clear();
            LyricsGrid.SelectedCells.Add(LyricsGrid.CurrentCell);
            UpdateSelection(row, cell.Column);
        }
    }

    private void SyncSelection()
    {
        if (_gridRowsResetInProgress)
            return;
        if (LyricsGrid.SelectedCells.Count == 1 && LyricsGrid.SelectedCells[0].Item is EditorRowViewModel row &&
            LyricsGrid.SelectedCells[0].Column is { } column)
        {
            UpdateSelection(row, column);
        }
        else if (_viewModel.Selection.SelectedRowId is not null)
        {
            _viewModel.SelectCell(null, EditorColumn.Lyrics);
        }
    }

    private void UpdateSelection(EditorRowViewModel row, DataGridColumn column)
    {
        if (_viewModel.Rows.All(current => current.EditorLineId != row.EditorLineId)) return;
        if (!_logicalGridColumns.TryGetValue(column, out var logicalColumn)) return;
        _viewModel.SelectCell(row.EditorLineId, logicalColumn.Column, logicalColumn.TimestampIndex);
    }

    private void OnGridPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            if (e.Key == Key.S)
            {
                CommitGridEdits();
                _viewModel.SaveCommand.Execute(EditorSaveAction.Save);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Shift) != 0 || e.Key == Key.Y)
            {
                CommitGridEdits();
                _viewModel.RedoCommand.Execute(null);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Z)
            {
                CommitGridEdits();
                _viewModel.UndoCommand.Execute(null);
                e.Handled = true;
                return;
            }
        }

        if (e.Key is not (Key.Enter or Key.Tab))
            return;
        // This editor owns grid navigation. Mark the routed input handled before any
        // edit commit or selection changes so the DataGrid bubble handler cannot move again.
        e.Handled = true;
        var key = e.Key;
        var backwards = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        // Capture stable row/column identity before committing; a column-count change may
        // rebuild the observable row collection before the navigation target is applied.
        if (!TryCaptureNavigationCell(out var current))
        {
            return;
        }

        CommitGridEdits();
        MoveGridCell(current, key == Key.Enter ? EditorGridNavigationKey.Enter : EditorGridNavigationKey.Tab,
            backwards);
    }

    private bool TryCaptureNavigationCell(out EditorSelection selection)
    {
        selection = new(null, EditorColumn.Lyrics);
        if (LyricsGrid.CurrentCell.Item is not EditorRowViewModel current ||
            !_viewModel.Rows.Any(row => row.EditorLineId == current.EditorLineId))
            return false;

        if (LyricsGrid.CurrentCell.Column?.IsReadOnly == true)
            return false;

        if (LyricsGrid.CurrentCell.Column is { } column &&
            _logicalGridColumns.TryGetValue(column, out var logicalColumn) &&
            column.Visibility == Visibility.Visible && !column.IsReadOnly)
        {
            selection = new(current.EditorLineId, logicalColumn.Column, logicalColumn.TimestampIndex);
            return true;
        }

        // A dynamic rebuild may replace the column while retaining the same logical selection.
        var selected = _viewModel.Selection;
        if (selected.SelectedRowId != current.EditorLineId ||
            !EditorGridNavigation.TryResolveColumn(GetVisibleEditableColumns(), selected, out _))
            return false;

        selection = selected;
        return true;
    }

    private void MoveGridCell(EditorSelection current, EditorGridNavigationKey key, bool backwards)
    {
        var rows = _viewModel.Rows.ToArray();
        var visibleColumns = GetVisibleEditableColumns();
        if (!EditorGridNavigation.TryMove(rows.Select(row => row.EditorLineId).ToArray(), visibleColumns,
                current, key, backwards, out var destination))
            return;

        FocusGridCell(destination);
    }

    private void FocusGridCell(EditorSelection destination)
    {
        var target = _viewModel.Rows.FirstOrDefault(row => row.EditorLineId == destination.SelectedRowId);
        var targetColumn = ResolveGridColumn(destination.SelectedColumn, destination.SelectedTimestampIndex);
        if (target is null || targetColumn is null || !LyricsGrid.Items.Contains(target))
            return;

        var cell = new DataGridCellInfo(target, targetColumn);
        LyricsGrid.CurrentCell = cell;
        LyricsGrid.SelectedCells.Clear();
        LyricsGrid.SelectedCells.Add(cell);
        _viewModel.SelectCell(destination.SelectedRowId, destination.SelectedColumn,
            destination.SelectedTimestampIndex);
        LyricsGrid.ScrollIntoView(target, targetColumn);
        LyricsGrid.UpdateLayout();
        if (_viewModel.Rows.Any(row => ReferenceEquals(row, target)) &&
            LyricsGrid.Items.Contains(target) && _logicalGridColumns.ContainsKey(targetColumn) &&
            ReferenceEquals(LyricsGrid.CurrentCell.Item, target) &&
            ReferenceEquals(LyricsGrid.CurrentCell.Column, targetColumn))
            LyricsGrid.BeginEdit();
    }

    private EditorGridColumn[] GetVisibleEditableColumns() => LyricsGrid.Columns
        .Where(column => column.Visibility == Visibility.Visible && !column.IsReadOnly &&
                         _logicalGridColumns.ContainsKey(column))
        .OrderBy(column => column.DisplayIndex)
        .Select(column => _logicalGridColumns[column])
        .ToArray();

    private DataGridColumn? ResolveGridColumn(EditorColumn column, int? timestampIndex)
    {
        var logicalColumns = GetVisibleEditableColumns();
        if (!EditorGridNavigation.TryResolveColumn(logicalColumns,
                new EditorSelection(null, column, timestampIndex), out var resolved))
            return null;

        return LyricsGrid.Columns.FirstOrDefault(gridColumn =>
            gridColumn.Visibility == Visibility.Visible && !gridColumn.IsReadOnly &&
            _logicalGridColumns.TryGetValue(gridColumn, out var logicalColumn) && logicalColumn == resolved);
    }

    private void RestoreSelectedCell(Guid rowId, EditorSelection selection)
    {
        if (_viewModel.Selection != selection)
            return;
        var row = _viewModel.Rows.FirstOrDefault(item => item.EditorLineId == rowId);
        if (row is null)
            return;
        var column = ResolveGridColumn(selection.SelectedColumn, selection.SelectedTimestampIndex);
        if (column is null || !LyricsGrid.Items.Contains(row))
            return;
        var cell = new DataGridCellInfo(row, column);
        LyricsGrid.CurrentCell = cell;
        LyricsGrid.SelectedCells.Clear();
        LyricsGrid.SelectedCells.Add(cell);
        var resolved = _logicalGridColumns[column];
        if (resolved.Column != selection.SelectedColumn || resolved.TimestampIndex != selection.SelectedTimestampIndex)
            _viewModel.SelectCell(rowId, resolved.Column, resolved.TimestampIndex);
    }

    private void CommitGridEdits()
    {
        var wasCommittingGridEdits = _committingGridEdits;
        _committingGridEdits = true;
        try
        {
            LyricsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            LyricsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        }
        finally
        {
            _committingGridEdits = wasCommittingGridEdits;
        }
        _viewModel.CommitMetadataCommand.Execute(null);
        _viewModel.CommitStagedEdits();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        var canClose = EditorCloseGuard.CanClose(
            ((App)WpfApplication.Current).Lifetime,
            CommitGridEdits,
            () => _viewModel.IsDirty,
            PromptForDirtyClose,
            () => _viewModel.SaveCommand.Execute(EditorSaveAction.Save));
        if (!canClose) e.Cancel = true;
    }

    private DirtyEditorCloseChoice PromptForDirtyClose()
    {
        var answer = WpfMessageBox.Show(this,
            "Save changes before closing?\n\nYes = Save; No = Discard; Cancel = Keep editing.",
            "Unsaved lyrics changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        return answer switch
        {
            MessageBoxResult.Yes => DirtyEditorCloseChoice.Save,
            MessageBoxResult.No => DirtyEditorCloseChoice.Discard,
            _ => DirtyEditorCloseChoice.Cancel
        };
    }

    private void OnConflictRequiresChoice()
    {
        var answer = WpfMessageBox.Show(this,
            "The authoritative LRC or sidecar changed externally.\n\nYes = Overwrite external changes; No = Reload external version; Cancel = Keep editing.",
            "External lyrics conflict", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        if (answer == MessageBoxResult.Yes)
            _viewModel.SaveCommand.Execute(EditorSaveAction.OverwriteExternalChanges);
        else if (answer == MessageBoxResult.No)
            _viewModel.ReloadExternalCommand.Execute(null);
    }

    private void OnExternalFileChanged(object sender, FileSystemEventArgs e) => ScheduleExternalCheck();
    private void OnExternalFileRenamed(object sender, RenamedEventArgs e) => ScheduleExternalCheck();
    private void OnExternalWatcherError(object sender, ErrorEventArgs e) => ScheduleExternalCheck();

    private void ScheduleExternalCheck()
    {
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            _externalChangeTimer.Stop();
            _externalChangeTimer.Start();
        }));
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _closed = true;
        _externalChangeTimer.Stop();
        _playbackRefreshTimer.Stop();
        _fileWatcher?.Dispose();
        _sidecarWatcher?.Dispose();
        _viewModel.Rows.CollectionChanged -= OnViewModelRowsCollectionChanged;
        _viewModel.ConflictRequiresChoice -= OnConflictRequiresChoice;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.Dispose();
    }
}
