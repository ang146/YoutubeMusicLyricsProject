using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Core.Playback;
using LyricsDisplayer.Core.Protocol;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Threading;

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
            Assert.That(coordinator.GetTimelinePosition().CurrentLine!.Text, Is.EqualTo("Remote line"));
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
            Is.EqualTo(LyricsApplyDecision.IgnoredBecauseLocal));
        Assert.Multiple(() =>
        {
            Assert.That(coordinator.Current!.Payload.Track.SourceTrackId, Is.EqualTo("track-a"));
            Assert.That(File.ReadAllText(lrc), Is.EqualTo("not usable LRC"));
            Assert.That(Directory.GetDirectories(Path.Combine(_paths.LibraryPath, "tracks")), Has.Length.EqualTo(1));
        });
    }

    [Test]
    public void ExternalLrcReloadRebuildsTimelineAndPreservesTimingOffset()
    {
        using var library = InitialiseLibrary();
        var imported = library.Import(TrackInfo("track-a"), Lyrics("track-a", 2).Payload);
        var lrc = Path.Combine(_paths.LibraryPath, "tracks", imported.LocalTrackId!, "track.lrc");
        var coordinator = Coordinator(library);
        var paused = Playback("track-a", 1);
        coordinator.Apply(paused with
        {
            Payload = paused.Payload with
            {
                Playback = paused.Payload.Playback with { Playing = false }
            }
        });
        coordinator.ApplyLyricsDetailed(Lyrics("track-a", 2));
        Assert.That(coordinator.AdjustTiming(250).Succeeded, Is.True);

        File.WriteAllText(lrc, "[00:02.000]Externally edited line\n[00:03.000]Next line\n");
        var fingerprint = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(lrc)));
        var update = coordinator.ReloadExternalLocalLyrics(imported.LocalTrackId!, fingerprint);

        Assert.Multiple(() =>
        {
            Assert.That(update, Is.EqualTo(ExternalLocalLyricsUpdate.Reloaded));
            Assert.That(coordinator.CurrentLyrics!.Payload.Lines[0].Text, Is.EqualTo("Externally edited line"));
            Assert.That(coordinator.GlobalOffsetMs, Is.EqualTo(250));
            Assert.That(coordinator.IsCurrentLocalLrcUsable, Is.True);
            Assert.That(coordinator.Current!.Payload.Playback.PositionMs, Is.EqualTo(1_000));
            Assert.That(coordinator.Current.Payload.Playback.Playing, Is.False);
        });
    }

    [Test]
    public void InvalidExternalLrcRetainsLastKnownGoodAndBlocksWritesUntilRepaired()
    {
        using var library = InitialiseLibrary();
        var imported = library.Import(TrackInfo("track-a"), Lyrics("track-a", 2).Payload);
        var lrc = Path.Combine(_paths.LibraryPath, "tracks", imported.LocalTrackId!, "track.lrc");
        var coordinator = Coordinator(library);
        coordinator.Apply(Playback("track-a", 1));
        coordinator.ApplyLyricsDetailed(Lyrics("track-a", 2));
        var oldLine = coordinator.CurrentLyrics!.Payload.Lines.Single().Text;

        File.WriteAllText(lrc, "invalid LRC");
        var invalidFingerprint = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(lrc)));
        Assert.That(coordinator.ReloadExternalLocalLyrics(imported.LocalTrackId!, invalidFingerprint),
            Is.EqualTo(ExternalLocalLyricsUpdate.Invalid));
        Assert.That(coordinator.CurrentLyrics!.Payload.Lines.Single().Text, Is.EqualTo(oldLine));
        Assert.That(coordinator.CanAdjustTiming, Is.False);

        File.WriteAllText(lrc, "[00:02.000]Repaired line\n");
        var repairedFingerprint = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(lrc)));
        Assert.That(coordinator.ReloadExternalLocalLyrics(imported.LocalTrackId!, repairedFingerprint),
            Is.EqualTo(ExternalLocalLyricsUpdate.Reloaded));
        Assert.That(coordinator.CurrentLyrics!.Payload.Lines.Single().Text, Is.EqualTo("Repaired line"));
        Assert.That(coordinator.IsCurrentLocalLrcUsable, Is.True);
    }

    [Test]
    public async Task WatcherRejectsRepeatedMalformedSavesAndRecoversOnValidEdit()
    {
        using var library = InitialiseLibrary();
        var track = TrackInfo("track-a");
        var lyrics = Lyrics("track-a", 2).Payload with
        {
            Lines =
            [
                new LyricsLine(10_000, 20_000, "A"),
                new LyricsLine(20_000, 30_000, "B"),
                new LyricsLine(30_000, 40_000, "C")
            ]
        };
        var imported = library.Import(track, lyrics);
        var lrc = Path.Combine(_paths.LibraryPath, "tracks", imported.LocalTrackId!, "track.lrc");
        const string original = "[00:10.000]A\n[00:20.000]B\n[00:30.000]C\n";
        File.WriteAllText(lrc, original);

        var coordinator = Coordinator(library);
        var playback = Playback("track-a", 1);
        coordinator.Apply(playback with
        {
            Payload = playback.Payload with
            {
                Playback = playback.Payload.Playback with { PositionMs = 15_000, Playing = false }
            }
        });
        Assert.That(coordinator.AdjustTiming(500).Succeeded, Is.True);

        var updates = new ConcurrentQueue<ExternalLocalLyricsUpdate>();
        using var updateSignal = new SemaphoreSlim(0);
        using var watcher = new ActiveLrcFileWatcher(lrc, coordinator.CurrentLocalLyrics!.LrcContentHash,
            observation =>
            {
                if (observation.Kind != ActiveLrcFileObservationKind.Content) return;
                updates.Enqueue(coordinator.ReloadExternalLocalLyrics(imported.LocalTrackId!, observation.Fingerprint!));
                updateSignal.Release();
            }, debounce: TimeSpan.FromMilliseconds(10), retryDelay: TimeSpan.FromMilliseconds(5));

        async Task<ExternalLocalLyricsUpdate> NextUpdate()
        {
            await updateSignal.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(updates.TryDequeue(out var update), Is.True);
            return update;
        }

        var malformedSaves = new[]
        {
            "[00:10.000]A\n[00:20.000B\n[00:30.000]C\n",
            "[00:10.000]A\n[0055.000]B\n[00:30.000]C\n",
            "[00:10.000]A\n[00:xx.000]B\n[00:30.000]C\n",
            "[00:10.000]A\n[00:20.000][bad timestamp]B\n[00:30.000]C\n",
            "[00:10.000]A\n[01:00.000][0200.000]B\n[00:30.000]C\n"
        };
        foreach (var malformed in malformedSaves)
        {
            File.WriteAllText(lrc, malformed);
            watcher.CheckNow();
            Assert.That(await NextUpdate(), Is.EqualTo(ExternalLocalLyricsUpdate.Invalid));
            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(lrc), Is.EqualTo(malformed), "Invalid disk content must remain untouched.");
                Assert.That(coordinator.CurrentLyrics!.Payload.Lines.Select(line => line.Text), Is.EqualTo(new[] { "A", "B", "C" }));
                Assert.That(coordinator.GetTimelinePosition().CurrentLine!.Text, Is.EqualTo("A"));
                Assert.That(coordinator.GetTimelinePosition().NextLine!.Text, Is.EqualTo("B"));
                Assert.That(coordinator.GlobalOffsetMs, Is.EqualTo(500));
                Assert.That(coordinator.Current!.Payload.Playback.PositionMs, Is.EqualTo(15_000));
                Assert.That(coordinator.Current.Payload.Playback.Playing, Is.False);
                Assert.That(coordinator.AdjustTiming(100).Succeeded, Is.False);
                Assert.That(coordinator.IsCurrentLocalLrcUsable, Is.False);
            });
        }

        const string recovered = "[00:10.000]A corrected\n[00:31.000]C\n";
        File.WriteAllText(lrc, recovered);
        watcher.CheckNow();
        Assert.That(await NextUpdate(), Is.EqualTo(ExternalLocalLyricsUpdate.Reloaded));

        Assert.Multiple(() =>
        {
            Assert.That(coordinator.CurrentLyrics!.Payload.Lines.Select(line => line.Text),
                Is.EqualTo(new[] { "A corrected", "C" }));
            Assert.That(coordinator.CurrentLyrics.Payload.Lines[1].StartMs, Is.EqualTo(31_000));
            Assert.That(coordinator.GlobalOffsetMs, Is.EqualTo(500));
            Assert.That(coordinator.LocalAssociationStatus, Is.EqualTo("Found"));
            Assert.That(coordinator.IsCurrentLocalLrcUsable, Is.True);
        });
    }

    [Test]
    public void MissingExternalLrcKeepsLocalAssociationAndCanRecoverWithoutProviderFallback()
    {
        using var library = InitialiseLibrary();
        var imported = library.Import(TrackInfo("track-a"), Lyrics("track-a", 2).Payload);
        var lrc = Path.Combine(_paths.LibraryPath, "tracks", imported.LocalTrackId!, "track.lrc");
        var coordinator = Coordinator(library);
        coordinator.Apply(Playback("track-a", 1));
        coordinator.ApplyLyricsDetailed(Lyrics("track-a", 2));
        File.Delete(lrc);

        Assert.That(coordinator.MarkExternalLocalLyricsMissing(imported.LocalTrackId!), Is.True);
        Assert.That(coordinator.ApplyLyricsDetailed(Lyrics("track-a", 3, text: "Must not replace local")),
            Is.EqualTo(LyricsApplyDecision.IgnoredBecauseLocal));
        Assert.That(File.Exists(lrc), Is.False);
        Assert.That(coordinator.CurrentLyrics, Is.Null);

        File.WriteAllText(lrc, "[00:00.100]Recovered line\n");
        var fingerprint = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(lrc)));
        Assert.That(coordinator.ReloadExternalLocalLyrics(imported.LocalTrackId!, fingerprint),
            Is.EqualTo(ExternalLocalLyricsUpdate.Reloaded));
        Assert.That(coordinator.CurrentLyrics!.Payload.Lines.Single().Text, Is.EqualTo("Recovered line"));
    }

    [Test]
    public void CurrentLineEditAfterExternalReloadUsesFreshSourceOccurrence()
    {
        using var library = InitialiseLibrary();
        var imported = library.Import(TrackInfo("track-a"), Lyrics("track-a", 2).Payload);
        var lrc = Path.Combine(_paths.LibraryPath, "tracks", imported.LocalTrackId!, "track.lrc");
        var coordinator = Coordinator(library);
        coordinator.Apply(Playback("track-a", 1));
        coordinator.ApplyLyricsDetailed(Lyrics("track-a", 2));
        File.WriteAllText(lrc, "[00:00.100]Fresh source occurrence\n");
        var fingerprint = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(lrc)));

        Assert.That(coordinator.ReloadExternalLocalLyrics(imported.LocalTrackId!, fingerprint),
            Is.EqualTo(ExternalLocalLyricsUpdate.Reloaded));
        Assert.That(coordinator.AdjustCurrentLineTiming(100).Succeeded, Is.True);

        Assert.That(File.ReadAllText(lrc), Does.Contain("[00:00.200]Fresh source occurrence"));
    }

    [Test]
    public void LateExternalReloadFromPreviousTrackIsDiscarded()
    {
        using var library = InitialiseLibrary();
        var trackA = library.Import(TrackInfo("track-a"), Lyrics("track-a", 2).Payload);
        var trackB = library.Import(TrackInfo("track-b"), Lyrics("track-b", 2).Payload);
        var coordinator = Coordinator(library);
        coordinator.Apply(Playback("track-a", 1));
        coordinator.ApplyLyricsDetailed(Lyrics("track-a", 2));
        coordinator.Apply(Playback("track-b", 3));
        coordinator.ApplyLyricsDetailed(Lyrics("track-b", 4));
        var pathA = Path.Combine(_paths.LibraryPath, "tracks", trackA.LocalTrackId!, "track.lrc");
        File.WriteAllText(pathA, "[00:02.000]Stale A edit\n");
        var fingerprintA = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pathA)));

        Assert.That(coordinator.ReloadExternalLocalLyrics(trackA.LocalTrackId!, fingerprintA),
            Is.EqualTo(ExternalLocalLyricsUpdate.Stale));
        Assert.That(coordinator.ActiveLocalLyricsRecord!.LocalTrackId, Is.EqualTo(trackB.LocalTrackId));
        Assert.That(coordinator.CurrentLyrics!.Payload.Lines.Single().Text, Is.EqualTo("Remote line"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AppOwnedCurrentLineAndBakeWritesDoNotTriggerReloadWriteLoops(bool bake)
    {
        using var library = InitialiseLibrary();
        var imported = library.Import(TrackInfo("track-a"), Lyrics("track-a", 2).Payload);
        var lrc = Path.Combine(_paths.LibraryPath, "tracks", imported.LocalTrackId!, "track.lrc");
        var coordinator = Coordinator(library);
        coordinator.Apply(Playback("track-a", 1));
        coordinator.ApplyLyricsDetailed(Lyrics("track-a", 2));
        if (bake) Assert.That(coordinator.AdjustTiming(500).Succeeded, Is.True);

        var observed = new TaskCompletionSource<ActiveLrcFileObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observationCount = 0;
        using var watcher = new ActiveLrcFileWatcher(lrc, coordinator.CurrentLocalLyrics!.LrcContentHash,
            change =>
            {
                Interlocked.Increment(ref observationCount);
                observed.TrySetResult(change);
            }, debounce: TimeSpan.FromMilliseconds(10), retryDelay: TimeSpan.FromMilliseconds(5));

        if (bake)
        {
            var target = coordinator.CaptureTimingAdjustmentTarget()!;
            Assert.That(coordinator.BakeTiming(target).Succeeded, Is.True);
        }
        else Assert.That(coordinator.AdjustCurrentLineTiming(100).Succeeded, Is.True);

        var change = await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(change.Kind, Is.EqualTo(ActiveLrcFileObservationKind.Content));
        Assert.That(coordinator.ReloadExternalLocalLyrics(imported.LocalTrackId!, change.Fingerprint!),
            Is.EqualTo(ExternalLocalLyricsUpdate.Unchanged));
        var afterReload = File.ReadAllBytes(lrc);
        watcher.CheckNow();
        await Task.Delay(100);

        Assert.Multiple(() =>
        {
            Assert.That(Interlocked.CompareExchange(ref observationCount, 0, 0), Is.EqualTo(1));
            Assert.That(File.ReadAllBytes(lrc), Is.EqualTo(afterReload));
            Assert.That(coordinator.GlobalOffsetMs, Is.Zero);
            if (bake) Assert.That(File.ReadAllText(lrc), Does.Contain("[00:00.600]"));
            else Assert.That(File.ReadAllText(lrc), Does.Contain("[00:00.200]"));
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
