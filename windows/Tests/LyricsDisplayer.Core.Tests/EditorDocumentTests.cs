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
            Assert.That(document.Rows, Has.Count.EqualTo(5));
            Assert.That(document.Rows.Select(row => row.LyricsText), Is.EqualTo(new[]
                { "[x-custom:keep]", "", "副歌", "Untimed ♪", "[0055.000]Malformed" }));
            Assert.That(document.Rows[2].Timestamps.Select(item => item.Value), Is.EqualTo(new[] { "01:00.000", "02:30.00" }));
            Assert.That(document.Rows[0].Timestamps, Is.Empty);
            Assert.That(document.Rows[1].Timestamps, Is.Empty);
            Assert.That(document.Rows[4].Timestamps, Is.Empty);
            Assert.That(document.PhysicalLines.Select(line => line.Kind),
                Does.Contain(EditorEntryKind.Metadata).And.Contain(EditorEntryKind.Lyric));
            Assert.That(document.Validate(), Has.Some.Matches<EditorValidationDiagnostic>(item =>
                item.Severity == EditorValidationSeverity.Error && item.Message.Contains("malformed", StringComparison.OrdinalIgnoreCase)));
            Assert.That(document.Validate(), Has.Some.Matches<EditorValidationDiagnostic>(item =>
                item.Severity == EditorValidationSeverity.Warning && item.Message.Contains("unrecognised tag", StringComparison.OrdinalIgnoreCase)));
        });
    }

    [Test]
    public void ValidationDiagnosticsRetainPhysicalSourceLinesAndExplicitCellOwnership()
    {
        var document = EditorDocumentCodec.Load(
            "[ti:Song]\n[00:30.000]First\n[x-custom:keep]\n\n[00:20.000]Second\n[00:xx.000]Broken");
        var diagnostics = document.Validate();

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Count(item => item.Severity == EditorValidationSeverity.Warning), Is.EqualTo(2));
            Assert.That(diagnostics.Count(item => item.Severity == EditorValidationSeverity.Error), Is.EqualTo(1));
            Assert.That(diagnostics.Select(item => item.PhysicalLine), Is.EqualTo(new int?[] { 3, 5, 6 }));
            Assert.That(diagnostics.Select(item => item.TargetColumn), Is.EqualTo(new EditorColumn?[]
                { EditorColumn.Lyrics, EditorColumn.Timestamp, EditorColumn.Lyrics }));
            Assert.That(document.Rows.Select(row => row.LyricsText),
                Is.EqualTo(new[] { "First", "[x-custom:keep]", "", "Second", "[00:xx.000]Broken" }));
            Assert.That(diagnostics.All(item => item.Message.Contains("Physical line", StringComparison.OrdinalIgnoreCase)),
                Is.False, "The live physical line number is carried separately so it cannot become stale.");
        });
    }

    [Test]
    public void UntimedTextDiagnosticsFollowPhysicalEditsAndDisappearWhenTheTextIsFixed()
    {
        var buffer = new EditorDocumentBuffer(EditorDocumentCodec.Load("First\n[00:xx.000]Broken"),
            UserTrackMetadata.Normalise(null, null));
        var firstRowId = buffer.Document.Rows[0].Id;
        var malformedRowId = buffer.Document.Rows[1].Id;

        Assert.That(buffer.Document.Validate().Single().PhysicalLine, Is.EqualTo(2));
        buffer.InsertAbove(firstRowId);
        Assert.That(buffer.Document.Validate().Single().PhysicalLine, Is.EqualTo(3));

        buffer.SetLyrics(malformedRowId, "Fixed lyric");

        Assert.That(buffer.Document.Validate(), Is.Empty);
    }

    [TestCase("A\n\nB", new[] { "A", "", "B" })]
    [TestCase("A\n\n\nB", new[] { "A", "", "", "B" })]
    [TestCase("\n\n[00:10.000]A", new[] { "", "", "A" })]
    [TestCase("[00:10.000]A\n\n", new[] { "A", "" })]
    [TestCase("A\n \t\nB", new[] { "A", " \t", "B" })]
    public void EveryNonMetadataPhysicalLineProjectsAndRoundTripsInOrder(string source, string[] expectedRows)
    {
        var document = EditorDocumentCodec.Load(source);

        Assert.Multiple(() =>
        {
            Assert.That(document.Rows.Select(row => row.LyricsText), Is.EqualTo(expectedRows));
            Assert.That(document.Rows.Select(row => row.Id).Distinct().Count(), Is.EqualTo(expectedRows.Length));
            Assert.That(EditorDocumentCodec.Serialize(document), Is.EqualTo(source));
            Assert.That(EditorDocumentCodec.Serialize(EditorDocumentCodec.Load(EditorDocumentCodec.Serialize(document))), Is.EqualTo(source));
        });
    }

    [Test]
    public void RecognizedMetadataStaysHiddenWhileBlankUnknownAndMalformedLinesRemainEditableRows()
    {
        const string source = "[ti:Song]\n[ar:Artist]\n\n[00:10.000]A\n[something:whatever]\n[00:xx.000]Broken\nB";
        var document = EditorDocumentCodec.Load(source);

        Assert.Multiple(() =>
        {
            Assert.That(document.Rows.Select(row => row.LyricsText), Is.EqualTo(new[]
                { "", "A", "[something:whatever]", "[00:xx.000]Broken", "B" }));
            Assert.That(document.PhysicalLines.Count(line => line.Kind == EditorEntryKind.Metadata), Is.EqualTo(2));
            Assert.That(document.Rows.All(row => row.Id != Guid.Empty), Is.True);
            Assert.That(EditorDocumentCodec.Serialize(document), Is.EqualTo(source));
        });
    }

    [Test]
    public void BlankRowsRemainOrderedAroundUnicodeLyricsAndDoNotMakeLoadedDocumentDirty()
    {
        const string source = "\n繁體中文\n\n简体中文\n日本語\n\n한국어 😀\n[01:00.000][02:30.000]🎵";
        var document = EditorDocumentCodec.Load(source);
        var buffer = new EditorDocumentBuffer(document, UserTrackMetadata.Normalise(null, null));

        Assert.Multiple(() =>
        {
            Assert.That(buffer.IsDirty, Is.False);
            Assert.That(document.Rows.Select(row => row.LyricsText), Is.EqualTo(new[]
                { "", "繁體中文", "", "简体中文", "日本語", "", "한국어 😀", "🎵" }));
            Assert.That(document.Rows[^1].Timestamps.Select(timestamp => timestamp.Value),
                Is.EqualTo(new[] { "01:00.000", "02:30.000" }));
            Assert.That(EditorDocumentCodec.Serialize(EditorDocumentCodec.Load(EditorDocumentCodec.Serialize(document))), Is.EqualTo(source));
        });
    }

    [Test]
    public void EmptyRowInsertDeleteEditAndUndoRedoRoundTripWithoutSyntheticTiming()
    {
        var buffer = new EditorDocumentBuffer(EditorDocumentCodec.Load("A\nB"), UserTrackMetadata.Normalise(null, null));
        var rowA = buffer.Document.Rows[0].Id;
        var insertedId = buffer.InsertBelow(rowA)!.Value;
        Assert.That(buffer.Document.Rows.Select(row => row.LyricsText), Is.EqualTo(new[] { "A", "", "B" }));
        Assert.That(buffer.Document.Rows[1].Timestamps, Is.Empty);
        Assert.That(EditorDocumentCodec.Load(EditorDocumentCodec.Serialize(buffer.Document)).Rows.Select(row => row.LyricsText),
            Is.EqualTo(new[] { "A", "", "B" }));

        buffer.Undo();
        Assert.That(buffer.Document.Rows.Select(row => row.LyricsText), Is.EqualTo(new[] { "A", "B" }));
        buffer.Redo();
        Assert.That(buffer.Document.Rows[1].Id, Is.EqualTo(insertedId));
        buffer.SetLyrics(insertedId, "New untimed lyric");
        Assert.That(EditorDocumentCodec.Serialize(buffer.Document), Is.EqualTo("A\nNew untimed lyric\nB"));
        Assert.That(EditorDocumentCodec.Load(EditorDocumentCodec.Serialize(buffer.Document)).Rows.Select(row => row.LyricsText),
            Is.EqualTo(new[] { "A", "New untimed lyric", "B" }));

        var secondEmpty = buffer.InsertBelow(insertedId)!.Value;
        var lastEmpty = buffer.InsertBelow(secondEmpty)!.Value;
        Assert.That(buffer.Delete(secondEmpty), Is.True);
        Assert.That(EditorDocumentCodec.Load(EditorDocumentCodec.Serialize(buffer.Document)).Rows.Select(row => row.LyricsText),
            Is.EqualTo(new[] { "A", "New untimed lyric", "", "B" }));
        buffer.Undo();
        Assert.That(buffer.Document.Rows.Select(row => row.Id), Does.Contain(secondEmpty));
        Assert.That(buffer.Document.Rows.Select(row => row.Id), Does.Contain(lastEmpty));
        buffer.Redo();
        Assert.That(buffer.Document.Rows.Select(row => row.Id), Does.Not.Contain(secondEmpty));
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
    public void AppendAddsVisibleUntimedRowAfterExistingUnknownTextWithoutReordering()
    {
        var source = "[ti:Title]\nA\n[x-end:preserve]";
        var buffer = new EditorDocumentBuffer(EditorDocumentCodec.Load(source), UserTrackMetadata.Normalise(null, null));

        var id = buffer.Append();
        Assert.That(EditorDocumentCodec.Serialize(buffer.Document), Is.EqualTo("[ti:Title]\nA\n[x-end:preserve]\n\n"));
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
    public void MultiTimestampChronologyIsValidatedIndependentlyForEachOccurrenceLane()
    {
        var validInterleavedLanes = EditorDocumentCodec.Load(
            "[00:10.000][00:50.000]A\n[00:20.000][01:00.000]B");
        var firstLaneRegression = EditorDocumentCodec.Load(
            "[00:10.000][00:50.000]A\n[00:05.000][01:00.000]B");
        var secondLaneRegression = EditorDocumentCodec.Load(
            "[00:10.000][00:50.000]A\n[00:20.000][00:40.000]B");

        Assert.Multiple(() =>
        {
            Assert.That(validInterleavedLanes.Validate(), Is.Empty,
                "A later timestamp in one lane may be greater than a timestamp in another lane.");
        });
        AssertLaneWarning(firstLaneRegression, rowIndex: 1, timestampIndex: 0);
        AssertLaneWarning(secondLaneRegression, rowIndex: 1, timestampIndex: 1);
    }

    [Test]
    public void SparseTimestampLanesSkipMissingCellsAndRetainPriorPopulatedValues()
    {
        var document = CreateSparseDocument(
            ["00:30.000", "00:40.000", "00:45.000"],
            [],
            ["", "", ""],
            ["00:20.000", "00:30.000", "00:50.000"]);

        var diagnostics = document.Validate();

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics, Has.Count.EqualTo(2));
            Assert.That(diagnostics.All(item => item.Severity == EditorValidationSeverity.Warning), Is.True);
            Assert.That(diagnostics.All(item => item.RowId == document.Rows[3].Id), Is.True);
            Assert.That(diagnostics.Select(item => item.TimestampIndex), Is.EqualTo(new int?[] { 0, 1 }));
            Assert.That(diagnostics.All(item => item.TargetColumn == EditorColumn.Timestamp), Is.True);
            Assert.That(diagnostics.All(item => item.Message.Contains("same occurrence lane")), Is.True);
        });
    }

    [Test]
    public void WithinRowOrderingIsWarnedIndependentlyAndEqualTimestampsAreAllowed()
    {
        var withinRowRegression = EditorDocumentCodec.Load("[00:20.000][00:10.000]A");
        var sparseWithinRowRegression = CreateSparseDocument(["00:20.000", "", "00:10.000"]);
        var equalTimestamps = EditorDocumentCodec.Load(
            "[00:10.000][00:10.000]A\n[00:10.000][00:10.000]B");
        var threeValidLanes = EditorDocumentCodec.Load(
            "[00:10.000][00:20.000][00:30.000]A\n[00:15.000][00:25.000][00:35.000]B");
        var thirdLaneRegression = EditorDocumentCodec.Load(
            "[00:10.000][00:20.000][00:30.000]A\n[00:15.000][00:25.000][00:29.000]B");

        Assert.Multiple(() =>
        {
            var rowDiagnostic = withinRowRegression.Validate().Single();
            Assert.That(rowDiagnostic.TimestampIndex, Is.EqualTo(1));
            Assert.That(rowDiagnostic.Message, Does.Contain("same lyric row"));
            var sparseRowDiagnostic = sparseWithinRowRegression.Validate().Single();
            Assert.That(sparseRowDiagnostic.TimestampIndex, Is.EqualTo(2),
                "A missing occurrence does not reset the preceding populated timestamp in the row.");
            Assert.That(sparseRowDiagnostic.Message, Does.Contain("same lyric row"));
            Assert.That(equalTimestamps.Validate(), Is.Empty,
                "Equality is allowed within a row and between rows in the same lane.");
            Assert.That(threeValidLanes.Validate(), Is.Empty);
        });
        AssertLaneWarning(thirdLaneRegression, rowIndex: 1, timestampIndex: 2);
    }

    private static void AssertLaneWarning(EditorDocument document, int rowIndex, int timestampIndex)
    {
        var diagnostic = document.Validate().Single();
        Assert.Multiple(() =>
        {
            Assert.That(diagnostic.RowId, Is.EqualTo(document.Rows[rowIndex].Id));
            Assert.That(diagnostic.TimestampIndex, Is.EqualTo(timestampIndex));
            Assert.That(diagnostic.TargetColumn, Is.EqualTo(EditorColumn.Timestamp));
            Assert.That(diagnostic.Message, Does.Contain("same occurrence lane"));
        });
    }

    private static EditorDocument CreateSparseDocument(params string[][] timestampRows)
    {
        var lines = timestampRows.Select((values, index) =>
        {
            var row = new EditorLyricRow(Guid.NewGuid(), $"Row {index + 1}",
                values.Select(value => new EditorTimestamp(Guid.NewGuid(), value)).ToArray());
            return new EditorPhysicalLine(Guid.NewGuid(), EditorEntryKind.Lyric, string.Empty, "\n", row);
        }).ToArray();
        return new EditorDocument(lines, string.Empty, null, "\n");
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
    public void SetCurrentTimeAppendsWithoutOverwritingAndUsesGlobalOffset()
    {
        var buffer = new EditorDocumentBuffer(EditorDocumentCodec.Load("[00:10.000][00:30.000]A\nUntimed B\n"),
            UserTrackMetadata.Normalise(null, null));
        var timedRow = buffer.Document.Rows[0];
        var untimedRow = buffer.Document.Rows[1];

        Assert.That(buffer.SetTimestampFromPlayback(new(timedRow.Id, EditorColumn.Timestamp, 0), true, 40_000, 500), Is.True,
            "Playback time appends even when an existing timestamp cell is selected.");
        Assert.That(buffer.SetTimestampFromPlayback(new(untimedRow.Id, EditorColumn.Lyrics), true, 12_345, 500), Is.True);

        Assert.That(EditorDocumentCodec.Serialize(buffer.Document),
            Is.EqualTo("[00:10.000][00:30.000][00:39.500]A\n[00:11.845]Untimed B\n"));
        Assert.That(buffer.SetTimestampFromPlayback(new(untimedRow.Id, EditorColumn.Timestamp, 0), false, 20_000, 0), Is.False);
    }

    [Test]
    public void SetCurrentTimeAppendsInOrderUpToFiveAndLeavesExistingOverLimitDataUntouched()
    {
        const string fourTimestamps = "[00:10.000][00:20.000][00:30.000][00:40.000]Line\n";
        var buffer = new EditorDocumentBuffer(EditorDocumentCodec.Load(fourTimestamps),
            UserTrackMetadata.Normalise(null, null));
        var row = buffer.Document.Rows.Single();

        Assert.That(buffer.SetTimestampFromPlayback(new(row.Id, EditorColumn.Lyrics), true, 50_000, 0), Is.True);
        Assert.That(buffer.Document.Rows.Single().Timestamps.Select(item => item.Value),
            Is.EqualTo(new[] { "00:10.000", "00:20.000", "00:30.000", "00:40.000", "00:50.000" }));
        Assert.That(buffer.Document.Validate().Any(item => item.Severity == EditorValidationSeverity.Warning), Is.False);

        var fiveTimestampContent = EditorDocumentCodec.Serialize(buffer.Document);
        Assert.That(buffer.SetTimestampFromPlayback(new(row.Id, EditorColumn.Lyrics), true, 55_000, 0), Is.False);
        Assert.That(EditorDocumentCodec.Serialize(buffer.Document), Is.EqualTo(fiveTimestampContent));

        var legacySixTimestamp = "[00:10.000][00:20.000][00:30.000][00:40.000][00:50.000][01:00.000]Legacy\n";
        var legacy = new EditorDocumentBuffer(EditorDocumentCodec.Load(legacySixTimestamp),
            UserTrackMetadata.Normalise(null, null));
        var legacyRow = legacy.Document.Rows.Single();
        Assert.That(legacy.SetTimestampFromPlayback(new(legacyRow.Id, EditorColumn.Lyrics), true, 65_000, 0), Is.False);
        Assert.That(EditorDocumentCodec.Serialize(legacy.Document), Is.EqualTo(legacySixTimestamp),
            "Existing source data beyond the UI command limit remains lossless.");
    }

    [Test]
    public void RepeatedCurrentTimeAppendsUndoAndRedoOneOccurrenceAtATimeAndRoundTrips()
    {
        var buffer = new EditorDocumentBuffer(EditorDocumentCodec.Load("Line\n"),
            UserTrackMetadata.Normalise(null, null));
        var row = buffer.Document.Rows.Single();
        var selection = new EditorSelection(row.Id, EditorColumn.Lyrics);

        Assert.That(buffer.SetTimestampFromPlayback(selection, true, 10_000, 0), Is.True);
        Assert.That(buffer.SetTimestampFromPlayback(selection, true, 22_500, 0), Is.True);
        Assert.That(buffer.SetTimestampFromPlayback(selection, true, 31_900, 0), Is.True);
        Assert.That(EditorDocumentCodec.Serialize(buffer.Document), Is.EqualTo(
            "[00:10.000][00:22.500][00:31.900]Line\n"));

        buffer.Undo();
        Assert.That(EditorDocumentCodec.Serialize(buffer.Document), Is.EqualTo("[00:10.000][00:22.500]Line\n"));
        buffer.Undo();
        Assert.That(EditorDocumentCodec.Serialize(buffer.Document), Is.EqualTo("[00:10.000]Line\n"));
        buffer.Redo();
        buffer.Redo();
        Assert.That(EditorDocumentCodec.Serialize(buffer.Document), Is.EqualTo(
            "[00:10.000][00:22.500][00:31.900]Line\n"));

        var roundTripped = EditorDocumentCodec.Load(EditorDocumentCodec.Serialize(buffer.Document));
        Assert.That(roundTripped.Rows.Single().LyricsText, Is.EqualTo("Line"));
        Assert.That(roundTripped.Rows.Single().Timestamps.Select(item => item.Value),
            Is.EqualTo(new[] { "00:10.000", "00:22.500", "00:31.900" }));
    }

    [Test]
    public void SetCurrentTimeFillsAnExistingSparseSlotWithoutMovingLaterOccurrences()
    {
        var first = new EditorTimestamp(Guid.NewGuid(), "00:10.000");
        var gap = new EditorTimestamp(Guid.NewGuid(), string.Empty);
        var later = new EditorTimestamp(Guid.NewGuid(), "00:30.000");
        var row = new EditorLyricRow(Guid.NewGuid(), "Line", [first, gap, later]);
        var line = new EditorPhysicalLine(Guid.NewGuid(), EditorEntryKind.Lyric,
            "[00:10.000][][00:30.000]Line", "\n", row, IsModified: true);
        var document = new EditorDocument([line], line.RawText + line.LineEnding, null, "\n");
        var buffer = new EditorDocumentBuffer(document, UserTrackMetadata.Normalise(null, null));

        Assert.That(buffer.SetTimestampFromPlayback(
            new(row.Id, EditorColumn.Lyrics), true, 20_000, 0, nextAvailableIndex: 1), Is.True);
        var updated = buffer.Document.Rows.Single();
        Assert.That(updated.Timestamps.Select(item => item.Value),
            Is.EqualTo(new[] { "00:10.000", "00:20.000", "00:30.000" }));
        Assert.That(updated.Timestamps[2].Id, Is.EqualTo(later.Id),
            "Filling a sparse slot preserves later occurrence identity and order.");
        Assert.That(EditorDocumentCodec.Serialize(buffer.Document),
            Is.EqualTo("[00:10.000][00:20.000][00:30.000]Line\n"));
    }

    [Test]
    public void CurrentTimeAllowsOutOfOrderAndEqualOccurrencesButRejectsNegativeAuthoredTime()
    {
        var buffer = new EditorDocumentBuffer(EditorDocumentCodec.Load("[00:30.000]Line\n"),
            UserTrackMetadata.Normalise(null, null));
        var row = buffer.Document.Rows.Single();

        Assert.That(buffer.SetTimestampFromPlayback(new(row.Id, EditorColumn.Lyrics), true, 20_000, 0), Is.True);
        Assert.That(buffer.Document.Validate().Any(item => item.Severity == EditorValidationSeverity.Warning), Is.True);
        Assert.That(buffer.SetTimestampFromPlayback(new(row.Id, EditorColumn.Lyrics), true, 20_000, 0), Is.True,
            "Equal timestamps are valid command input.");
        Assert.That(buffer.Document.Rows.Single().Timestamps.Select(item => item.Value),
            Is.EqualTo(new[] { "00:30.000", "00:20.000", "00:20.000" }));

        var beforeNegative = EditorDocumentCodec.Serialize(buffer.Document);
        Assert.That(buffer.SetTimestampFromPlayback(new(row.Id, EditorColumn.Lyrics), true, 1_000, 2_000), Is.False);
        Assert.That(EditorDocumentCodec.Serialize(buffer.Document), Is.EqualTo(beforeNegative));
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
