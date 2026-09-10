using System.Text.Json.Nodes;
using LyricsDisplayer.Core.Protocol;

namespace LyricsDisplayer.Core.Tests;

[TestFixture]
public sealed class LyricsSnapshotTests
{
    private static ProtocolEnvelope<LyricsSnapshotPayload> Envelope(bool available = true, bool timed = true) => new(
        1, "lyricsSnapshot", "youtubeMusic", "550e8400-e29b-41d4-a716-446655440000", 812,
        DateTimeOffset.Parse("2026-09-09T15:30:00Z"),
        new LyricsSnapshotPayload("abcdefghijk", available, timed, available ? "youtubeMusic" : null,
            timed ? [new LyricsLine(9200, 10630, "測試 \"text\"\n第二行"), new LyricsLine(10680, 12540, "")] : [],
            "Test attribution"));

    [TestCase(true, true)]
    [TestCase(true, false)]
    [TestCase(false, false)]
    public void LyricsStatesRoundTrip(bool available, bool timed)
    {
        var original = Envelope(available, timed);
        Assert.That(ProtocolSerializer.TryParse(ProtocolSerializer.Serialize(original), out var parsed, out var error),
            Is.True, error);
        var message = (LyricsSnapshotMessage)parsed!;
        Assert.Multiple(() =>
        {
            Assert.That(message.Payload.Available, Is.EqualTo(available));
            Assert.That(message.Payload.Timed, Is.EqualTo(timed));
            Assert.That(message.Payload.Lines, Is.EqualTo(original.Payload.Lines));
            Assert.That(message.Payload.SourceTrackId, Is.EqualTo("abcdefghijk"));
            Assert.That(message.Payload.Attribution, Is.EqualTo("Test attribution"));
            Assert.That(ProtocolSerializer.Serialize(message), Does.Not.Contain("\n"));
        });
    }

    [TestCase("sourceTrackId")]
    [TestCase("available")]
    [TestCase("timed")]
    [TestCase("source")]
    [TestCase("lines")]
    public void MissingRequiredFieldRejected(string field)
    {
        var json = JsonNode.Parse(ProtocolSerializer.Serialize(Envelope()))!;
        json["payload"]!.AsObject().Remove(field);
        Assert.That(ProtocolSerializer.TryParse(json.ToJsonString(), out _, out _), Is.False);
    }

    [TestCase("startMs", "-1")]
    [TestCase("endMs", "1")]
    [TestCase("startMs", "1.5")]
    [TestCase("text", "null")]
    public void MalformedLineRejected(string field, string value)
    {
        var json = JsonNode.Parse(ProtocolSerializer.Serialize(Envelope()))!;
        json["payload"]!["lines"]![0]![field] = JsonNode.Parse(value);
        Assert.That(ProtocolSerializer.TryParse(json.ToJsonString(), out _, out _), Is.False);
    }

    [Test]
    public void InconsistentTimedStateRejected()
    {
        Assert.That(ProtocolSerializer.TryParse(ProtocolSerializer.Serialize(Envelope(false, true)), out _, out _), Is.False);
    }
}
