using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Core.Playback;
using LyricsDisplayer.Core.Protocol;

namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class BuiltInLyricsEditorViewModelTests
{
    [Test]
    public void PlaybackUpdatesDoNotChangeEditorDocumentSelectionOrDirtyState()
    {
        var metadata = new UserTrackMetadata(null, null);
        var association = new SourceTrackAssociation("youtubeMusic", "track-a",
            new SourceTrackMetadata("A", "Artist", null, 60_000));
        var record = new LocalTrackRecord("local-a", metadata, "tracks/local-a/lyrics.lrc",
            "tracks/local-a/track.lyrics.json", null, null, [association]);
        var sidecar = new LyricsSidecar(1, "local-a", [association], metadata,
            new LyricsAsset("lyrics.lrc", null, null, DateTimeOffset.UtcNow));
        var asset = new EditorAssetSnapshot(record, sidecar, "", "", "Line A\nLine B\n", "lrc-hash", "sidecar-hash", false);
        using var library = new LyricsLibrary(new LibraryPaths("", "", "", "", false));
        var playback = new PlaybackStateCoordinator(new SnapshotStateTracker(), new PlaybackClock());
        using var viewModel = new BuiltInLyricsEditorViewModel(asset, library, playback);
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

    private static PlaybackSnapshotMessage CreateMessage(long sequence, string trackId, long positionMs)
    {
        var metadata = new EnvelopeMetadata(1, "playbackSnapshot", "youtubeMusic",
            "550e8400-e29b-41d4-a716-446655440000", sequence, DateTimeOffset.UtcNow);
        var payload = new PlaybackSnapshotPayload(new TrackInfo(trackId, "Title", "Artist", null, 60_000),
            new PlaybackState(positionMs, true, 1), new LyricsInfo(false, false, null, []));
        return new(metadata, payload, "{}");
    }
}
