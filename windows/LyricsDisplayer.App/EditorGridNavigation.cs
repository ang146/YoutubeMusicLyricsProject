using LyricsDisplayer.Core.Library;

namespace LyricsDisplayer;

public enum EditorGridNavigationKey
{
    Tab,
    Enter
}

/// <summary>A stable identity for one editable grid column, independent of DataGrid indices.</summary>
public readonly record struct EditorGridColumn(EditorColumn Column, int? TimestampIndex = null);

/// <summary>Resolves editor navigation against the current rows and visible logical columns.</summary>
public static class EditorGridNavigation
{
    public static bool TryResolveColumn(IReadOnlyList<EditorGridColumn> visibleColumns,
        EditorSelection selection, out EditorGridColumn resolved)
    {
        ArgumentNullException.ThrowIfNull(visibleColumns);
        ArgumentNullException.ThrowIfNull(selection);
        resolved = default;

        var requested = selection.SelectedColumn == EditorColumn.Lyrics
            ? new EditorGridColumn(EditorColumn.Lyrics)
            : new EditorGridColumn(EditorColumn.Timestamp, selection.SelectedTimestampIndex);
        for (var index = 0; index < visibleColumns.Count; index++)
        {
            if (visibleColumns[index] == requested)
            {
                resolved = requested;
                return true;
            }
        }

        // If an edit contracts the dynamic timestamp set while the caret is in its former
        // trailing column, continue from the nearest timestamp column that still exists.
        if (selection.SelectedColumn != EditorColumn.Timestamp ||
            selection.SelectedTimestampIndex is not { } requestedIndex || requestedIndex < 0)
            return false;

        var nearestIndex = -1;
        var nearestDistance = int.MaxValue;
        for (var index = 0; index < visibleColumns.Count; index++)
        {
            var column = visibleColumns[index];
            if (column.Column != EditorColumn.Timestamp || column.TimestampIndex is not { } timestampIndex)
                continue;

            var distance = Math.Abs(timestampIndex - requestedIndex);
            if (distance >= nearestDistance) continue;
            nearestDistance = distance;
            nearestIndex = index;
        }

        if (nearestIndex < 0) return false;
        resolved = visibleColumns[nearestIndex];
        return true;
    }

    public static bool TryMove(IReadOnlyList<Guid> orderedRowIds,
        IReadOnlyList<EditorGridColumn> visibleColumns, EditorSelection current,
        EditorGridNavigationKey key, bool backwards, out EditorSelection destination)
    {
        ArgumentNullException.ThrowIfNull(orderedRowIds);
        ArgumentNullException.ThrowIfNull(visibleColumns);
        ArgumentNullException.ThrowIfNull(current);
        destination = new(null, EditorColumn.Lyrics);

        if (orderedRowIds.Count == 0 || visibleColumns.Count == 0 || current.SelectedRowId is not { } rowId)
            return false;

        var rowIndex = IndexOf(orderedRowIds, rowId);
        if (rowIndex < 0 || !TryResolveColumn(visibleColumns, current, out var currentColumn)) return false;

        var columnIndex = IndexOf(visibleColumns, currentColumn);
        if (columnIndex < 0) return false;

        if (key == EditorGridNavigationKey.Enter)
        {
            var targetRowIndex = rowIndex + (backwards ? -1 : 1);
            if (targetRowIndex < 0 || targetRowIndex >= orderedRowIds.Count) return false;
            destination = ToSelection(orderedRowIds[targetRowIndex], currentColumn);
            return true;
        }

        var targetColumnIndex = columnIndex + (backwards ? -1 : 1);
        var targetRow = rowIndex;
        if (targetColumnIndex < 0)
        {
            targetRow--;
            if (targetRow < 0) return false;
            targetColumnIndex = visibleColumns.Count - 1;
        }
        else if (targetColumnIndex >= visibleColumns.Count)
        {
            targetRow++;
            if (targetRow >= orderedRowIds.Count) return false;
            targetColumnIndex = 0;
        }

        destination = ToSelection(orderedRowIds[targetRow], visibleColumns[targetColumnIndex]);
        return true;
    }

    public static bool TryMoveFromLineNumber(IReadOnlyList<Guid> orderedRowIds,
        IReadOnlyList<EditorGridColumn> editableColumns, Guid currentRowId,
        EditorGridNavigationKey key, bool backwards, out EditorSelection destination)
    {
        ArgumentNullException.ThrowIfNull(orderedRowIds);
        ArgumentNullException.ThrowIfNull(editableColumns);
        destination = new(null, EditorColumn.Lyrics);
        if (editableColumns.Count == 0 || IndexOf(orderedRowIds, currentRowId) < 0)
            return false;

        var firstColumn = editableColumns[0];
        var firstCell = new EditorSelection(currentRowId, firstColumn.Column, firstColumn.TimestampIndex);
        if (key == EditorGridNavigationKey.Tab && !backwards)
        {
            destination = firstCell;
            return true;
        }

        return TryMove(orderedRowIds, editableColumns, firstCell, key, backwards, out destination);
    }

    private static EditorSelection ToSelection(Guid rowId, EditorGridColumn column) =>
        new(rowId, column.Column, column.TimestampIndex);

    private static int IndexOf<T>(IReadOnlyList<T> values, T value)
    {
        for (var index = 0; index < values.Count; index++)
            if (EqualityComparer<T>.Default.Equals(values[index], value)) return index;
        return -1;
    }
}
