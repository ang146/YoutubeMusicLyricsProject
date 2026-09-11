using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Core.Playback;
using LyricsDisplayer.Core.Protocol;

namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class LocalLyricsCoordinatorTests
{
    private string _root = null!;
    private LibraryPaths _paths = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "LyricsDisplayerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _paths = new(_root, Path.Combine(_root, "settings.json"), Path.Combine(_root, "Lyrics"),
            Path.Combine(_root, "library-index.db"), false);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Test]
    public void TrackChangeLoadsEditedLocalLyricsBeforeRemoteResult()
    {
        using var library = InitialiseLibrary();
        var imported = library.Import(TrackInfo("track-a"), Lyrics("track-a", 2).Payload);
        var lrc = Path.Combine(_paths.LibraryPath, "tracks", imported.LocalTrackId!, "track.lrc");
        File.WriteAllText(lrc, "[00:00.100]Hand edited\n");
        var coordinator = Coordinator(library);

        coordinator.Apply(Playback("track-a", 1));
        var remoteDecision = coordinator.ApplyLyricsDetailed(Lyrics("track-a", 2, text: "Remote replacement"));

        Assert.Multiple(() =>
        {
            Assert.That(coordinator.CurrentLyrics!.Payload.Lines.Single().Text, Is.EqualTo("Hand edited"));
            Assert.That(coordinator.LyricsLoadedFrom, Is.EqualTo("Local Library"));
            Assert.That(coordinator.LocalAssociationStatus, Is.EqualTo("Found"));
            Assert.That(remoteDecision, Is.EqualTo(LyricsApplyDecision.IgnoredBecauseLocal));
            Assert.That(File.ReadAllText(lrc), Does.Not.Contain("Remote replacement"));
        });
    }

    [Test]
    public void FirstTimedRemoteResultBecomesLocalAndIsImmediatelyAuthoritative()
    {
        using var library = InitialiseLibrary();
        var coordinator = Coordinator(library);
        coordinator.Apply(Playback("track-a", 1));

        var decision = coordinator.ApplyLyricsDetailed(Lyrics("track-a", 2));

        Assert.Multiple(() =>
        {
            Assert.That(decision, Is.EqualTo(LyricsApplyDecision.ImportedAsLocal));
            Assert.That(coordinator.CurrentLocalLyrics, Is.Not.Null);
            Assert.That(coordinator.LyricsLoadedFrom, Is.EqualTo("Local Library"));
            Assert.That(coordinator.LocalAssociationStatus, Is.EqualTo("Imported"));
            Assert.That(library.Lookup("youtubeMusic", "track-a").Status, Is.EqualTo(LocalLyricsLookupStatus.Found));
            Assert.That(Directory.GetDirectories(Path.Combine(_paths.LibraryPath, "tracks")), Has.Length.EqualTo(1));
        });
    }

    [TestCase(true, false)]
    [TestCase(false, false)]
    public void UntimedOrUnavailableRemoteResultStaysRuntimeOnly(bool available, bool timed)
    {
        using var library = InitialiseLibrary();
        var coordinator = Coordinator(library);
        coordinator.Apply(Playback("track-a", 1));

        var decision = coordinator.ApplyLyricsDetailed(Lyrics("track-a", 2, available, timed));

        Assert.That(decision, Is.EqualTo(LyricsApplyDecision.AcceptedRemote));
        Assert.That(coordinator.CurrentLocalLyrics, Is.Null);
        Assert.That(Directory.EnumerateDirectories(Path.Combine(_paths.LibraryPath, "tracks")), Is.Empty);
    }

    [Test]
    public void LatePreviousTrackResultCannotImportIntoCurrentTrack()
    {
        using var library = InitialiseLibrary();
        var coordinator = Coordinator(library);
        coordinator.Apply(Playback("track-a", 1));
        coordinator.Apply(Playback("track-b", 10));

        Assert.That(coordinator.ApplyLyricsDetailed(Lyrics("track-a", 11)), Is.EqualTo(LyricsApplyDecision.Rejected));
        Assert.That(coordinator.ApplyLyricsDetailed(Lyrics("track-b", 12)), Is.EqualTo(LyricsApplyDecision.ImportedAsLocal));
        Assert.Multiple(() =>
        {
            Assert.That(library.Lookup("youtubeMusic", "track-a").Status, Is.EqualTo(LocalLyricsLookupStatus.NotFound));
            Assert.That(library.Lookup("youtubeMusic", "track-b").Status, Is.EqualTo(LocalLyricsLookupStatus.Found));
        });
    }

    [Test]
    public void OldSessionResultCannotAffectNewSession()
    {
        using var library = InitialiseLibrary();
        var coordinator = Coordinator(library);
        coordinator.Apply(Playback("track-a", 20, "old-session"));
        coordinator.Apply(Playback("track-a", 1, "new-session"));

        Assert.That(coordinator.ApplyLyricsDetailed(Lyrics("track-a", 21, session: "old-session")),
            Is.EqualTo(LyricsApplyDecision.Rejected));
        Assert.That(coordinator.ApplyLyricsDetailed(Lyrics("track-a", 2, session: "new-session")),
            Is.EqualTo(LyricsApplyDecision.ImportedAsLocal));
    }

    [Test]
    public void UnavailableConfiguredLibraryDoesNotBreakRuntimeLyrics()
    {
        var paths = _paths with
        {
            LibraryPath = "\\\\invalid-server-for-lyrics-tests\\missing-share\\Lyrics",
            UsesConfiguredPath = true
        };
        using var library = new LyricsLibrary(paths);
        Assert.That(library.Initialise().Completed, Is.False);
        var coordinator = Coordinator(library);
        coordinator.Apply(Playback("track-a", 1));

        Assert.That(coordinator.ApplyLyricsDetailed(Lyrics("track-a", 2)),
            Is.EqualTo(LyricsApplyDecision.AcceptedRemote));
        Assert.That(coordinator.CurrentLyrics!.Payload.Lines.Single().Text, Is.EqualTo("Remote line"));
        Assert.That(coordinator.CurrentLocalLyrics, Is.Null);
    }

    [Test]
    public void BrokenLocalLoadDoesNotBreakPlaybackOrPermitRemoteOverwrite()
    {
        using var library = InitialiseLibrary();
        var imported = library.Import(TrackInfo("track-a"), Lyrics("track-a", 2).Payload);
        var lrc = Path.Combine(_paths.LibraryPath, "tracks", imported.LocalTrackId!, "track.lrc");
        File.WriteAllText(lrc, "not usable LRC");
        library.ScanAndSynchronise();
        var coordinator = Coordinator(library);

        Assert.That(coordinator.Apply(Playback("track-a", 3)), Is.EqualTo(SnapshotDecision.Accepted));
        Assert.That(coordinator.LocalAssociationStatus, Is.EqualTo("BrokenRecord"));
        Assert.That(coordinator.ApplyLyricsDetailed(Lyrics("track-a", 4)),
            Is.EqualTo(LyricsApplyDecision.AcceptedRemote));
        Assert.Multiple(() =>
        {
            Assert.That(coordinator.Current!.Payload.Track.SourceTrackId, Is.EqualTo("track-a"));
            Assert.That(File.ReadAllText(lrc), Is.EqualTo("not usable LRC"));
            Assert.That(Directory.GetDirectories(Path.Combine(_paths.LibraryPath, "tracks")), Has.Length.EqualTo(1));
        });
    }

    [Test]
    public void SqliteInitialisationFailureDoesNotBreakPlaybackOrRuntimeLyrics()
    {
        Directory.CreateDirectory(_paths.IndexPath);
        using var library = new LyricsLibrary(_paths);
        Assert.That(library.Initialise().Completed, Is.True);
        Assert.That(library.IndexStatus, Is.EqualTo("Unavailable"));
        var coordinator = Coordinator(library);

        Assert.That(coordinator.Apply(Playback("track-a", 1)), Is.EqualTo(SnapshotDecision.Accepted));
        Assert.That(coordinator.ApplyLyricsDetailed(Lyrics("track-a", 2)),
            Is.EqualTo(LyricsApplyDecision.AcceptedRemote));
        Assert.That(coordinator.CurrentLyrics!.Payload.Lines.Single().Text, Is.EqualTo("Remote line"));
    }

    private LyricsLibrary InitialiseLibrary()
    {
        var library = new LyricsLibrary(_paths);
        Assert.That(library.Initialise().Completed, Is.True);
        return library;
    }

    private static PlaybackStateCoordinator Coordinator(LyricsLibrary library) =>
        new(new SnapshotStateTracker(), new PlaybackClock(), library);

    private static TrackInfo TrackInfo(string id) => new(id, $"Title {id}", "Artist", null, 60_000);

    private static PlaybackSnapshotMessage Playback(string id, long sequence, string session = "session-a") =>
        new(Metadata(sequence, session, ProtocolConstants.PlaybackSnapshot),
            new(TrackInfo(id), new(1_000, true, 1), new(false, false, null, [])), "{}");

    private static LyricsSnapshotMessage Lyrics(string id, long sequence, bool available = true, bool timed = true,
        string session = "session-a", string text = "Remote line") =>
        new(Metadata(sequence, session, ProtocolConstants.LyricsSnapshot),
            new(id, available, timed, available ? "youtubeMusic" : null,
                timed ? [new LyricsLine(100, 500, text)] : [], "Provider"), "{}");

    private static EnvelopeMetadata Metadata(long sequence, string session, string type) =>
        new(1, type, ProtocolConstants.YouTubeMusicSource, session, sequence,
            DateTimeOffset.Parse("2026-09-12T00:00:00Z"));
}
