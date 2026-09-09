using LyricsDisplayer.Core.Protocol;

namespace LyricsDisplayer.Core.Tests;

[TestFixture]
public sealed class ProtocolSerializerTests
{
    [Test]
    public void ProtocolVersionOneIsAccepted()
    {
        var json = ProtocolSerializer.Serialize(CreateSnapshotEnvelope());
        Assert.That(ProtocolSerializer.TryParse(json, out _, out _), Is.True);
    }

    [Test]
    public void UnsupportedProtocolVersionIsRejected()
    {
        var envelope = CreateSnapshotEnvelope() with { ProtocolVersion = 2 };
        var success = ProtocolSerializer.TryParse(ProtocolSerializer.Serialize(envelope), out _, out var error);
        Assert.Multiple(() =>
        {
            Assert.That(success, Is.False);
            Assert.That(error, Does.Contain("Unsupported protocolVersion"));
        });
    }

    [Test]
    public void PlaybackSnapshotDeserialisesAndRoundTripsMillisecondTrackAndLyricsValues()
    {
        var original = CreateSnapshotEnvelope(sequence: 42);
        var json = ProtocolSerializer.Serialize(original);

        var success = ProtocolSerializer.TryParse(json, out var message, out var error);

        Assert.That(success, Is.True, error);
        var snapshot = message as PlaybackSnapshotMessage;
        Assert.That(snapshot, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(snapshot!.Envelope.Sequence, Is.EqualTo(42));
            Assert.That(snapshot.Payload.Track, Is.EqualTo(original.Payload.Track));
            Assert.That(snapshot.Payload.Track.DurationMs, Is.EqualTo(243520));
            Assert.That(snapshot.Payload.Playback.PositionMs, Is.EqualTo(53420));
            Assert.That(snapshot.Payload.Lyrics.Lines[0].StartMs, Is.EqualTo(50000));
            Assert.That(snapshot.Payload.Lyrics.Lines[0].EndMs, Is.EqualTo(55000));
            Assert.That(snapshot.Payload.Lyrics.Lines[0].Text, Is.EqualTo("First synthetic line"));
        });
    }

    [Test]
    public void DiagnosticLogDeserialisesCorrectly()
    {
        var envelope = new ProtocolEnvelope<DiagnosticLogPayload>(
            1, "diagnosticLog", "youtubeMusic", Guid.NewGuid().ToString(), 5,
            DateTimeOffset.Parse("2026-09-09T05:30:00.250Z"),
            new DiagnosticLogPayload("Information", "PlaybackSource", "Synthetic source started."));

        var success = ProtocolSerializer.TryParse(ProtocolSerializer.Serialize(envelope), out var message, out var error);

        Assert.That(success, Is.True, error);
        var diagnostic = message as DiagnosticLogMessage;
        Assert.Multiple(() =>
        {
            Assert.That(diagnostic, Is.Not.Null);
            Assert.That(diagnostic!.Payload.Level, Is.EqualTo("Information"));
            Assert.That(diagnostic.Payload.Category, Is.EqualTo("PlaybackSource"));
            Assert.That(diagnostic.Payload.Message, Is.EqualTo("Synthetic source started."));
        });
    }

    [TestCase("protocolVersion")]
    [TestCase("messageType")]
    [TestCase("source")]
    [TestCase("sourceSessionId")]
    [TestCase("sequence")]
    [TestCase("sentAtUtc")]
    [TestCase("payload")]
    public void MissingRequiredEnvelopeFieldIsRejected(string field)
    {
        var json = ProtocolSerializer.Serialize(CreateSnapshotEnvelope());
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var values = document.RootElement.EnumerateObject()
            .Where(property => property.Name != field)
            .ToDictionary(property => property.Name, property => property.Value.Clone());
        var withoutField = System.Text.Json.JsonSerializer.Serialize(values);

        var success = ProtocolSerializer.TryParse(withoutField, out _, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.False);
            Assert.That(error, Does.Contain(field));
        });
    }

    [Test]
    public void SequenceGapsAreValid()
    {
        var first = ProtocolSerializer.TryParse(
            ProtocolSerializer.Serialize(CreateSnapshotEnvelope(sequence: 1)), out _, out var firstError);
        var afterGap = ProtocolSerializer.TryParse(
            ProtocolSerializer.Serialize(CreateSnapshotEnvelope(sequence: 100)), out _, out var gapError);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.True, firstError);
            Assert.That(afterGap, Is.True, gapError);
        });
    }

    [Test]
    public void WrongJsonTypesAreRejectedWithoutThrowing()
    {
        const string json = """
            {"protocolVersion":"1","messageType":"playbackSnapshot","source":"youtubeMusic",
             "sourceSessionId":"550e8400-e29b-41d4-a716-446655440000","sequence":1,
             "sentAtUtc":"2026-09-09T05:30:00Z","payload":{}}
            """;

        Assert.DoesNotThrow(() =>
        {
            var success = ProtocolSerializer.TryParse(json, out _, out var error);
            Assert.That(success, Is.False);
            Assert.That(error, Does.Contain("protocolVersion"));
        });
    }

    [Test]
    public void ParsedSnapshotCanBeReserialisedAsOneCompactPipeLine()
    {
        var indented = System.Text.Json.JsonSerializer.Serialize(
            CreateSnapshotEnvelope(),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            });
        Assert.That(ProtocolSerializer.TryParse(indented, out var parsed, out var error), Is.True, error);

        var compact = ProtocolSerializer.Serialize((PlaybackSnapshotMessage)parsed!);

        Assert.Multiple(() =>
        {
            Assert.That(compact, Does.Not.Contain("\r"));
            Assert.That(compact, Does.Not.Contain("\n"));
            Assert.That(ProtocolSerializer.TryParse(compact, out _, out _), Is.True);
        });
    }

    [Test]
    public void RealTrackSnapshotWithUnavailableLyricsRoundTrips()
    {
        var envelope = new ProtocolEnvelope<PlaybackSnapshotPayload>(
            1,
            "playbackSnapshot",
            "youtubeMusic",
            "550e8400-e29b-41d4-a716-446655440000",
            27,
            DateTimeOffset.Parse("2026-09-09T05:30:00Z"),
            new PlaybackSnapshotPayload(
                new TrackInfo("AbCdEfGhI12", "Observed Song", "Observed Artist", null, 231442),
                new PlaybackState(52137, true, 1.0),
                new LyricsInfo(false, false, null, [])));

        var success = ProtocolSerializer.TryParse(ProtocolSerializer.Serialize(envelope), out var parsed, out var error);

        Assert.That(success, Is.True, error);
        var snapshot = (PlaybackSnapshotMessage)parsed!;
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Payload.Track.SourceTrackId, Is.EqualTo("AbCdEfGhI12"));
            Assert.That(snapshot.Payload.Track.Album, Is.Null);
            Assert.That(snapshot.Payload.Track.DurationMs, Is.EqualTo(231442));
            Assert.That(snapshot.Payload.Playback.PositionMs, Is.EqualTo(52137));
            Assert.That(snapshot.Payload.Lyrics.Available, Is.False);
            Assert.That(snapshot.Payload.Lyrics.Timed, Is.False);
            Assert.That(snapshot.Payload.Lyrics.Source, Is.Null);
            Assert.That(snapshot.Payload.Lyrics.Lines, Is.Empty);
        });
    }

    [Test]
    public void EmptyAlbumIsAccepted()
    {
        var original = CreateSnapshotEnvelope();
        var envelope = original with
        {
            Payload = original.Payload with
            {
                Track = original.Payload.Track with { Album = string.Empty }
            }
        };

        var success = ProtocolSerializer.TryParse(ProtocolSerializer.Serialize(envelope), out var parsed, out var error);

        Assert.That(success, Is.True, error);
        Assert.That(((PlaybackSnapshotMessage)parsed!).Payload.Track.Album, Is.Empty);
    }

    internal static ProtocolEnvelope<PlaybackSnapshotPayload> CreateSnapshotEnvelope(long sequence = 1)
    {
        var payload = new PlaybackSnapshotPayload(
            new TrackInfo("fake-video-7421", "Synthetic Song 7421", "Synthetic Artist 18",
                "Synthetic Album 4", 243520),
            new PlaybackState(53420, true, 1.0),
            new LyricsInfo(true, true, "youtubeMusic",
                [new LyricsLine(50000, 55000, "First synthetic line")]));
        return new ProtocolEnvelope<PlaybackSnapshotPayload>(
            1, "playbackSnapshot", "youtubeMusic", "550e8400-e29b-41d4-a716-446655440000", sequence,
            DateTimeOffset.Parse("2026-09-09T05:30:00.123Z"), payload);
    }
}
