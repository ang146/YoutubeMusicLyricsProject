using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Core.Protocol;

namespace LyricsDisplayer.Core.Tests;

[TestFixture]
public sealed class LyricsLibraryTests
{
    private string _root = null!;
    private LibraryPaths _paths = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "LyricsDisplayerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _paths = new(_root, Path.Combine(_root, "settings.json"), Path.Combine(_root, "portable"),
            Path.Combine(_root, "library-index.db"), false);
    }

    [TearDown] public void TearDown() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private static TrackInfo Track(string id = "P4SDPyGfxho") => new(id, "Source title", "Source artist", null, 10_000);
    private static LyricsSnapshotPayload Untimed(params string[] lines) =>
        new("P4SDPyGfxho", true, false, "youtubeMusic", [], "Provider", lines);
    private static LyricsSnapshotPayload Timed(string text = "原始歌詞") =>
        new("P4SDPyGfxho", true, true, "youtubeMusic", [new LyricsLine(1_000, 4_000, text), new LyricsLine(4_000, 8_000, "第二行")], "Provider");

    [Test]
    public void FirstImportWritesPortableFilesIndexesAndSurvivesFreshInstance()
    {
        string id;
        using (var library = new LyricsLibrary(_paths))
        {
            Assert.That(library.Initialise().Completed, Is.True);
            var imported = library.Import(Track(), Timed(), DateTimeOffset.Parse("2026-09-12T00:00:00Z"));
            Assert.That(imported.Status, Is.EqualTo(LyricsImportStatus.Imported));
            id = imported.LocalTrackId!;
            Assert.Multiple(() =>
            {
                Assert.That(Guid.Parse(id).ToString("D"), Is.EqualTo(id));
                Assert.That(File.ReadAllText(Path.Combine(_paths.LibraryPath, "tracks", id, "track.lrc")),
                    Does.Contain("[00:01.000]原始歌詞"));
                Assert.That(File.ReadAllText(Path.Combine(_paths.LibraryPath, "tracks", id, "track.lyrics.json")),
                    Does.Contain("\"schemaVersion\": 1"));
                Assert.That(library.Lookup("youtubeMusic", "P4SDPyGfxho").Status, Is.EqualTo(LocalLyricsLookupStatus.Found));
            });
        }
        using var restarted = new LyricsLibrary(_paths,
            (level, category, message) => TestContext.Progress.WriteLine($"{level} [{category}] {message}"));
        restarted.Initialise();
        Assert.That(restarted.Lookup("youtubeMusic", "P4SDPyGfxho").Document!.Record.LocalTrackId, Is.EqualTo(id));
    }

    [Test]
    public void ExistingAndManuallyEditedLocalLyricsAreNeverOverwritten()
    {
        using var library = new LyricsLibrary(_paths);
        library.Initialise();
        var first = library.Import(Track(), Timed());
        var lrc = Path.Combine(_paths.LibraryPath, "tracks", first.LocalTrackId!, "track.lrc");
        var sidecar = Path.Combine(_paths.LibraryPath, "tracks", first.LocalTrackId!, "track.lyrics.json");
        File.WriteAllText(lrc, "[00:01.000]手動編輯\n");
        var sidecarBefore = File.ReadAllText(sidecar);
        var second = library.Import(Track(), Timed("provider replacement"));
        var untimed = library.Import(Track(), Untimed("untimed provider replacement"));
        Assert.Multiple(() =>
        {
            Assert.That(second.Status, Is.EqualTo(LyricsImportStatus.AlreadyLocal));
            Assert.That(untimed.Status, Is.EqualTo(LyricsImportStatus.AlreadyLocal));
            Assert.That(File.ReadAllText(lrc), Does.Contain("手動編輯"));
            Assert.That(File.ReadAllText(lrc), Does.Not.Contain("provider replacement"));
            Assert.That(File.ReadAllText(sidecar), Is.EqualTo(sidecarBefore));
        });
    }

    [Test]
    public void UntimedProviderLyricsMaterialiseAsPlainUtf8AndRemainUntimedAfterRestart()
    {
        string localTrackId;
        var lines = new[] { "第一行", "Line B", "第三行 🎵" };
        using (var library = new LyricsLibrary(_paths))
        {
            library.Initialise();
            var imported = library.Import(Track(), Untimed(lines));
            Assert.That(imported.Status, Is.EqualTo(LyricsImportStatus.Imported));
            localTrackId = imported.LocalTrackId!;
            var path = Path.Combine(_paths.LibraryPath, "tracks", localTrackId, "track.lrc");
            var bytes = File.ReadAllBytes(path);
            var content = System.Text.Encoding.UTF8.GetString(bytes);
            var document = library.Lookup("youtubeMusic", "P4SDPyGfxho").Document!;
            Assert.Multiple(() =>
            {
                Assert.That(bytes.AsSpan().StartsWith(System.Text.Encoding.UTF8.Preamble), Is.False);
                Assert.That(content, Is.EqualTo("第一行\nLine B\n第三行 🎵\n"));
                Assert.That(content, Does.Not.Contain("[00:00.000]"));
                Assert.That(document.IsTimed, Is.False);
                Assert.That(document.Lines, Is.Empty);
                Assert.That(document.UntimedLines, Is.EqualTo(lines));
            });
        }

        using (var restarted = new LyricsLibrary(_paths))
        {
            restarted.Initialise();
            var reloaded = restarted.Lookup("youtubeMusic", "P4SDPyGfxho").Document!;
            Assert.Multiple(() =>
            {
                Assert.That(reloaded.Record.LocalTrackId, Is.EqualTo(localTrackId));
                Assert.That(reloaded.IsTimed, Is.False);
                Assert.That(reloaded.UntimedLines, Is.EqualTo(lines));
            });
        }

        var editedPath = Path.Combine(_paths.LibraryPath, "tracks", localTrackId, "track.lrc");
        File.WriteAllText(editedPath, "[00:01.000]第一行\n[00:02.000]Line B\n[00:03.000]第三行 🎵\n");
        using var timedRestart = new LyricsLibrary(_paths);
        timedRestart.Initialise();
        var timedDocument = timedRestart.Lookup("youtubeMusic", "P4SDPyGfxho").Document!;
        Assert.Multiple(() =>
        {
            Assert.That(timedDocument.Record.LocalTrackId, Is.EqualTo(localTrackId));
            Assert.That(timedDocument.IsTimed, Is.True);
            Assert.That(timedDocument.Lines.Select(line => line.Text), Is.EqualTo(lines));
        });
    }

    [Test]
    public void ExistingLocalUntimedFileWinsOverLaterProviderUntimedLyrics()
    {
        using var library = new LyricsLibrary(_paths);
        library.Initialise();
        var imported = library.Import(Track(), Untimed("Original A", "Original B"));
        var lrc = Path.Combine(_paths.LibraryPath, "tracks", imported.LocalTrackId!, "track.lrc");
        File.WriteAllText(lrc, "User edited A\nOriginal B\n");
        var before = File.ReadAllBytes(lrc);

        var second = library.Import(Track(), Untimed("New provider A", "New provider B"));

        Assert.Multiple(() =>
        {
            Assert.That(second.Status, Is.EqualTo(LyricsImportStatus.AlreadyLocal));
            Assert.That(second.LocalTrackId, Is.EqualTo(imported.LocalTrackId));
            Assert.That(File.ReadAllBytes(lrc), Is.EqualTo(before));
        });
    }

    [TestCase(true, false)]
    [TestCase(false, false)]
    public void UntimedAndUnavailableResultsCreateNothing(bool available, bool timed)
    {
        using var library = new LyricsLibrary(_paths);
        library.Initialise();
        var payload = new LyricsSnapshotPayload("P4SDPyGfxho", available, timed,
            available ? "youtubeMusic" : null, [], null);
        Assert.That(library.Import(Track(), payload).Status, Is.EqualTo(LyricsImportStatus.NotTimed));
        Assert.That(Directory.EnumerateDirectories(Path.Combine(_paths.LibraryPath, "tracks")), Is.Empty);
    }

    [Test]
    public void DeletingDatabaseRebuildsEquivalentAssociationFromPortableSidecar()
    {
        string firstId;
        string secondId;
        using (var library = new LyricsLibrary(_paths))
        {
            library.Initialise();
            firstId = library.Import(Track(), Timed()).LocalTrackId!;
            secondId = library.Import(Track("second-track"),
                Timed() with { SourceTrackId = "second-track" }).LocalTrackId!;
        }
        File.Delete(_paths.IndexPath);
        using var rebuilt = new LyricsLibrary(_paths);
        Assert.That(rebuilt.Initialise().Completed, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(rebuilt.Lookup("youtubeMusic", "P4SDPyGfxho").Document!.Record.LocalTrackId, Is.EqualTo(firstId));
            Assert.That(rebuilt.Lookup("youtubeMusic", "second-track").Document!.Record.LocalTrackId, Is.EqualTo(secondId));
        });
    }

    [Test]
    public void CorruptDatabaseIsPreservedAndRebuiltFromPortableFiles()
    {
        using (var library = new LyricsLibrary(_paths))
        {
            library.Initialise();
            library.Import(Track(), Timed());
        }
        File.WriteAllBytes(_paths.IndexPath, [0x00, 0x01, 0x02, 0x03]);

        using var rebuilt = new LyricsLibrary(_paths);
        Assert.That(rebuilt.Initialise().Completed, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(rebuilt.Lookup("youtubeMusic", "P4SDPyGfxho").Status, Is.EqualTo(LocalLyricsLookupStatus.Found));
            Assert.That(Directory.GetFiles(_root, "library-index.db.corrupt-*"), Has.Length.EqualTo(1));
        });
    }

    [Test]
    public void SidecarEditsWinOverStaleSqliteMetadata()
    {
        string sidecarPath;
        using (var library = new LyricsLibrary(_paths))
        {
            library.Initialise();
            var imported = library.Import(Track(), Timed());
            sidecarPath = Path.Combine(_paths.LibraryPath, "tracks", imported.LocalTrackId!, "track.lyrics.json");
        }
        SidecarSerializer.TryRead(sidecarPath, out var sidecar, out _);
        File.WriteAllText(sidecarPath, SidecarSerializer.Serialize(sidecar! with
        { UserMetadata = new UserTrackMetadata("Manual Title", "Manual Artist") }));
        using var restarted = new LyricsLibrary(_paths);
        restarted.Initialise();
        var found = restarted.Lookup("youtubeMusic", "P4SDPyGfxho").Document!;
        Assert.Multiple(() =>
        {
            Assert.That(found.Record.UserMetadata.Title, Is.EqualTo("Manual Title"));
            Assert.That(found.EffectiveMetadata, Is.EqualTo(new EffectiveTrackMetadata("Manual Title", "Manual Artist")));
        });
    }

    [Test]
    public void SuccessfulScanPrunesMissingDirectoryButFailedScanDoesNotWipeIndex()
    {
        string id;
        using (var library = new LyricsLibrary(_paths))
        {
            library.Initialise();
            id = library.Import(Track(), Timed()).LocalTrackId!;
            Directory.Delete(Path.Combine(_paths.LibraryPath, "tracks", id), true);
            Assert.That(library.ScanAndSynchronise().Completed, Is.True);
            Assert.That(library.Lookup("youtubeMusic", "P4SDPyGfxho").Status, Is.EqualTo(LocalLyricsLookupStatus.NotFound));
        }

        using (var library = new LyricsLibrary(_paths))
        {
            library.Initialise();
            id = library.Import(Track("another-id"), Timed() with { SourceTrackId = "another-id" }).LocalTrackId!;
            Directory.Delete(Path.Combine(_paths.LibraryPath, "tracks"), true);
            Assert.That(library.ScanAndSynchronise().Completed, Is.False);
        }
        using var index = new LyricsLibraryIndex(_paths.IndexPath);
        Assert.That(index.FindByAssociation("youtubeMusic", "another-id")!.LocalTrackId, Is.EqualTo(id));
    }

    [Test]
    public void InvalidAndMissingRecordsDoNotPreventValidRecordIndexing()
    {
        string validId;
        using (var library = new LyricsLibrary(_paths))
        {
            library.Initialise();
            validId = library.Import(Track(), Timed()).LocalTrackId!;
        }
        var invalidDir = Path.Combine(_paths.LibraryPath, "tracks", Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(invalidDir);
        File.WriteAllText(Path.Combine(invalidDir, "track.lyrics.json"), "{");
        var missingDir = Path.Combine(_paths.LibraryPath, "tracks", Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(missingDir);
        using var restarted = new LyricsLibrary(_paths);
        var scan = restarted.Initialise();
        Assert.Multiple(() =>
        {
            Assert.That(scan.Completed, Is.True);
            Assert.That(scan.InvalidRecords, Is.EqualTo(2));
            Assert.That(restarted.Lookup(validId).Status, Is.EqualTo(LocalLyricsLookupStatus.Found));
            Assert.That(File.Exists(Path.Combine(invalidDir, "track.lyrics.json")), Is.True);
        });
    }

    [Test]
    public void BrokenAssociatedRecordBlocksOverwriteAndKeepsFiles()
    {
        string lrc;
        using (var library = new LyricsLibrary(_paths))
        {
            library.Initialise();
            var imported = library.Import(Track(), Timed());
            lrc = Path.Combine(_paths.LibraryPath, "tracks", imported.LocalTrackId!, "track.lrc");
        }
        File.WriteAllText(lrc, "[0055.000]broken");
        using var restarted = new LyricsLibrary(_paths);
        restarted.Initialise();
        Assert.That(restarted.Lookup("youtubeMusic", "P4SDPyGfxho").Status, Is.EqualTo(LocalLyricsLookupStatus.BrokenRecord));
        Assert.That(restarted.Import(Track(), Timed("replacement")).Status, Is.EqualTo(LyricsImportStatus.BlockedByBrokenRecord));
        Assert.That(File.ReadAllText(lrc), Is.EqualTo("[0055.000]broken"));
    }

    [Test]
    public void UnsupportedSidecarAssociationBlocksReimportAfterFreshIndexRebuild()
    {
        string sidecarPath;
        using (var library = new LyricsLibrary(_paths))
        {
            library.Initialise();
            var imported = library.Import(Track(), Timed());
            sidecarPath = Path.Combine(_paths.LibraryPath, "tracks", imported.LocalTrackId!, "track.lyrics.json");
        }
        File.WriteAllText(sidecarPath, File.ReadAllText(sidecarPath).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99"));
        File.Delete(_paths.IndexPath);

        using var rebuilt = new LyricsLibrary(_paths);
        Assert.That(rebuilt.Initialise().InvalidRecords, Is.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(rebuilt.Lookup("youtubeMusic", "P4SDPyGfxho").Status,
                Is.EqualTo(LocalLyricsLookupStatus.BrokenRecord));
            Assert.That(rebuilt.Import(Track(), Timed()).Status,
                Is.EqualTo(LyricsImportStatus.BlockedByBrokenRecord));
            Assert.That(File.ReadAllText(sidecarPath), Does.Contain("\"schemaVersion\": 99"));
        });
    }

    [Test]
    public void DuplicateAssociationIsReportedAndNotChosenByEnumerationOrder()
    {
        using (var library = new LyricsLibrary(_paths))
        {
            library.Initialise();
            library.Import(Track(), Timed());
        }
        var originalDir = Directory.GetDirectories(Path.Combine(_paths.LibraryPath, "tracks")).Single();
        SidecarSerializer.TryRead(Path.Combine(originalDir, "track.lyrics.json"), out var sidecar, out _);
        var duplicateId = Guid.NewGuid().ToString("D");
        var duplicateDir = Path.Combine(_paths.LibraryPath, "tracks", duplicateId);
        Directory.CreateDirectory(duplicateDir);
        File.Copy(Path.Combine(originalDir, "track.lrc"), Path.Combine(duplicateDir, "track.lrc"));
        File.WriteAllText(Path.Combine(duplicateDir, "track.lyrics.json"),
            SidecarSerializer.Serialize(sidecar! with { LocalTrackId = duplicateId }));
        using var restarted = new LyricsLibrary(_paths);
        Assert.That(restarted.Initialise().DuplicateAssociations, Is.EqualTo(1));
        Assert.That(restarted.Lookup("youtubeMusic", "P4SDPyGfxho").Status, Is.EqualTo(LocalLyricsLookupStatus.DuplicateAssociation));
        Assert.That(restarted.Import(Track(), Timed()).Status, Is.EqualTo(LyricsImportStatus.BlockedByBrokenRecord));
    }

    [Test]
    public void ConfiguredUnavailableUncDoesNotCreateDefaultOrPruneIndex()
    {
        var configured = new LibraryPaths(_root, Path.Combine(_root, "settings.json"),
            "\\\\invalid-server-for-lyrics-tests\\missing-share\\Lyrics", _paths.IndexPath, true);
        using var library = new LyricsLibrary(configured);
        Assert.That(library.Initialise().Completed, Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(library.LibraryAvailable, Is.False);
            Assert.That(Directory.Exists(Path.Combine(_root, "Lyrics")), Is.False);
            Assert.That(library.Import(Track(), Timed()).Status, Is.EqualTo(LyricsImportStatus.LibraryUnavailable));
        });
    }
}
