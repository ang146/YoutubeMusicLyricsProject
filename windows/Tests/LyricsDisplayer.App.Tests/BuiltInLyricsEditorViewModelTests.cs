using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Core.Playback;
using LyricsDisplayer.Core.Protocol;

namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class BuiltInLyricsEditorViewModelTests
{
    [Test]
    public void ValidDocumentShowsZeroCountsAndPhysicalLineNumbersIncludingHiddenMetadataAndBlankRows()
    {
        using var fixture = CreateViewModel("[ti:Song]\nFirst\n\n[00:01.000]Last");
        var viewModel = fixture.ViewModel;

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.WarningCount, Is.Zero);
            Assert.That(viewModel.ErrorCount, Is.Zero);
            Assert.That(viewModel.HasValidationDiagnostics, Is.False);
            Assert.That(viewModel.Rows.Select(row => row.PhysicalLineNumber), Is.EqualTo(new[] { 2, 3, 4 }));
            Assert.That(viewModel.Rows.Select(row => row.LineDisplay), Is.EqualTo(new[] { "2", "3", "4" }));
            Assert.That(viewModel.Rows[1].RowDiagnosticSeverity, Is.Null,
                "A blank physical row is valid content and receives no diagnostic styling.");
        });
    }

    [Test]
    public void DocumentAggregateCountsTwoWarningsAndOneErrorAndProjectsThemToPhysicalRows()
    {
        using var fixture = CreateViewModel(
            "[ti:Song]\n[00:30.000]First\n[x-custom:keep]\n\n[00:20.000]Second\n[00:xx.000]Broken");
        var viewModel = fixture.ViewModel;

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.WarningCount, Is.EqualTo(2));
            Assert.That(viewModel.ErrorCount, Is.EqualTo(1));
            Assert.That(viewModel.HasValidationDiagnostics, Is.True);
            Assert.That(viewModel.Rows.Select(row => row.PhysicalLineNumber), Is.EqualTo(new[] { 2, 3, 4, 5, 6 }));
            Assert.That(viewModel.Rows.Select(row => row.LineDisplay), Is.EqualTo(new[] { "2", "3 W", "4", "5 W", "6 E" }));
            Assert.That(viewModel.Rows[1].DiagnosticDetails, Does.Contain("Warning - Line 3"));
            Assert.That(viewModel.Rows[4].DiagnosticDetails, Does.Contain("Error - Line 6"));
        });
    }

    [Test]
    public void EditingMalformedLyricsTextImmediatelyClearsItsErrorPresentation()
    {
        using var fixture = CreateViewModel("[00:xx.000]Broken text");
        var viewModel = fixture.ViewModel;
        var row = viewModel.Rows.Single();
        Assert.That(viewModel.ErrorCount, Is.EqualTo(1));

        row.LyricsText = "Fixed lyric text";
        viewModel.CommitRowEdit(row);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.ErrorCount, Is.Zero);
            Assert.That(viewModel.WarningCount, Is.Zero);
            Assert.That(viewModel.HasValidationDiagnostics, Is.False);
            Assert.That(viewModel.Rows.Single().RowDiagnosticSeverity, Is.Null);
            Assert.That(viewModel.Rows.Single().DiagnosticDetails, Is.Null);
        });
    }

    [Test]
    public void WarningAndErrorCountsAndRowDetailsUpdateLiveWithErrorWinningVisually()
    {
        using var fixture = CreateViewModel("[00:30.000]First\n[00:20.000]Second");
        var viewModel = fixture.ViewModel;
        var secondRow = viewModel.Rows[1];
        secondRow.Timestamps[1] = "not-a-time";

        viewModel.CommitStagedEdits();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.WarningCount, Is.EqualTo(1));
            Assert.That(viewModel.ErrorCount, Is.EqualTo(1));
            Assert.That(viewModel.HasValidationDiagnostics, Is.True);
            Assert.That(viewModel.Rows[1], Is.Not.SameAs(secondRow),
                "Adding a dynamic timestamp column rebuilds the row projection without stale diagnostics.");
            Assert.That(viewModel.TimestampColumnCount, Is.EqualTo(3));
            Assert.That(viewModel.Rows[1].RowDiagnosticSeverity, Is.EqualTo(EditorValidationSeverity.Error));
            Assert.That(viewModel.Rows[1].LineDisplay, Is.EqualTo("2 E"));
            Assert.That(viewModel.Rows[1].DiagnosticDetails, Does.Contain("Warning - Line 2"));
            Assert.That(viewModel.Rows[1].DiagnosticDetails, Does.Contain("Error - Line 2"));
            Assert.That(viewModel.Rows[1].DiagnosticDetails, Does.Contain("earlier than the preceding document timestamp"));
            Assert.That(viewModel.Rows[1].DiagnosticDetails, Does.Contain("not-a-time"));
            Assert.That(viewModel.SaveCommand.CanExecute(null), Is.True,
                "Validation errors do not gate Save for a dirty document.");
        });

        viewModel.Rows[1].Timestamps[1] = string.Empty;
        viewModel.CommitStagedEdits();
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.WarningCount, Is.EqualTo(1));
            Assert.That(viewModel.ErrorCount, Is.Zero);
            Assert.That(viewModel.Rows[1].RowDiagnosticSeverity, Is.EqualTo(EditorValidationSeverity.Warning));
            Assert.That(viewModel.Rows[1].LineDisplay, Is.EqualTo("2 W"));
            Assert.That(viewModel.TimestampColumnCount, Is.EqualTo(2));
        });

        viewModel.Rows[1].Timestamps[0] = "00:40.000";
        viewModel.CommitStagedEdits();
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.WarningCount, Is.Zero);
            Assert.That(viewModel.ErrorCount, Is.Zero);
            Assert.That(viewModel.HasValidationDiagnostics, Is.False);
            Assert.That(viewModel.Rows[1].RowDiagnosticSeverity, Is.Null);
            Assert.That(viewModel.Rows[1].DiagnosticDetails, Is.Null);
        });
    }

    [Test]
    public void UndoRedoAndDeleteUndoRefreshDiagnosticCountsAndRowPresentation()
    {
        using (var fixture = CreateViewModel("[00:01.000]Timed line"))
        {
            var viewModel = fixture.ViewModel;
            viewModel.Rows[0].Timestamps[0] = "invalid";
            viewModel.CommitStagedEdits();
            Assert.That(viewModel.ErrorCount, Is.EqualTo(1));

            viewModel.UndoCommand.Execute(null);
            Assert.Multiple(() =>
            {
                Assert.That(viewModel.ErrorCount, Is.Zero);
                Assert.That(viewModel.HasValidationDiagnostics, Is.False);
                Assert.That(viewModel.Rows[0].RowDiagnosticSeverity, Is.Null);
            });

            viewModel.RedoCommand.Execute(null);
            Assert.Multiple(() =>
            {
                Assert.That(viewModel.ErrorCount, Is.EqualTo(1));
                Assert.That(viewModel.Rows[0].RowDiagnosticSeverity, Is.EqualTo(EditorValidationSeverity.Error));
            });
        }

        using (var fixture = CreateViewModel("[x-custom:preserve]\nValid row"))
        {
            var viewModel = fixture.ViewModel;
            var diagnosticRowId = viewModel.Rows[0].EditorLineId;
            Assert.That(viewModel.WarningCount, Is.EqualTo(1));
            viewModel.SelectCell(diagnosticRowId, EditorColumn.Lyrics);
            viewModel.DeleteRowCommand.Execute(null);

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.WarningCount, Is.Zero);
                Assert.That(viewModel.HasValidationDiagnostics, Is.False);
                Assert.That(viewModel.Selection.SelectedRowId, Is.Null);
            });

            viewModel.UndoCommand.Execute(null);
            Assert.Multiple(() =>
            {
                Assert.That(viewModel.WarningCount, Is.EqualTo(1));
                Assert.That(viewModel.Rows[0].EditorLineId, Is.EqualTo(diagnosticRowId));
                Assert.That(viewModel.Rows[0].PhysicalLineNumber, Is.EqualTo(1));
                Assert.That(viewModel.Rows[0].RowDiagnosticSeverity, Is.EqualTo(EditorValidationSeverity.Warning));
            });
        }
    }

    [Test]
    public void RowRelativeCommandsRequireCurrentSelectionWhileAppendDoesNot()
    {
        using var fixture = CreateViewModel("A\nB\nC\n");
        var viewModel = fixture.ViewModel;

        AssertRowCommandState(viewModel, insertAbove: false, insertBelow: false, delete: false, append: true);
        viewModel.InsertRowAboveCommand.Execute(null);
        viewModel.InsertRowBelowCommand.Execute(null);
        viewModel.DeleteRowCommand.Execute(null);
        AssertRows(viewModel, "A", "B", "C");

        var rowB = viewModel.Rows[1].EditorLineId;
        viewModel.SelectCell(rowB, EditorColumn.Lyrics);
        AssertRowCommandState(viewModel, insertAbove: true, insertBelow: true, delete: true, append: true);

        viewModel.SelectCell(Guid.NewGuid(), EditorColumn.Lyrics);
        Assert.That(viewModel.Selection.SelectedRowId, Is.Null, "A non-existent row ID is not retained as a valid selection.");
        AssertRowCommandState(viewModel, insertAbove: false, insertBelow: false, delete: false, append: true);
        viewModel.InsertRowAboveCommand.Execute(null);
        viewModel.InsertRowBelowCommand.Execute(null);
        viewModel.DeleteRowCommand.Execute(null);
        AssertRows(viewModel, "A", "B", "C");
    }

    [Test]
    public void LoadedBlankPhysicalLineIsAnOrdinarySelectableEditorRow()
    {
        using var fixture = CreateViewModel("[ti:Song]\nA\n\n\nB\n");
        var viewModel = fixture.ViewModel;

        AssertRows(viewModel, "A", "", "", "B");
        Assert.That(viewModel.IsDirty, Is.False);
        var firstBlank = viewModel.Rows[1];
        Assert.That(firstBlank.EditorLineId, Is.Not.EqualTo(Guid.Empty));
        Assert.That(firstBlank.Timestamps, Is.All.Empty);

        viewModel.SelectCell(firstBlank.EditorLineId, EditorColumn.Lyrics);
        AssertRowCommandState(viewModel, insertAbove: true, insertBelow: true, delete: true, append: true);
        viewModel.DeleteRowCommand.Execute(null);
        AssertRows(viewModel, "A", "", "B");
        Assert.That(viewModel.Selection.SelectedRowId, Is.Null);
        AssertRowCommandState(viewModel, insertAbove: false, insertBelow: false, delete: false, append: true);
    }

    [Test]
    public void DeleteClearsSelectionAndDisablesRowCommandsUntilAnotherRowIsSelected()
    {
        using var fixture = CreateViewModel("A\nB\nC\n");
        var viewModel = fixture.ViewModel;
        var rowA = viewModel.Rows[0].EditorLineId;
        var rowB = viewModel.Rows[1].EditorLineId;
        var rowC = viewModel.Rows[2].EditorLineId;
        var deleteCommandStateChanges = 0;
        viewModel.DeleteRowCommand.CanExecuteChanged += (_, _) => deleteCommandStateChanges++;

        viewModel.SelectCell(rowB, EditorColumn.Lyrics);
        var changesAfterSelection = deleteCommandStateChanges;
        viewModel.DeleteRowCommand.Execute(null);
        AssertRows(viewModel, "A", "C");
        Assert.That(deleteCommandStateChanges, Is.GreaterThan(changesAfterSelection));
        Assert.That(viewModel.Selection.SelectedRowId, Is.Null);
        AssertRowCommandState(viewModel, insertAbove: false, insertBelow: false, delete: false, append: true);

        viewModel.DeleteRowCommand.Execute(null);
        AssertRows(viewModel, "A", "C");
        Assert.That(viewModel.Selection.SelectedRowId, Is.Null);
        AssertRowCommandState(viewModel, insertAbove: false, insertBelow: false, delete: false, append: true);

        viewModel.SelectCell(rowC, EditorColumn.Lyrics);
        viewModel.DeleteRowCommand.Execute(null);
        AssertRows(viewModel, "A");
        Assert.That(viewModel.Selection.SelectedRowId, Is.Null);
        AssertRowCommandState(viewModel, insertAbove: false, insertBelow: false, delete: false, append: true);

        viewModel.SelectCell(rowA, EditorColumn.Lyrics);

        viewModel.DeleteRowCommand.Execute(null);
        AssertRows(viewModel);
        Assert.That(viewModel.Selection.SelectedRowId, Is.Null);
        AssertRowCommandState(viewModel, insertAbove: false, insertBelow: false, delete: false, append: true);

        viewModel.DeleteRowCommand.Execute(null);
        viewModel.InsertRowAboveCommand.Execute(null);
        viewModel.InsertRowBelowCommand.Execute(null);
        AssertRows(viewModel);
        viewModel.AppendRowCommand.Execute(null);
        AssertRows(viewModel, "");
        Assert.That(viewModel.Selection.SelectedRowId, Is.EqualTo(viewModel.Rows[0].EditorLineId));
        AssertRowCommandState(viewModel, insertAbove: true, insertBelow: true, delete: true, append: true);
    }

    [Test]
    public void LastAndOnlyRowDeletionClearSelectionWithoutRetainingStaleIdentity()
    {
        using var fixture = CreateViewModel("A\nB\n");
        var viewModel = fixture.ViewModel;
        var rowA = viewModel.Rows[0].EditorLineId;
        var rowB = viewModel.Rows[1].EditorLineId;
        viewModel.SelectCell(rowB, EditorColumn.Lyrics);

        viewModel.DeleteRowCommand.Execute(null);
        AssertRows(viewModel, "A");
        Assert.That(viewModel.Selection.SelectedRowId, Is.Null);
        Assert.That(viewModel.DeleteRowCommand.CanExecute(null), Is.False);

        viewModel.SelectCell(rowA, EditorColumn.Lyrics);
        viewModel.DeleteRowCommand.Execute(null);
        var afterOnlyRowDelete = viewModel.Rows.Select(row => row.EditorLineId).ToArray();
        viewModel.DeleteRowCommand.Execute(null);
        Assert.That(viewModel.Rows.Select(row => row.EditorLineId), Is.EqualTo(afterOnlyRowDelete));
        Assert.That(viewModel.Selection.SelectedRowId, Is.Null);
        Assert.That(viewModel.DeleteRowCommand.CanExecute(null), Is.False);
    }

    [Test]
    public void UndoRedoClearSelectionWhenItsRowIdentityNoLongerExists()
    {
        using var fixture = CreateViewModel("A\nB\nC\n");
        var viewModel = fixture.ViewModel;
        var rowB = viewModel.Rows[1].EditorLineId;
        viewModel.SelectCell(rowB, EditorColumn.Lyrics);
        Assert.That(viewModel.DeleteRowCommand.CanExecute(null), Is.True);
        viewModel.SelectCell(null, EditorColumn.Lyrics);
        viewModel.DeleteRowCommand.Execute(null);
        AssertRows(viewModel, "A", "B", "C");

        viewModel.SelectCell(rowB, EditorColumn.Lyrics);
        viewModel.InsertRowAboveCommand.Execute(null);
        var insertedId = viewModel.Selection.SelectedRowId;
        Assert.That(insertedId, Is.Not.Null);

        viewModel.UndoCommand.Execute(null);
        AssertRows(viewModel, "A", "B", "C");
        Assert.That(viewModel.Selection.SelectedRowId, Is.Null);
        AssertRowCommandState(viewModel, insertAbove: false, insertBelow: false, delete: false, append: true);

        viewModel.RedoCommand.Execute(null);
        AssertRows(viewModel, "A", "", "B", "C");
        Assert.That(viewModel.Selection.SelectedRowId, Is.Null);
        Assert.That(viewModel.Rows.Any(row => row.EditorLineId == insertedId), Is.True);
        AssertRowCommandState(viewModel, insertAbove: false, insertBelow: false, delete: false, append: true);

        viewModel.SelectCell(rowB, EditorColumn.Lyrics);
        viewModel.DeleteRowCommand.Execute(null);
        AssertRows(viewModel, "A", "", "C");
        Assert.That(viewModel.Selection.SelectedRowId, Is.Null);
        AssertRowCommandState(viewModel, insertAbove: false, insertBelow: false, delete: false, append: true);
        viewModel.UndoCommand.Execute(null);
        AssertRows(viewModel, "A", "", "B", "C");
        Assert.That(viewModel.Selection.SelectedRowId, Is.Null);
        AssertRowCommandState(viewModel, insertAbove: false, insertBelow: false, delete: false, append: true);
        viewModel.RedoCommand.Execute(null);
        AssertRows(viewModel, "A", "", "C");
        Assert.That(viewModel.Selection.SelectedRowId, Is.Null);
        AssertRowCommandState(viewModel, insertAbove: false, insertBelow: false, delete: false, append: true);
    }

    [Test]
    public void ConsecutiveRowCommandsImmediatelyUpdateObservableRowsAndPreserveIdentity()
    {
        using var fixture = CreateViewModel("A\nB\nC\n");
        var rowA = fixture.ViewModel.Rows[0].EditorLineId;
        var rowB = fixture.ViewModel.Rows[1].EditorLineId;
        var rowC = fixture.ViewModel.Rows[2].EditorLineId;
        var notifications = 0;
        fixture.ViewModel.Rows.CollectionChanged += (_, _) => notifications++;
        fixture.ViewModel.SelectCell(rowB, EditorColumn.Lyrics);

        fixture.ViewModel.InsertRowAboveCommand.Execute(null);
        AssertRows(fixture.ViewModel, "A", "", "B", "C");
        Assert.That(fixture.ViewModel.Rows[0].EditorLineId, Is.EqualTo(rowA));
        Assert.That(fixture.ViewModel.Rows[1].EditorLineId, Is.EqualTo(fixture.ViewModel.Selection.SelectedRowId));
        Assert.That(fixture.ViewModel.Rows[2].EditorLineId, Is.EqualTo(rowB));
        Assert.That(fixture.ViewModel.Rows[3].EditorLineId, Is.EqualTo(rowC));
        Assert.That(fixture.ViewModel.Rows[1].Timestamps, Is.All.Empty);

        fixture.ViewModel.SelectCell(rowB, EditorColumn.Lyrics);
        fixture.ViewModel.InsertRowBelowCommand.Execute(null);
        AssertRows(fixture.ViewModel, "A", "", "B", "", "C");
        Assert.That(fixture.ViewModel.Rows.Select(row => row.EditorLineId).ElementAt(2), Is.EqualTo(rowB));
        Assert.That(fixture.ViewModel.Rows.Select(row => row.EditorLineId).ElementAt(4), Is.EqualTo(rowC));

        fixture.ViewModel.AppendRowCommand.Execute(null);
        AssertRows(fixture.ViewModel, "A", "", "B", "", "C", "");
        Assert.That(fixture.ViewModel.Rows[^1].Timestamps, Is.All.Empty);

        fixture.ViewModel.SelectCell(rowB, EditorColumn.Lyrics);
        fixture.ViewModel.DeleteRowCommand.Execute(null);
        AssertRows(fixture.ViewModel, "A", "", "", "C", "");
        Assert.That(fixture.ViewModel.Rows.Select(row => row.EditorLineId),
            Does.Contain(rowA).And.Contain(rowC));

        fixture.ViewModel.AppendRowCommand.Execute(null);
        AssertRows(fixture.ViewModel, "A", "", "", "C", "", "");

        var appendedRowId = fixture.ViewModel.Rows[^1].EditorLineId;
        fixture.ViewModel.InsertRowAboveCommand.Execute(null);
        AssertRows(fixture.ViewModel, "A", "", "", "C", "", "", "");
        Assert.That(fixture.ViewModel.Rows[^1].EditorLineId, Is.EqualTo(appendedRowId),
            "Append followed by Insert Above places the inserted row immediately before the appended row.");
        Assert.That(notifications, Is.GreaterThan(0));
    }

    [Test]
    public void InsertAndDeleteUndoRedoImmediatelyRestoreObservableRows()
    {
        using var fixture = CreateViewModel("A\nB\nC\n");
        var rowB = fixture.ViewModel.Rows[1].EditorLineId;
        fixture.ViewModel.SelectCell(rowB, EditorColumn.Lyrics);

        fixture.ViewModel.InsertRowAboveCommand.Execute(null);
        AssertRows(fixture.ViewModel, "A", "", "B", "C");
        fixture.ViewModel.UndoCommand.Execute(null);
        AssertRows(fixture.ViewModel, "A", "B", "C");
        fixture.ViewModel.RedoCommand.Execute(null);
        AssertRows(fixture.ViewModel, "A", "", "B", "C");

        fixture.ViewModel.SelectCell(rowB, EditorColumn.Lyrics);
        fixture.ViewModel.DeleteRowCommand.Execute(null);
        AssertRows(fixture.ViewModel, "A", "", "C");
        fixture.ViewModel.UndoCommand.Execute(null);
        AssertRows(fixture.ViewModel, "A", "", "B", "C");
        fixture.ViewModel.RedoCommand.Execute(null);
        AssertRows(fixture.ViewModel, "A", "", "C");
    }

    [Test]
    public void InsertingNeighbouringRowKeepsAllTimestampOccurrencesInProjection()
    {
        using var fixture = CreateViewModel("A\n[01:00.000][02:30.000][03:00.000]B\nC\n");
        var rowB = fixture.ViewModel.Rows[1].EditorLineId;
        fixture.ViewModel.SelectCell(rowB, EditorColumn.Lyrics);

        fixture.ViewModel.InsertRowAboveCommand.Execute(null);

        AssertRows(fixture.ViewModel, "A", "", "B", "C");
        Assert.That(fixture.ViewModel.Rows[2].Timestamps.Take(3),
            Is.EqualTo(new[] { "01:00.000", "02:30.000", "03:00.000" }));
    }

    [Test]
    public void CommittingStagedTimestampPreservesValueAndExpandsVisibleColumnCount()
    {
        using var fixture = CreateViewModel("[00:00.000]Line\n");
        var viewModel = fixture.ViewModel;
        var editingRow = viewModel.Rows[0];

        Assert.That(viewModel.TimestampColumnCount, Is.EqualTo(2));
        editingRow.Timestamps[1] = "00:01.000";
        viewModel.CommitStagedEdits();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Rows[0], Is.Not.SameAs(editingRow),
                "Expanding the timestamp column shape rebuilds the row projection.");
            Assert.That(viewModel.Rows[0].Timestamps[1], Is.EqualTo("00:01.000"));
            Assert.That(viewModel.TimestampColumnCount, Is.EqualTo(3));
        });
    }

    [Test]
    public void CommittingRowWithoutColumnShapeChangePreservesProjectionAndLogicalSelection()
    {
        using var fixture = CreateViewModel("[00:00.000]Line\n");
        var viewModel = fixture.ViewModel;
        var row = viewModel.Rows[0];
        var selection = new EditorSelection(row.EditorLineId, EditorColumn.Timestamp, 0);
        viewModel.SelectCell(selection.SelectedRowId, selection.SelectedColumn, selection.SelectedTimestampIndex);
        row.Timestamps[0] = "00:01.000";

        viewModel.CommitStagedEdits();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Rows[0], Is.SameAs(row));
            Assert.That(viewModel.Rows[0].Timestamps[0], Is.EqualTo("00:01.000"));
            Assert.That(viewModel.Selection, Is.EqualTo(selection));
            Assert.That(viewModel.TimestampColumnCount, Is.EqualTo(2));
            Assert.That(viewModel.IsDirty, Is.True);
        });
    }

    [Test]
    public void CommittingOneEditedRowWithoutColumnExpansionDoesNotReplaceGridItems()
    {
        using var fixture = CreateViewModel("[00:00.000]Line\nOther\n");
        var viewModel = fixture.ViewModel;
        var editingRow = viewModel.Rows[0];
        var selectedRow = viewModel.Rows[1];
        var selection = new EditorSelection(selectedRow.EditorLineId, EditorColumn.Timestamp, 0);
        viewModel.SelectCell(selection.SelectedRowId, selection.SelectedColumn, selection.SelectedTimestampIndex);
        editingRow.LyricsText = "Edited line";

        viewModel.CommitRowEdit(editingRow);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Rows[0], Is.SameAs(editingRow));
            Assert.That(viewModel.Rows[1], Is.SameAs(selectedRow));
            Assert.That(viewModel.Selection, Is.EqualTo(selection));
            Assert.That(viewModel.Rows[0].LyricsText, Is.EqualTo("Edited line"));
            Assert.That(viewModel.IsDirty, Is.True);
        });
    }

    [Test]
    public void CommittingMetadataDoesNotRebuildOrMoveTheLyricsGridRows()
    {
        using var fixture = CreateViewModel("Line\n");
        var viewModel = fixture.ViewModel;
        var row = viewModel.Rows[0];
        var selection = new EditorSelection(row.EditorLineId, EditorColumn.Lyrics);
        viewModel.SelectCell(selection.SelectedRowId, selection.SelectedColumn);
        viewModel.TitleOverride = "Updated title";

        viewModel.CommitMetadataCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Rows[0], Is.SameAs(row));
            Assert.That(viewModel.Selection, Is.EqualTo(selection));
            Assert.That(viewModel.EffectiveTitle, Is.EqualTo("Updated title"));
            Assert.That(viewModel.IsDirty, Is.True);
        });
    }

    [TestCase(EditorValidationSeverity.Warning)]
    [TestCase(EditorValidationSeverity.Error)]
    public void SaveRemainsAvailableAndWritesTheChosenContentWithDiagnostics(EditorValidationSeverity severity)
    {
        var root = CreateLibraryRoot();
        try
        {
            using var library = new LyricsLibrary(CreateLibraryPaths(root));
            Assert.That(library.Initialise().Completed, Is.True);
            var track = ImportTrack(library, "track-a");
            var imported = library.LoadForEditing(track.Record).Asset!;
            const string source = "[00:01.000]First\n[00:02.000]Second";
            File.WriteAllText(imported.LyricsPath, source);
            var asset = library.LoadForEditing(track.Record).Asset!;
            using var viewModel = new BuiltInLyricsEditorViewModel(asset, library,
                new PlaybackStateCoordinator(new SnapshotStateTracker(), new PlaybackClock(), library));

            if (severity == EditorValidationSeverity.Warning)
                viewModel.Rows[1].Timestamps[0] = "00:00.500";
            else
                viewModel.Rows[0].Timestamps[0] = "not-a-time";
            viewModel.CommitStagedEdits();

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.WarningCount, Is.EqualTo(severity == EditorValidationSeverity.Warning ? 1 : 0));
                Assert.That(viewModel.ErrorCount, Is.EqualTo(severity == EditorValidationSeverity.Error ? 1 : 0));
                Assert.That(viewModel.HasValidationDiagnostics, Is.True);
                Assert.That(viewModel.SaveCommand.CanExecute(null), Is.True,
                    "Warnings and errors are advisory and do not gate Save.");
            });

            viewModel.SaveCommand.Execute(EditorSaveAction.Save);
            var saved = library.LoadForEditing(track.Record).Asset!;
            Assert.Multiple(() =>
            {
                Assert.That(viewModel.IsDirty, Is.False);
                Assert.That(viewModel.Status, Does.StartWith("Saved."));
                Assert.That(saved.LrcContent, Does.Contain(severity == EditorValidationSeverity.Warning
                    ? "[00:00.500]Second" : "[not-a-time]First"));
            });
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Test]
    public void CleanExternalReloadRefreshesDiagnosticCountsAndRowLineNumbers()
    {
        var root = CreateLibraryRoot();
        try
        {
            using var library = new LyricsLibrary(CreateLibraryPaths(root));
            Assert.That(library.Initialise().Completed, Is.True);
            var track = ImportTrack(library, "track-a");
            var imported = library.LoadForEditing(track.Record).Asset!;
            const string original = "[ti:Song]\nFirst\nSecond";
            File.WriteAllText(imported.LyricsPath, original);
            var asset = library.LoadForEditing(track.Record).Asset!;
            using var viewModel = new BuiltInLyricsEditorViewModel(asset, library,
                new PlaybackStateCoordinator(new SnapshotStateTracker(), new PlaybackClock(), library));
            Assert.That(viewModel.HasValidationDiagnostics, Is.False);

            File.WriteAllText(asset.LyricsPath,
                "[ti:Song]\n[00:30.000]First\n[x-custom:keep]\n[00:20.000]Second");
            viewModel.NotifyExternalChange();

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.IsDirty, Is.False);
                Assert.That(viewModel.WarningCount, Is.EqualTo(2));
                Assert.That(viewModel.ErrorCount, Is.Zero);
                Assert.That(viewModel.HasValidationDiagnostics, Is.True);
                Assert.That(viewModel.Rows.Select(row => row.PhysicalLineNumber), Is.EqualTo(new[] { 2, 3, 4 }));
                Assert.That(viewModel.Rows[1].LineDisplay, Is.EqualTo("3 W"));
                Assert.That(viewModel.Rows[2].LineDisplay, Is.EqualTo("4 W"));
            });
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Test]
    public void MetadataOnlySavePersistsBothOverridesKeepsEditorCleanAndUpdatesOnlyTheActiveTrack()
    {
        var root = CreateLibraryRoot();
        try
        {
            using var library = new LyricsLibrary(CreateLibraryPaths(root));
            Assert.That(library.Initialise().Completed, Is.True);
            var trackA = ImportTrack(library, "track-a");
            var trackB = ImportTrack(library, "track-b");
            var playback = new PlaybackStateCoordinator(new SnapshotStateTracker(), new PlaybackClock(), library);
            playback.Apply(CreateMessage(1, "track-a", 1_000));
            var sourceAsset = library.LoadForEditing(trackA.Record).Asset!;
            var originalLrcBytes = File.ReadAllBytes(sourceAsset.LyricsPath);
            using var viewModel = new BuiltInLyricsEditorViewModel(sourceAsset, library, playback);

            viewModel.TitleOverride = "Custom Song Title";
            viewModel.ArtistOverride = "Custom Artist";
            viewModel.SaveCommand.Execute(null);

            var savedA = library.LoadForEditing(trackA.Record).Asset!;
            Assert.Multiple(() =>
            {
                Assert.That(viewModel.TitleOverride, Is.EqualTo("Custom Song Title"));
                Assert.That(viewModel.ArtistOverride, Is.EqualTo("Custom Artist"));
                Assert.That(viewModel.EffectiveTitle, Is.EqualTo("Custom Song Title"));
                Assert.That(viewModel.EffectiveArtist, Is.EqualTo("Custom Artist"));
                Assert.That(viewModel.IsDirty, Is.False);
                Assert.That(savedA.Sidecar.UserMetadata,
                    Is.EqualTo(new UserTrackMetadata("Custom Song Title", "Custom Artist")));
                Assert.That(File.ReadAllBytes(savedA.LyricsPath), Is.EqualTo(originalLrcBytes),
                    "A metadata-only Save must not rewrite the LRC.");
                Assert.That(playback.CurrentLocalLyrics!.Record.UserMetadata, Is.EqualTo(savedA.Sidecar.UserMetadata));
                Assert.That(playback.CurrentLocalLyrics.EffectiveMetadata,
                    Is.EqualTo(new EffectiveTrackMetadata("Custom Song Title", "Custom Artist")));
            });

            playback.Apply(CreateMessage(2, "track-b", 2_000));
            var trackBEffectiveBefore = playback.CurrentLocalLyrics!.EffectiveMetadata;
            viewModel.TitleOverride = "Track A Updated While B Plays";
            viewModel.SaveCommand.Execute(null);
            var savedB = library.LoadForEditing(trackB.Record).Asset!;
            var reloadedA = library.LoadForEditing(trackA.Record).Asset!;

            Assert.Multiple(() =>
            {
                Assert.That(savedB.Sidecar.UserMetadata, Is.EqualTo(new UserTrackMetadata(null, null)));
                Assert.That(playback.CurrentLocalLyrics!.Record.LocalTrackId, Is.EqualTo(trackB.Record.LocalTrackId));
                Assert.That(playback.CurrentLocalLyrics.EffectiveMetadata, Is.EqualTo(trackBEffectiveBefore));
                Assert.That(reloadedA.Sidecar.UserMetadata,
                    Is.EqualTo(new UserTrackMetadata("Track A Updated While B Plays", "Custom Artist")));
                Assert.That(viewModel.IsDirty, Is.False);
            });

            playback.Apply(CreateMessage(3, "track-a", 3_000));
            using var reopened = new BuiltInLyricsEditorViewModel(reloadedA, library, playback);
            Assert.Multiple(() =>
            {
                Assert.That(reopened.TitleOverride, Is.EqualTo("Track A Updated While B Plays"));
                Assert.That(reopened.ArtistOverride, Is.EqualTo("Custom Artist"));
                Assert.That(reopened.IsDirty, Is.False);
                Assert.That(playback.CurrentLocalLyrics!.EffectiveMetadata,
                    Is.EqualTo(new EffectiveTrackMetadata("Track A Updated While B Plays", "Custom Artist")));
            });

            reopened.TitleOverride = "Unsaved Title";
            reopened.UndoCommand.Execute(null);
            Assert.Multiple(() =>
            {
                Assert.That(reopened.TitleOverride, Is.EqualTo("Track A Updated While B Plays"));
                Assert.That(reopened.IsDirty, Is.False, "Undo back to the saved baseline should be clean.");
            });
            reopened.RedoCommand.Execute(null);
            Assert.That(reopened.TitleOverride, Is.EqualTo("Unsaved Title"));
            Assert.That(reopened.IsDirty, Is.True);

            reopened.ClearTitleOverrideCommand.Execute(null);
            reopened.ClearArtistOverrideCommand.Execute(null);
            reopened.SaveCommand.Execute(null);
            var cleared = library.LoadForEditing(trackA.Record).Asset!;
            Assert.Multiple(() =>
            {
                Assert.That(cleared.Sidecar.UserMetadata, Is.EqualTo(new UserTrackMetadata(null, null)));
                Assert.That(reopened.EffectiveTitle, Is.EqualTo(reopened.SourceTitle));
                Assert.That(reopened.EffectiveArtist, Is.EqualTo(reopened.SourceArtist));
                Assert.That(reopened.IsDirty, Is.False);
            });
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Test]
    public void PendingOverridesSurviveTimestampProjectionRebuildDuringCombinedSave()
    {
        var root = CreateLibraryRoot();
        try
        {
            using var library = new LyricsLibrary(CreateLibraryPaths(root));
            Assert.That(library.Initialise().Completed, Is.True);
            var track = ImportTrack(library, "track-a");
            var initialAsset = library.LoadForEditing(track.Record).Asset!;
            File.WriteAllText(initialAsset.LyricsPath,
                "[ti:Original LRC Title]\r\n[ar:Original LRC Artist]\r\n[00:01.000]Line");
            var asset = library.LoadForEditing(track.Record).Asset!;
            var playback = new PlaybackStateCoordinator(new SnapshotStateTracker(), new PlaybackClock(), library);
            playback.Apply(CreateMessage(1, "track-a", 1_000));
            using var viewModel = new BuiltInLyricsEditorViewModel(asset, library, playback);
            var originalLrcBytes = File.ReadAllBytes(asset.LyricsPath);

            viewModel.TitleOverride = "Combined Title";
            viewModel.ArtistOverride = "Combined Artist";
            viewModel.Rows[0].Timestamps[1] = "00:02.000";
            viewModel.SaveCommand.Execute(null);

            var saved = library.LoadForEditing(track.Record).Asset!;
            Assert.Multiple(() =>
            {
                Assert.That(saved.Sidecar.UserMetadata,
                    Is.EqualTo(new UserTrackMetadata("Combined Title", "Combined Artist")));
                Assert.That(saved.LrcContent, Does.Contain("[00:02.000]"));
                Assert.That(saved.LrcContent, Does.Contain("[ti:Original LRC Title]\r\n[ar:Original LRC Artist]"));
                Assert.That(File.ReadAllBytes(saved.LyricsPath), Is.Not.EqualTo(originalLrcBytes));
                Assert.That(viewModel.TitleOverride, Is.EqualTo("Combined Title"));
                Assert.That(viewModel.ArtistOverride, Is.EqualTo("Combined Artist"));
                Assert.That(viewModel.IsDirty, Is.False);
            });
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Test]
    public void MetadataWriteFailureKeepsOverrideVisibleAndEditorDirty()
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("The sidecar sharing-violation check is Windows-specific.");
        var root = CreateLibraryRoot();
        try
        {
            using var library = new LyricsLibrary(CreateLibraryPaths(root));
            Assert.That(library.Initialise().Completed, Is.True);
            var track = ImportTrack(library, "track-a");
            var asset = library.LoadForEditing(track.Record).Asset!;
            var playback = new PlaybackStateCoordinator(new SnapshotStateTracker(), new PlaybackClock(), library);
            using var viewModel = new BuiltInLyricsEditorViewModel(asset, library, playback);
            viewModel.TitleOverride = "Keep Me";

            using (new FileStream(asset.SidecarPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                viewModel.SaveCommand.Execute(null);

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.TitleOverride, Is.EqualTo("Keep Me"));
                Assert.That(viewModel.IsDirty, Is.True);
                Assert.That(viewModel.Status, Is.Not.Empty);
                Assert.That(library.LoadForEditing(track.Record).Asset!.Sidecar.UserMetadata.Title, Is.Null);
            });
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Test]
    public void CombinedSaveReportsPartialFailureAndRetriesOnlyTheUnsavedMetadata()
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("The sidecar sharing-violation check is Windows-specific.");
        var root = CreateLibraryRoot();
        try
        {
            using var library = new LyricsLibrary(CreateLibraryPaths(root));
            Assert.That(library.Initialise().Completed, Is.True);
            var track = ImportTrack(library, "track-a");
            var asset = library.LoadForEditing(track.Record).Asset!;
            using var viewModel = new BuiltInLyricsEditorViewModel(asset, library,
                new PlaybackStateCoordinator(new SnapshotStateTracker(), new PlaybackClock(), library));
            viewModel.TitleOverride = "Pending title";
            viewModel.Rows[0].LyricsText = "Changed lyric";

            using (new FileStream(asset.SidecarPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                viewModel.SaveCommand.Execute(null);

            var partial = library.LoadForEditing(track.Record).Asset!;
            Assert.Multiple(() =>
            {
                Assert.That(viewModel.IsDirty, Is.True);
                Assert.That(viewModel.TitleOverride, Is.EqualTo("Pending title"));
                Assert.That(viewModel.Status, Does.Contain("portable metadata"));
                Assert.That(partial.LrcContent, Does.Contain("Changed lyric"));
                Assert.That(partial.Sidecar.UserMetadata.Title, Is.Null);
            });

            var lrcAfterPartialWrite = File.ReadAllBytes(partial.LyricsPath);
            viewModel.SaveCommand.Execute(null);
            var completed = library.LoadForEditing(track.Record).Asset!;
            Assert.Multiple(() =>
            {
                Assert.That(completed.Sidecar.UserMetadata.Title, Is.EqualTo("Pending title"));
                Assert.That(completed.LrcContent, Does.Contain("Changed lyric"));
                Assert.That(File.ReadAllBytes(completed.LyricsPath), Is.EqualTo(lrcAfterPartialWrite),
                    "Retry should write the unsaved sidecar only, not rewrite the completed LRC.");
                Assert.That(viewModel.IsDirty, Is.False);
            });
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Test]
    public void PlaybackUpdatesDoNotChangeEditorDocumentSelectionOrDirtyState()
    {
        using var fixture = CreateViewModel("Line A\nLine B\n");
        var viewModel = fixture.ViewModel;
        var playback = fixture.Playback;
        var selectedRowId = viewModel.Rows[1].EditorLineId;
        viewModel.SelectCell(selectedRowId, EditorColumn.Lyrics);
        viewModel.Rows[0].LyricsText = "Edited locally";
        var dirtyBeforePlayback = viewModel.IsDirty;
        var documentBeforePlayback = viewModel.Rows.Select(row => (row.EditorLineId, row.LyricsText)).ToArray();

        playback.Apply(CreateMessage(1, "track-a", 1_000));
        viewModel.RefreshPlayback();
        playback.Apply(CreateMessage(2, "track-b", 2_000));
        viewModel.RefreshPlayback();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Selection.SelectedRowId, Is.EqualTo(selectedRowId));
            Assert.That(viewModel.Rows.Select(row => (row.EditorLineId, row.LyricsText)), Is.EqualTo(documentBeforePlayback));
            Assert.That(viewModel.IsDirty, Is.EqualTo(dirtyBeforePlayback));
        });
    }

    private static EditorFixture CreateViewModel(string content)
    {
        var metadata = new UserTrackMetadata(null, null);
        var association = new SourceTrackAssociation("youtubeMusic", "track-a",
            new SourceTrackMetadata("A", "Artist", null, 60_000));
        var record = new LocalTrackRecord("local-a", metadata, "tracks/local-a/lyrics.lrc",
            "tracks/local-a/track.lyrics.json", null, null, [association]);
        var sidecar = new LyricsSidecar(1, "local-a", [association], metadata,
            new LyricsAsset("lyrics.lrc", null, null, DateTimeOffset.UtcNow));
        var asset = new EditorAssetSnapshot(record, sidecar, "", "", content, "lrc-hash", "sidecar-hash", false);
        var library = new LyricsLibrary(new LibraryPaths("", "", "", "", false));
        var playback = new PlaybackStateCoordinator(new SnapshotStateTracker(), new PlaybackClock());
        var viewModel = new BuiltInLyricsEditorViewModel(asset, library, playback);
        return new(viewModel, library, playback);
    }

    private static void AssertRows(BuiltInLyricsEditorViewModel viewModel, params string[] expected) =>
        Assert.That(viewModel.Rows.Select(row => row.LyricsText), Is.EqualTo(expected));

    private static void AssertRowCommandState(BuiltInLyricsEditorViewModel viewModel,
        bool insertAbove, bool insertBelow, bool delete, bool append)
    {
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.InsertRowAboveCommand.CanExecute(null), Is.EqualTo(insertAbove));
            Assert.That(viewModel.InsertRowBelowCommand.CanExecute(null), Is.EqualTo(insertBelow));
            Assert.That(viewModel.DeleteRowCommand.CanExecute(null), Is.EqualTo(delete));
            Assert.That(viewModel.AppendRowCommand.CanExecute(null), Is.EqualTo(append));
        });
    }

    private static PlaybackSnapshotMessage CreateMessage(long sequence, string trackId, long positionMs)
    {
        var metadata = new EnvelopeMetadata(1, "playbackSnapshot", "youtubeMusic",
            "550e8400-e29b-41d4-a716-446655440000", sequence, DateTimeOffset.UtcNow);
        var payload = new PlaybackSnapshotPayload(new TrackInfo(trackId, "Title", "Artist", null, 60_000),
            new PlaybackState(positionMs, true, 1), new LyricsInfo(false, false, null, []));
        return new(metadata, payload, "{}");
    }

    private static string CreateLibraryRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "LyricsDisplayerAppTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static LibraryPaths CreateLibraryPaths(string root) => new(root,
        Path.Combine(root, "settings.json"), Path.Combine(root, "Lyrics"),
        Path.Combine(root, "library-index.db"), false);

    private static LocalLyricsDocument ImportTrack(LyricsLibrary library, string sourceTrackId)
    {
        var track = new TrackInfo(sourceTrackId, "Title", "Artist", null, 60_000);
        var lyrics = new LyricsSnapshotPayload(sourceTrackId, true, true, "youtubeMusic",
            [new LyricsLine(1_000, 60_000, "Line")], "Test provider");
        var imported = library.Import(track, lyrics);
        Assert.That(imported.Document, Is.Not.Null, imported.Error);
        return imported.Document!;
    }

    private sealed class EditorFixture(BuiltInLyricsEditorViewModel viewModel, LyricsLibrary library,
        PlaybackStateCoordinator playback) : IDisposable
    {
        public BuiltInLyricsEditorViewModel ViewModel { get; } = viewModel;
        public LyricsLibrary Library { get; } = library;
        public PlaybackStateCoordinator Playback { get; } = playback;

        public void Dispose()
        {
            ViewModel.Dispose();
            Library.Dispose();
        }
    }
}
