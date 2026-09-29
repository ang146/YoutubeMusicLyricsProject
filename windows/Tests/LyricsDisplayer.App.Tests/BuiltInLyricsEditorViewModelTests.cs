using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Core.Playback;
using LyricsDisplayer.Core.Protocol;

namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class BuiltInLyricsEditorViewModelTests
{
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
