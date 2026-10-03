using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using LyricsDisplayer.Core.Library;
using WpfApplication = System.Windows.Application;
using WpfBinding = System.Windows.Data.Binding;
using WpfDataGridCell = System.Windows.Controls.DataGridCell;
using WpfMessageBox = System.Windows.MessageBox;

namespace LyricsDisplayer;

public partial class BuiltInLyricsEditorWindow : Window
{
    private readonly BuiltInLyricsEditorViewModel _viewModel;
    private readonly FileSystemWatcher? _fileWatcher;
    private readonly FileSystemWatcher? _sidecarWatcher;
    private readonly DispatcherTimer _externalChangeTimer;
    private readonly DispatcherTimer _playbackRefreshTimer;
    private bool _closed;

    public BuiltInLyricsEditorWindow(BuiltInLyricsEditorViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
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
        for (var index = 0; index < count; index++)
        {
            var cellStyle = new Style(typeof(WpfDataGridCell));
            var invalidTimestamp = new DataTrigger
            {
                Binding = new WpfBinding($"Timestamps[{index}]") { Converter = InvalidTimestampConverter.Instance },
                Value = true
            };
            invalidTimestamp.Setters.Add(new Setter(BackgroundProperty, System.Windows.Media.Brushes.MistyRose));
            invalidTimestamp.Setters.Add(new Setter(ToolTipProperty, "Invalid LRC timestamp; saving remains allowed."));
            cellStyle.Triggers.Add(invalidTimestamp);
            LyricsGrid.Columns.Add(new DataGridTextColumn
            {
                Header = $"Timestamp {index + 1}",
                Binding = new WpfBinding($"Timestamps[{index}]")
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                },
                Width = new DataGridLength(112),
                CellStyle = cellStyle
            });
        }
        LyricsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Lyrics",
            Binding = new WpfBinding(nameof(EditorRowViewModel.LyricsText))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BuiltInLyricsEditorViewModel.TimestampColumnCount))
        {
            var selected = _viewModel.Selection;
            BuildTimestampColumns(_viewModel.TimestampColumnCount);
            if (selected.SelectedRowId is { } rowId)
                Dispatcher.BeginInvoke(new Action(() => RestoreSelectedCell(rowId, selected)));
        }
    }

    private void OnCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.Row.Item is not EditorRowViewModel row) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => _viewModel.CommitRowEdit(row)));
    }

    private void OnBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Row.Item is EditorRowViewModel row) UpdateSelection(row, e.Column);
    }

    private void OnSelectedCellsChanged(object? sender, SelectedCellsChangedEventArgs e) => SyncSelection();
    private void OnCurrentCellChanged(object? sender, EventArgs e) => SyncSelection();

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
        if (LyricsGrid.SelectedCells.Count == 1 && LyricsGrid.SelectedCells[0].Item is EditorRowViewModel row &&
            LyricsGrid.SelectedCells[0].Column is { } column)
            UpdateSelection(row, column);
        else if (_viewModel.Selection.SelectedRowId is not null)
            _viewModel.SelectCell(null, EditorColumn.Lyrics);
    }

    private void UpdateSelection(EditorRowViewModel row, DataGridColumn column)
    {
        var timestampIndex = LyricsGrid.Columns.IndexOf(column);
        if (timestampIndex >= 0 && timestampIndex < _viewModel.TimestampColumnCount)
            _viewModel.SelectCell(row.EditorLineId, EditorColumn.Timestamp, timestampIndex);
        else _viewModel.SelectCell(row.EditorLineId, EditorColumn.Lyrics);
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

        if (e.Key is not (Key.Enter or Key.Tab)) return;
        var backwards = e.Key == Key.Enter
            ? (Keyboard.Modifiers & ModifierKeys.Shift) != 0
            : (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        CommitGridEdits();
        MoveGridCell(e.Key, backwards);
        e.Handled = true;
    }

    private void MoveGridCell(Key key, bool backwards)
    {
        if (LyricsGrid.CurrentCell.Item is not EditorRowViewModel current || LyricsGrid.Items.Count == 0) return;
        var rowIndex = LyricsGrid.Items.IndexOf(current);
        var timestampColumns = _viewModel.TimestampColumnCount;
        var columnIndex = LyricsGrid.Columns.IndexOf(LyricsGrid.CurrentCell.Column);
        var logicalColumn = Math.Clamp(columnIndex, 0, timestampColumns);
        if (key == Key.Enter)
            rowIndex = Math.Clamp(rowIndex + (backwards ? -1 : 1), 0, LyricsGrid.Items.Count - 1);
        else
        {
            logicalColumn += backwards ? -1 : 1;
            if (logicalColumn < 0)
            {
                rowIndex = Math.Max(0, rowIndex - 1);
                logicalColumn = timestampColumns;
            }
            else if (logicalColumn > timestampColumns)
            {
                rowIndex = Math.Min(LyricsGrid.Items.Count - 1, rowIndex + 1);
                logicalColumn = 0;
            }
        }
        if (LyricsGrid.Items[rowIndex] is not EditorRowViewModel target) return;
        var cell = new DataGridCellInfo(target, LyricsGrid.Columns[logicalColumn]);
        LyricsGrid.CurrentCell = cell;
        LyricsGrid.SelectedCells.Clear();
        LyricsGrid.SelectedCells.Add(cell);
        UpdateSelection(target, LyricsGrid.Columns[logicalColumn]);
        LyricsGrid.ScrollIntoView(target, LyricsGrid.Columns[logicalColumn]);
        Dispatcher.BeginInvoke(new Action(() => LyricsGrid.BeginEdit()));
    }

    private void RestoreSelectedCell(Guid rowId, EditorSelection selection)
    {
        var row = _viewModel.Rows.FirstOrDefault(item => item.EditorLineId == rowId);
        if (row is null) return;
        var columnIndex = selection.SelectedColumn == EditorColumn.Lyrics
            ? LyricsGrid.Columns.Count - 1
            : Math.Clamp(selection.SelectedTimestampIndex ?? 0, 0, LyricsGrid.Columns.Count - 1);
        var cell = new DataGridCellInfo(row, LyricsGrid.Columns[columnIndex]);
        LyricsGrid.CurrentCell = cell;
        LyricsGrid.SelectedCells.Clear();
        LyricsGrid.SelectedCells.Add(cell);
    }

    private void CommitGridEdits()
    {
        LyricsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        LyricsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        _viewModel.CommitStagedEdits();
        _viewModel.CommitMetadataCommand.Execute(null);
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
        _viewModel.ConflictRequiresChoice -= OnConflictRequiresChoice;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.Dispose();
    }
}

internal sealed class InvalidTimestampConverter : IValueConverter
{
    public static InvalidTimestampConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string timestamp && !string.IsNullOrWhiteSpace(timestamp) &&
        !EditorDocumentCodec.TryParseTimestamp(timestamp, out _);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        System.Windows.Data.Binding.DoNothing;
}
