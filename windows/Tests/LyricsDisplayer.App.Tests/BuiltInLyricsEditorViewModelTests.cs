using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Core.Playback;
using LyricsDisplayer.Core.Protocol;

namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class BuiltInLyricsEditorViewModelTests
{
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
