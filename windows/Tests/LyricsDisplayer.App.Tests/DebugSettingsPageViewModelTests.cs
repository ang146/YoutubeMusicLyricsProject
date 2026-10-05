using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Core.Protocol;

namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class DebugSettingsPageViewModelTests
{
    [Test]
    public void ExposesExistingPathsAndEnablesFolderActionsOnlyWhenPathsAreConfigured()
    {
        var viewModel = new DebugSettingsPageViewModel(
            "C:\\Lyrics", "C:\\Lyrics\\library-index.db", "C:\\App\\Logs", "C:\\App\\Logs\\Crash",
            _ => { });
        var unconfigured = new DebugSettingsPageViewModel();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.LibraryPath, Is.EqualTo("C:\\Lyrics"));
            Assert.That(viewModel.IndexPath, Is.EqualTo("C:\\Lyrics\\library-index.db"));
            Assert.That(viewModel.LogsDirectory, Is.EqualTo("C:\\App\\Logs"));
            Assert.That(viewModel.CrashReportsDirectory, Is.EqualTo("C:\\App\\Logs\\Crash"));
            Assert.That(viewModel.OpenLogsFolderCommand.CanExecute(null), Is.True);
            Assert.That(viewModel.OpenCrashReportsFolderCommand.CanExecute(null), Is.True);
            Assert.That(unconfigured.OpenLogsFolderCommand.CanExecute(null), Is.False);
            Assert.That(unconfigured.OpenCrashReportsFolderCommand.CanExecute(null), Is.False);
        });
    }

    [Test]
    public void RuntimeDiagnosticsUpdateFromPlaybackAndLyricsSnapshots()
    {
        var viewModel = new DebugSettingsPageViewModel();
        var propertyChanges = new List<string?>();
        viewModel.PropertyChanged += (_, args) => propertyChanges.Add(args.PropertyName);
        var playback = Playback("track-a", "raw playback A");
        var lyrics = Lyrics("track-a", "raw lyrics A");
        var localTrack = new LocalTrackRecord("local-a", new UserTrackMetadata(null, null),
            "song.lrc", "song.json", null, null, []);

        viewModel.UpdateRuntimeState(playback, lyrics, localTrack,
            new EffectiveTrackMetadata("Effective title", "Effective artist"), "Local Library", "Found");
        viewModel.SetTransportStatus("Connected");

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.PlaybackSource, Is.EqualTo("youtubeMusic"));
            Assert.That(viewModel.SourceTrackId, Is.EqualTo("track-a"));
            Assert.That(viewModel.LocalTrackId, Is.EqualTo("local-a"));
            Assert.That(viewModel.EffectiveTitle, Is.EqualTo("Effective title"));
            Assert.That(viewModel.EffectiveArtist, Is.EqualTo("Effective artist"));
            Assert.That(viewModel.PlaybackState, Does.Contain("Playing"));
            Assert.That(viewModel.TransportStatus, Is.EqualTo("Connected"));
            Assert.That(viewModel.LyricsSource, Is.EqualTo("test-provider"));
            Assert.That(viewModel.LyricsAvailability, Is.EqualTo("Available"));
            Assert.That(viewModel.LyricsTiming, Is.EqualTo("Timed"));
            Assert.That(viewModel.LyricsLoadedFrom, Is.EqualTo("Local Library"));
            Assert.That(viewModel.LocalAssociationStatus, Is.EqualTo("Found"));
            Assert.That(viewModel.RawPlaybackJson, Is.EqualTo("raw playback A"));
            Assert.That(viewModel.RawLyricsJson, Is.EqualTo("raw lyrics A"));
            Assert.That(propertyChanges, Does.Contain(nameof(viewModel.RawPlaybackJson)));
            Assert.That(propertyChanges, Does.Contain(nameof(viewModel.RawLyricsJson)));
            Assert.That(propertyChanges, Does.Contain(nameof(viewModel.TransportStatus)));
        });

        viewModel.UpdateRuntimeState(Playback("track-b", "raw playback B"), null, null,
            new EffectiveTrackMetadata("Next title", "Next artist"), "YouTube Music (runtime)", "Not checked");

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.SourceTrackId, Is.EqualTo("track-b"));
            Assert.That(viewModel.LocalTrackId, Is.EqualTo("Not available"));
            Assert.That(viewModel.EffectiveTitle, Is.EqualTo("Next title"));
            Assert.That(viewModel.LyricsSource, Is.EqualTo("test-provider"));
            Assert.That(viewModel.LyricsLoadedFrom, Is.EqualTo("YouTube Music (runtime)"));
            Assert.That(viewModel.RawPlaybackJson, Is.EqualTo("raw playback B"));
            Assert.That(viewModel.RawLyricsJson, Is.EqualTo("No raw lyrics snapshot is currently available."));
        });
    }

    private static PlaybackSnapshotMessage Playback(string trackId, string rawJson) =>
        new(new EnvelopeMetadata(1, ProtocolConstants.PlaybackSnapshot, "youtubeMusic", "session", 1,
                DateTimeOffset.UnixEpoch),
            new PlaybackSnapshotPayload(new TrackInfo(trackId, "Source title", "Source artist", null, 120_000),
                new PlaybackState(3_000, true, 1), new LyricsInfo(true, true, "test-provider", [])), rawJson);

    private static LyricsSnapshotMessage Lyrics(string trackId, string rawJson) =>
        new(new EnvelopeMetadata(1, ProtocolConstants.LyricsSnapshot, "youtubeMusic", "session", 2,
                DateTimeOffset.UnixEpoch),
            new LyricsSnapshotPayload(trackId, true, true, "test-provider", [], null), rawJson);
}
