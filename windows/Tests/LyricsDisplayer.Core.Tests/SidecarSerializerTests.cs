using LyricsDisplayer.Core.Library;

namespace LyricsDisplayer.Core.Tests;

[TestFixture]
public sealed class SidecarSerializerTests
{
    private static LyricsSidecar Sidecar(int version = 1, string? title = null, string? artist = null) => new(
        version, "8a963ffc-49eb-478e-a2ee-bff9e6e765cc",
        [new("youtubeMusic", "P4SDPyGfxho", new("來源標題", "アーティスト", null, 244_000)),
         new("futureSource", "other-id", new("Other", "Artist", "Album", 244_000))],
        new(title, artist), new("track.lrc", "youtubeMusic", "Musixmatch", DateTimeOffset.Parse("2026-09-12T00:00:00Z")));

    [Test]
    public void SchemaV1RoundTripsAllPortableConcepts()
    {
        var json = SidecarSerializer.Serialize(Sidecar(1, "Correct Title", "Correct Artist"));
        Assert.That(SidecarSerializer.TryDeserialize(json, out var value, out var error), Is.True, error);
        Assert.Multiple(() =>
        {
            Assert.That(value!.SchemaVersion, Is.EqualTo(1));
            Assert.That(value.LocalTrackId, Is.EqualTo("8a963ffc-49eb-478e-a2ee-bff9e6e765cc"));
            Assert.That(value.SourceAssociations, Has.Count.EqualTo(2));
            Assert.That(value.SourceAssociations[0].Metadata.Title, Is.EqualTo("來源標題"));
            Assert.That(value.UserMetadata.Title, Is.EqualTo("Correct Title"));
            Assert.That(value.Lyrics.File, Is.EqualTo("track.lrc"));
            Assert.That(value.Lyrics.Attribution, Is.EqualTo("Musixmatch"));
        });
    }

    [Test]
    public void NullAndBlankOverridesNormaliseToNull()
    {
        var json = SidecarSerializer.Serialize(Sidecar(title: "  ", artist: null));
        SidecarSerializer.TryDeserialize(json, out var value, out _);
        Assert.Multiple(() => { Assert.That(value!.UserMetadata.Title, Is.Null); Assert.That(value.UserMetadata.Artist, Is.Null); });
    }

    [Test]
    public void InvalidJsonFutureSchemaAndMissingIdentityAreRejected()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SidecarSerializer.TryDeserialize("{", out _, out _), Is.False);
            Assert.That(SidecarSerializer.TryDeserialize(System.Text.Json.JsonSerializer.Serialize(Sidecar(2)), out _, out var future), Is.False);
            Assert.That(future, Does.Contain("future"));
            var missing = Sidecar() with { LocalTrackId = "" };
            Assert.That(() => SidecarSerializer.Serialize(missing), Throws.TypeOf<InvalidDataException>());
        });
    }

    [Test]
    public void EffectiveMetadataUsesOverrideThenLiveThenStored()
    {
        var stored = new SourceTrackMetadata("Stored", "Stored Artist", null, 1);
        var live = new SourceTrackMetadata("Live", "Live Artist", null, 1);
        Assert.That(EffectiveTrackMetadata.From(stored, new(null, null), live), Is.EqualTo(new EffectiveTrackMetadata("Live", "Live Artist")));
        Assert.That(EffectiveTrackMetadata.From(stored, new("User", "User Artist"), live), Is.EqualTo(new EffectiveTrackMetadata("User", "User Artist")));
        Assert.That(EffectiveTrackMetadata.From(stored, new(null, null)), Is.EqualTo(new EffectiveTrackMetadata("Stored", "Stored Artist")));
    }
}
