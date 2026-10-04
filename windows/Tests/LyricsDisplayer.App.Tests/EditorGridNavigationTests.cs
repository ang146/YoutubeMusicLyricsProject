using LyricsDisplayer.Core.Library;

namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class EditorGridNavigationTests
{
    private static readonly EditorGridColumn Timestamp1 = new(EditorColumn.Timestamp, 0);
    private static readonly EditorGridColumn Timestamp2 = new(EditorColumn.Timestamp, 1);
    private static readonly EditorGridColumn Timestamp3 = new(EditorColumn.Timestamp, 2);
    private static readonly EditorGridColumn Lyrics = new(EditorColumn.Lyrics);

    [Test]
    public void TabMovesAcrossBasicTimestampColumnsAndLyrics()
    {
        var row = Guid.NewGuid();
        var rows = new[] { row };
        var columns = new[] { Timestamp1, Timestamp2, Lyrics };

        AssertMove(rows, columns, new(row, EditorColumn.Timestamp, 0), EditorGridNavigationKey.Tab,
            backwards: false, new(row, EditorColumn.Timestamp, 1));
        AssertMove(rows, columns, new(row, EditorColumn.Timestamp, 1), EditorGridNavigationKey.Tab,
            backwards: false, new(row, EditorColumn.Lyrics));
    }

    [Test]
    public void ReadOnlyLineIndicatorIsSkippedByTabAndEnterUsesFirstEditableColumn()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var rows = new[] { first, second };
        // The read-only Line DataGrid column has no EditorGridColumn mapping, so it is
        // intentionally absent from the editable navigation projection.
        var editableColumns = new[] { Timestamp1, Timestamp2, Lyrics };

        AssertLineMove(rows, editableColumns, first, EditorGridNavigationKey.Tab, backwards: false,
            new(first, EditorColumn.Timestamp, 0));
        AssertLineMove(rows, editableColumns, second, EditorGridNavigationKey.Tab, backwards: true,
            new(first, EditorColumn.Lyrics));
        AssertLineMove(rows, editableColumns, first, EditorGridNavigationKey.Enter, backwards: false,
            new(second, EditorColumn.Timestamp, 0));
        AssertLineMove(rows, editableColumns, second, EditorGridNavigationKey.Enter, backwards: true,
            new(first, EditorColumn.Timestamp, 0));
    }

    [Test]
    public void ShiftTabMovesBackAcrossLyricsAndTimestampColumns()
    {
        var row = Guid.NewGuid();
        var rows = new[] { row };
        var columns = new[] { Timestamp1, Timestamp2, Lyrics };

        AssertMove(rows, columns, new(row, EditorColumn.Lyrics), EditorGridNavigationKey.Tab,
            backwards: true, new(row, EditorColumn.Timestamp, 1));
        AssertMove(rows, columns, new(row, EditorColumn.Timestamp, 1), EditorGridNavigationKey.Tab,
            backwards: true, new(row, EditorColumn.Timestamp, 0));
    }

    [Test]
    public void TabAndShiftTabCrossRowBoundariesInDocumentOrder()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var rows = new[] { first, second };
        var columns = new[] { Timestamp1, Timestamp2, Lyrics };

        AssertMove(rows, columns, new(first, EditorColumn.Lyrics), EditorGridNavigationKey.Tab,
            backwards: false, new(second, EditorColumn.Timestamp, 0));
        AssertMove(rows, columns, new(second, EditorColumn.Timestamp, 0), EditorGridNavigationKey.Tab,
            backwards: true, new(first, EditorColumn.Lyrics));
    }

    [Test]
    public void ShiftTabAtFirstCellAndTabAtLastCellAreSafeNoOps()
    {
        var row = Guid.NewGuid();
        var rows = new[] { row };
        var columns = new[] { Timestamp1, Timestamp2, Lyrics };

        AssertNoMove(rows, columns, new(row, EditorColumn.Timestamp, 0), EditorGridNavigationKey.Tab,
            backwards: true);
        AssertNoMove(rows, columns, new(row, EditorColumn.Lyrics), EditorGridNavigationKey.Tab,
            backwards: false);
    }

    [Test]
    public void EnterMovesBetweenRowsInSameLogicalColumnAndIsSafeAtBoundaries()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var rows = new[] { first, second };
        var columns = new[] { Timestamp1, Timestamp2, Lyrics };

        AssertMove(rows, columns, new(first, EditorColumn.Timestamp, 1), EditorGridNavigationKey.Enter,
            backwards: false, new(second, EditorColumn.Timestamp, 1));
        AssertMove(rows, columns, new(second, EditorColumn.Lyrics), EditorGridNavigationKey.Enter,
            backwards: true, new(first, EditorColumn.Lyrics));
        AssertNoMove(rows, columns, new(first, EditorColumn.Timestamp, 0), EditorGridNavigationKey.Enter,
            backwards: true);
        AssertNoMove(rows, columns, new(second, EditorColumn.Lyrics), EditorGridNavigationKey.Enter,
            backwards: false);
    }

    [Test]
    public void MissingSelectionEmptyDocumentAndRemovedRowAreSafeNoOps()
    {
        var row = Guid.NewGuid();
        var columns = new[] { Timestamp1, Lyrics };

        foreach (var (key, backwards) in new[]
                 {
                     (EditorGridNavigationKey.Tab, false),
                     (EditorGridNavigationKey.Tab, true),
                     (EditorGridNavigationKey.Enter, false),
                     (EditorGridNavigationKey.Enter, true)
                 })
            AssertNoMove(Array.Empty<Guid>(), columns, new(row, EditorColumn.Timestamp, 0), key, backwards);
        AssertNoMove(new[] { row }, columns, new(null, EditorColumn.Lyrics),
            EditorGridNavigationKey.Tab, backwards: false);
        AssertNoMove(new[] { Guid.NewGuid() }, columns, new(row, EditorColumn.Timestamp, 0),
            EditorGridNavigationKey.Tab, backwards: false);
        AssertNoMove(new[] { row }, columns, new(row, EditorColumn.Timestamp),
            EditorGridNavigationKey.Tab, backwards: false);
    }

    [Test]
    public void DynamicTimestampExpansionUsesThePostCommitLogicalColumnModel()
    {
        var row = Guid.NewGuid();
        var expandedColumns = new[] { Timestamp1, Timestamp2, Timestamp3, Lyrics };

        AssertMove(new[] { row }, expandedColumns, new(row, EditorColumn.Timestamp, 1),
            EditorGridNavigationKey.Tab, backwards: false, new(row, EditorColumn.Timestamp, 2));
    }

    [Test]
    public void DynamicTimestampContractionResolvesRemovedColumnToNearestVisibleTimestamp()
    {
        var row = Guid.NewGuid();
        var contractedColumns = new[] { Timestamp1, Timestamp2, Lyrics };

        AssertMove(contractedColumns: contractedColumns, row: row,
            current: new(row, EditorColumn.Timestamp, 2), backwards: false,
            expected: new(row, EditorColumn.Lyrics));
        AssertMove(contractedColumns: contractedColumns, row: row,
            current: new(row, EditorColumn.Timestamp, 2), backwards: true,
            expected: new(row, EditorColumn.Timestamp, 0));
    }

    [Test]
    public void MultipleTimestampsAndBlankRowsUseTheSameSpreadsheetTraversal()
    {
        var first = Guid.NewGuid();
        var blank = Guid.NewGuid();
        var last = Guid.NewGuid();
        var rows = new[] { first, blank, last };
        var columns = new[] { Timestamp1, Timestamp2, Timestamp3, Lyrics };

        AssertMove(rows, columns, new(first, EditorColumn.Lyrics), EditorGridNavigationKey.Tab,
            backwards: false, new(blank, EditorColumn.Timestamp, 0));
        AssertMove(rows, columns, new(blank, EditorColumn.Timestamp, 0), EditorGridNavigationKey.Tab,
            backwards: false, new(blank, EditorColumn.Timestamp, 1));
        AssertMove(rows, columns, new(blank, EditorColumn.Timestamp, 2), EditorGridNavigationKey.Tab,
            backwards: false, new(blank, EditorColumn.Lyrics));
        AssertMove(rows, columns, new(blank, EditorColumn.Lyrics), EditorGridNavigationKey.Tab,
            backwards: false, new(last, EditorColumn.Timestamp, 0));
        AssertMove(rows, columns, new(last, EditorColumn.Timestamp, 0), EditorGridNavigationKey.Tab,
            backwards: true, new(blank, EditorColumn.Lyrics));
    }

    [Test]
    public void ReorderedVisibleColumnsAreTraversedByCurrentDisplayOrder()
    {
        var row = Guid.NewGuid();
        var currentDisplayOrder = new[] { Lyrics, Timestamp1, Timestamp2 };

        AssertMove(new[] { row }, currentDisplayOrder, new(row, EditorColumn.Timestamp, 0),
            EditorGridNavigationKey.Tab, backwards: false, new(row, EditorColumn.Timestamp, 1));
        AssertMove(new[] { row }, currentDisplayOrder, new(row, EditorColumn.Lyrics),
            EditorGridNavigationKey.Tab, backwards: false, new(row, EditorColumn.Timestamp, 0));
    }

    [Test]
    public void RowCollectionChangesResolveByStableIdentityNotOldRowIndex()
    {
        var first = Guid.NewGuid();
        var removed = Guid.NewGuid();
        var last = Guid.NewGuid();
        var columns = new[] { Timestamp1, Lyrics };

        AssertNoMove(new[] { first, last }, columns, new(removed, EditorColumn.Lyrics),
            EditorGridNavigationKey.Tab, backwards: false);
        AssertMove(new[] { first, Guid.NewGuid(), removed, last }, columns,
            new(removed, EditorColumn.Lyrics), EditorGridNavigationKey.Tab,
            backwards: false, new(last, EditorColumn.Timestamp, 0));
    }

    private static void AssertMove(IReadOnlyList<Guid> rows, IReadOnlyList<EditorGridColumn> columns,
        EditorSelection current, EditorGridNavigationKey key, bool backwards, EditorSelection expected)
    {
        var moved = EditorGridNavigation.TryMove(rows, columns, current, key, backwards, out var destination);
        Assert.That(moved, Is.True);
        Assert.That(destination, Is.EqualTo(expected));
    }

    private static void AssertMove(EditorGridColumn[] contractedColumns, Guid row,
        EditorSelection current, bool backwards, EditorSelection expected) =>
        AssertMove(new[] { row }, contractedColumns, current, EditorGridNavigationKey.Tab, backwards, expected);

    private static void AssertNoMove(IReadOnlyList<Guid> rows, IReadOnlyList<EditorGridColumn> columns,
        EditorSelection current, EditorGridNavigationKey key, bool backwards)
    {
        var moved = EditorGridNavigation.TryMove(rows, columns, current, key, backwards, out _);
        Assert.That(moved, Is.False);
    }

    private static void AssertLineMove(IReadOnlyList<Guid> rows, IReadOnlyList<EditorGridColumn> columns,
        Guid currentRow, EditorGridNavigationKey key, bool backwards, EditorSelection expected)
    {
        var moved = EditorGridNavigation.TryMoveFromLineNumber(rows, columns, currentRow, key, backwards,
            out var destination);
        Assert.That(moved, Is.True);
        Assert.That(destination, Is.EqualTo(expected));
    }
}
