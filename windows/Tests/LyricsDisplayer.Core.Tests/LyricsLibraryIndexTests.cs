using LyricsDisplayer.Core.Library;
using Microsoft.Data.Sqlite;

namespace LyricsDisplayer.Core.Tests;

[TestFixture]
public sealed class LyricsLibraryIndexTests
{
    private string _root = null!;
    private string _database = null!;
    [SetUp] public void SetUp() { _root = Path.Combine(Path.GetTempPath(), "LyricsDisplayerTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_root); _database = Path.Combine(_root, "index.db"); }
    [TearDown] public void TearDown() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private static LocalTrackRecord Record(string id, string sourceId, string? userTitle = null) => new(
        id, new(userTitle, null), $"tracks/{id}/track.lrc", $"tracks/{id}/track.lyrics.json",
        "youtubeMusic", "Provider", [new("youtubeMusic", sourceId, new("Title", "Artist", null, 10_000))]);

    [Test]
    public void FreshDatabaseCreatesVersionOneSchemaAndForeignKeys()
    {
        using var index = new LyricsLibraryIndex(_database);
        Assert.That(index.GetSchemaVersion(), Is.EqualTo(1));
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = _database, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_list(SourceAssociations);";
        using var reader = command.ExecuteReader();
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader["table"], Is.EqualTo("LocalTracks"));
        reader.Close();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type IN ('table','index') ORDER BY name;";
        using var objects = command.ExecuteReader();
        var names = new List<string>();
        while (objects.Read()) names.Add(objects.GetString(0));
        Assert.That(names, Does.Contain("LocalTracks"));
        Assert.That(names, Does.Contain("SourceAssociations"));
        Assert.That(names, Does.Contain("IX_SourceAssociations_LocalTrackId"));
        Assert.That(names, Does.Contain("IX_SourceAssociations_TitleArtist"));
    }

    [Test]
    public void InsertAndBothIdentityLookupsRoundTrip()
    {
        using var index = new LyricsLibraryIndex(_database);
        var id = Guid.NewGuid().ToString("D");
        index.Upsert(Record(id, "source-a", "User title"));
        Assert.Multiple(() =>
        {
            Assert.That(index.FindByAssociation("youtubeMusic", "source-a")!.LocalTrackId, Is.EqualTo(id));
            Assert.That(index.FindByLocalTrackId(id)!.UserMetadata.Title, Is.EqualTo("User title"));
            Assert.That(index.Search("User title").Single().LocalTrackId, Is.EqualTo(id));
        });
    }

    [Test]
    public void AssociationCannotResolveToTwoTracksAndTransactionRollsBack()
    {
        using var index = new LyricsLibraryIndex(_database);
        var first = Record(Guid.NewGuid().ToString("D"), "same");
        var second = Record(Guid.NewGuid().ToString("D"), "same");
        Assert.That(() => index.Synchronise([first, second],
            new HashSet<string> { Path.GetDirectoryName(first.SidecarRelativePath)!, Path.GetDirectoryName(second.SidecarRelativePath)! },
            new HashSet<(string, string)>()), Throws.TypeOf<SqliteException>());
        Assert.That(index.FindByLocalTrackId(first.LocalTrackId), Is.Null);
    }

    [Test]
    public void ForeignKeyRejectsInvalidAssociation()
    {
        using var index = new LyricsLibraryIndex(_database);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = _database, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON; INSERT INTO SourceAssociations VALUES('x','y','missing','t','a',NULL,1);";
        Assert.That(() => command.ExecuteNonQuery(), Throws.TypeOf<SqliteException>());
    }
}
