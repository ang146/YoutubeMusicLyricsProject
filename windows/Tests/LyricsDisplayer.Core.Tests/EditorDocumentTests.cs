using LyricsDisplayer.Core.Library;

namespace LyricsDisplayer.Core.Tests;

[TestFixture]
public sealed class EditorDocumentTests
{
    [Test]
    public void LoadAndSaveWithoutEditsPreservesPhysicalLrcStructureExactly()
    {
        const string source = "[ar:Artist]\r\n[x-custom:keep]\r\n\r\n[01:00.000][02:30.00]副歌\r\nUntimed ♪\r\n[0055.000]Malformed\r\n";
        var document = EditorDocumentCodec.Load(source, "source-hash");

        Assert.Multiple(() =>
        {
            Assert.That(EditorDocumentCodec.Serialize(document), Is.EqualTo(source));
            Assert.That(document.Rows, Has.Count.EqualTo(2));
            Assert.That(document.Rows[0].Timestamps.Select(item => item.Value), Is.EqualTo(new[] { "01:00.000", "02:30.00" }));
            Assert.That(document.Rows[0].LyricsText, Is.EqualTo("副歌"));
            Assert.That(document.Rows[1].Timestamps, Is.Empty);
            Assert.That(document.Rows[1].LyricsText, Is.EqualTo("Untimed ♪"));
            Assert.That(document.PhysicalLines.Select(line => line.Kind),
                Does.Contain(EditorEntryKind.Metadata).And.Contain(EditorEntryKind.UnknownTag)
                    .And.Contain(EditorEntryKind.Blank).And.Contain(EditorEntryKind.Unsupported));
            Assert.That(document.Validate(), Has.Some.Matches<EditorValidationDiagnostic>(item => item.Message.Contains("malformed")));
        });
    }

    [Test]
    public void RevertingAnEditedRowToItsPhysicalSourceRestoresCleanStateAndExactFormatting()
    {
        const string source = "  [00:10.000]Original\r\n";
        var buffer = new EditorDocumentBuffer(EditorDocumentCodec.Load(source), UserTrackMetadata.Normalise(null, null));
        var row = buffer.Document.Rows.Single();

        buffer.SetLyrics(row.Id, "Changed");
        Assert.That(buffer.IsDirty, Is.True);
        buffer.SetLyrics(row.Id, "Original");

        Assert.That(buffer.IsDirty, Is.False);
        Assert.That(EditorDocumentCodec.Serialize(buffer.Document), Is.EqualTo(source));
    }

    [Test]
    public void InsertDeleteUndoRedoKeepStableRowIdentityAndUntimedNewRows()
    {
        var buffer = new EditorDocumentBuffer(EditorDocumentCodec.Load("A\nB\nC\n"), UserTrackMetadata.Normalise(null, null));
        var originalIds = buffer.Document.Rows.Select(row => row.Id).ToArray();

        var inserted = buffer.InsertBelow(originalIds[0])!.Value;
        Assert.Multiple(() =>
        {
            Assert.That(buffer.Document.Rows.Select(row => row.Id), Is.EqualTo(new[] { originalIds[0], inserted, originalIds[1], originalIds[2] }));
            Assert.That(buffer.Document.Rows[1].LyricsText, Is.Empty);
            Assert.That(buffer.Document.Rows[1].Timestamps, Is.Empty);
        });

        Assert.That(buffer.Delete(inserted), Is.True);
        buffer.Undo();
        Assert.That(buffer.Document.Rows.Select(row => row.Id), Is.EqualTo(new[] { originalIds[0], inserted, originalIds[1], originalIds[2] }));
        buffer.Undo();
        Assert.That(buffer.Document.Rows.Select(row => row.Id), Is.EqualTo(originalIds));
        Assert.That(buffer.IsDirty, Is.False);
        buffer.Redo();
        Assert.That(buffer.Document.Rows.Select(row => row.Id), Does.Contain(inserted));
        buffer.Redo();
        Assert.That(buffer.Document.Rows.Select(row => row.Id), Does.Not.Contain(inserted));
    }

    [Test]
    public void AppendPlacesNewRowBeforeTrailingOpaqueLinesWithoutReordering()
    {
        var source = "[ti:Title]\nA\n[x-end:preserve]";
        var buffer = new EditorDocumentBuffer(EditorDocumentCodec.Load(source), UserTrackMetadata.Normalise(null, null));

        var id = buffer.Append();
        Assert.That(EditorDocumentCodec.Serialize(buffer.Document), Is.EqualTo("[ti:Title]\nA\n\n[x-end:preserve]"));
        Assert.That(buffer.Document.Rows.Last().Id, Is.EqualTo(id));
        Assert.That(buffer.Document.Rows.Last().Timestamps, Is.Empty);
    }

    [Test]
    public void TimestampValidationIsAdvisoryAndDocumentOrderIsNotSorted()
    {
        var buffer = new EditorDocumentBuffer(EditorDocumentCodec.Load("[00:10.000]A\n[00:30.000]B\n"),
            UserTrackMetadata.Normalise(null, null));
        var first = buffer.Document.Rows[0].Id;
        var second = buffer.Document.Rows[1].Id;

        Assert.That(buffer.SetTimestamp(second, 0, "00:20.000"), Is.True);
        Assert.That(buffer.SetTimestamp(first, 0, "00:xx.000"), Is.True);
        Assert.That(buffer.Document.Validate().Any(item => item.Message.Contains("invalid")), Is.True);
        Assert.That(EditorDocumentCodec.Serialize(buffer.Document), Is.EqualTo("[00:xx.000]A\n[00:20.000]B\n"));
        Assert.That(buffer.Document.Rows.Select(row => row.Id), Is.EqualTo(new[] { first, second }));
    }

    [Test]
    public void ClearingTimestampOccurrencesDoesNotDuplicateOrReorderRemainingOccurrences()
    {
        var buffer = new EditorDocumentBuffer(EditorDocumentCodec.Load("[00:10.000][00:20.000][00:30.000]A\n"),
            UserTrackMetadata.Normalise(null, null));
        var row = buffer.Document.Rows.Single();
        var originalIds = row.Timestamps.Select(item => item.Id).ToArray();

        Assert.That(buffer.SetTimestamps(row.Id, ["", "00:20.000", "00:30.000", ""]), Is.True);
        Assert.That(buffer.Document.Rows.Single().Timestamps.Select(item => item.Value),
            Is.EqualTo(new[] { "00:20.000", "00:30.000" }));
        Assert.That(buffer.Document.Rows.Single().Timestamps.Select(item => item.Id),
            Is.EqualTo(new[] { originalIds[1], originalIds[2] }));
        buffer.Undo();
        Assert.That(buffer.Document.Rows.Single().Timestamps.Select(item => item.Value),
            Is.EqualTo(new[] { "00:10.000", "00:20.000", "00:30.000" }));
    }

    [Test]
    public void SetCurrentTimeCreatesOrReplacesOnlyExplicitTimestampUsingGlobalOffset()
    {
        var buffer = new EditorDocumentBuffer(EditorDocumentCodec.Load("[00:10.000][00:30.000]A\nUntimed B\n"),
            UserTrackMetadata.Normalise(null, null));
        var timedRow = buffer.Document.Rows[0];
        var untimedRow = buffer.Document.Rows[1];

        Assert.That(buffer.SetTimestampFromPlayback(new(timedRow.Id, EditorColumn.Lyrics), true, 40_000, 500), Is.False,
            "An existing multi-timestamp row requires an explicitly selected timestamp occurrence.");
        Assert.That(buffer.SetTimestampFromPlayback(new(timedRow.Id, EditorColumn.Timestamp, 1), true, 40_000, 500), Is.True);
        Assert.That(buffer.SetTimestampFromPlayback(new(untimedRow.Id, EditorColumn.Lyrics), true, 12_345, 500), Is.True);

        Assert.That(EditorDocumentCodec.Serialize(buffer.Document),
            Is.EqualTo("[00:10.000][00:39.500]A\n[00:11.845]Untimed B\n"));
        Assert.That(buffer.SetTimestampFromPlayback(new(untimedRow.Id, EditorColumn.Timestamp, 0), false, 20_000, 0), Is.False);
    }

    [Test]
    public void UndoRedoAndMetadataOverridesFollowSavedBaseline()
    {
        var buffer = new EditorDocumentBuffer(EditorDocumentCodec.Load("A\n"), UserTrackMetadata.Normalise("Title", null));
        var row = buffer.Document.Rows.Single();
        buffer.SetLyrics(row.Id, "Changed");
        buffer.SetArtistOverride("Artist");
        Assert.That(buffer.IsDirty, Is.True);
        buffer.Undo();
        Assert.That(buffer.Metadata.Artist, Is.Null);
        buffer.Undo();
        Assert.That(buffer.IsDirty, Is.False);
        buffer.Redo();
        buffer.SetTitleOverride("  ");
        Assert.That(buffer.Metadata.Title, Is.Null);
        var content = EditorDocumentCodec.Serialize(buffer.Document);
        buffer.MarkSaved(content, "new-hash");
        Assert.That(buffer.IsDirty, Is.False);
        buffer.Undo();
        Assert.That(buffer.CanUndo, Is.False, "A successful save establishes a fresh undo baseline.");
    }
}
