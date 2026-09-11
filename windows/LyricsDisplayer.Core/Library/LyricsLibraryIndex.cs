using Microsoft.Data.Sqlite;

namespace LyricsDisplayer.Core.Library;

public sealed class LyricsLibraryIndex : IDisposable
{
    public const int SchemaVersion = 1;
    private readonly SqliteConnection _connection;

    public string DatabasePath { get; }

    public LyricsLibraryIndex(string databasePath)
    {
        DatabasePath = databasePath;
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());
        try
        {
            _connection.Open();
            Execute("PRAGMA foreign_keys = ON;");
            InitialiseSchema();
        }
        catch
        {
            _connection.Dispose();
            throw;
        }
    }

    public int GetSchemaVersion()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public LocalTrackRecord? FindByAssociation(string source, string sourceTrackId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT lt.LocalTrackId, lt.UserTitle, lt.UserArtist, lt.LyricsRelativePath,
                   lt.SidecarRelativePath, lt.LyricsSource, lt.Attribution
            FROM SourceAssociations sa
            JOIN LocalTracks lt ON lt.LocalTrackId = sa.LocalTrackId
            WHERE sa.Source = $source AND sa.SourceTrackId = $sourceTrackId;
            """;
        command.Parameters.AddWithValue("$source", source);
        command.Parameters.AddWithValue("$sourceTrackId", sourceTrackId);
        LocalTrackRecord? record;
        using (var reader = command.ExecuteReader())
            record = reader.Read() ? ReadBaseRecord(reader) : null;
        return AddAssociations(record);
    }

    public LocalTrackRecord? FindByLocalTrackId(string localTrackId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT LocalTrackId, UserTitle, UserArtist, LyricsRelativePath,
                   SidecarRelativePath, LyricsSource, Attribution
            FROM LocalTracks WHERE LocalTrackId = $id;
            """;
        command.Parameters.AddWithValue("$id", localTrackId);
        LocalTrackRecord? record;
        using (var reader = command.ExecuteReader())
            record = reader.Read() ? ReadBaseRecord(reader) : null;
        return AddAssociations(record);
    }

    public IReadOnlyList<LocalTrackRecord> Search(string text)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT DISTINCT lt.LocalTrackId, lt.UserTitle, lt.UserArtist, lt.LyricsRelativePath,
                   lt.SidecarRelativePath, lt.LyricsSource, lt.Attribution
            FROM LocalTracks lt
            JOIN SourceAssociations sa ON sa.LocalTrackId = lt.LocalTrackId
            WHERE COALESCE(lt.UserTitle, sa.SourceTitle) LIKE $query ESCAPE '\'
               OR COALESCE(lt.UserArtist, sa.SourceArtist) LIKE $query ESCAPE '\'
            ORDER BY COALESCE(lt.UserArtist, sa.SourceArtist), COALESCE(lt.UserTitle, sa.SourceTitle);
            """;
        var escaped = text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        command.Parameters.AddWithValue("$query", $"%{escaped}%");
        var records = new List<LocalTrackRecord>();
        using (var reader = command.ExecuteReader())
            while (reader.Read()) records.Add(ReadBaseRecord(reader));
        return records.Select(record => AddAssociations(record)!).ToArray();
    }

    public void Synchronise(
        IReadOnlyList<LocalTrackRecord> records,
        IReadOnlySet<string> existingTrackDirectories,
        IReadOnlySet<(string Source, string SourceTrackId)> duplicateAssociations)
    {
        using var transaction = _connection.BeginTransaction();
        foreach (var record in records)
        {
            UpsertRecord(record, duplicateAssociations, transaction);
        }

        using var stale = _connection.CreateCommand();
        stale.Transaction = transaction;
        stale.CommandText = "SELECT LocalTrackId, SidecarRelativePath FROM LocalTracks;";
        var staleIds = new List<string>();
        using (var reader = stale.ExecuteReader())
        {
            while (reader.Read())
            {
                var relative = reader.GetString(1).Replace('/', Path.DirectorySeparatorChar);
                var directory = (Path.GetDirectoryName(relative) ?? string.Empty)
                    .Replace(Path.DirectorySeparatorChar, '/');
                if (!existingTrackDirectories.Contains(directory)) staleIds.Add(reader.GetString(0));
            }
        }
        foreach (var id in staleIds) Delete(id, transaction);
        transaction.Commit();
    }

    public void Upsert(LocalTrackRecord record)
    {
        using var transaction = _connection.BeginTransaction();
        UpsertRecord(record, new HashSet<(string, string)>(), transaction);
        transaction.Commit();
    }

    public void Delete(string localTrackId)
    {
        using var transaction = _connection.BeginTransaction();
        Delete(localTrackId, transaction);
        transaction.Commit();
    }

    private void InitialiseSchema()
    {
        var version = GetSchemaVersion();
        if (version > SchemaVersion) throw new InvalidDataException($"Unsupported SQLite schema version {version}.");
        if (version == SchemaVersion)
        {
            ValidateVersionOneSchema();
            return;
        }
        if (version != 0) throw new InvalidDataException($"Unsupported SQLite schema version {version}.");
        using var transaction = _connection.BeginTransaction();
        Execute("""
            CREATE TABLE LocalTracks (
                LocalTrackId TEXT PRIMARY KEY,
                UserTitle TEXT NULL,
                UserArtist TEXT NULL,
                LyricsRelativePath TEXT NOT NULL,
                SidecarRelativePath TEXT NOT NULL UNIQUE,
                LyricsSource TEXT NULL,
                Attribution TEXT NULL
            );
            CREATE TABLE SourceAssociations (
                Source TEXT NOT NULL,
                SourceTrackId TEXT NOT NULL,
                LocalTrackId TEXT NOT NULL,
                SourceTitle TEXT NOT NULL,
                SourceArtist TEXT NOT NULL,
                SourceAlbum TEXT NULL,
                DurationMs INTEGER NOT NULL CHECK (DurationMs >= 0),
                PRIMARY KEY (Source, SourceTrackId),
                FOREIGN KEY (LocalTrackId) REFERENCES LocalTracks(LocalTrackId) ON DELETE CASCADE
            );
            CREATE INDEX IX_SourceAssociations_LocalTrackId ON SourceAssociations(LocalTrackId);
            CREATE INDEX IX_SourceAssociations_TitleArtist ON SourceAssociations(SourceTitle, SourceArtist);
            PRAGMA user_version = 1;
            """, transaction);
        transaction.Commit();
        ValidateVersionOneSchema();
    }

    private void ValidateVersionOneSchema()
    {
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = """
                SELECT LocalTrackId, UserTitle, UserArtist, LyricsRelativePath, SidecarRelativePath,
                       LyricsSource, Attribution FROM LocalTracks LIMIT 0;
                SELECT Source, SourceTrackId, LocalTrackId, SourceTitle, SourceArtist, SourceAlbum,
                       DurationMs FROM SourceAssociations LIMIT 0;
                """;
            command.ExecuteNonQuery();
        }
        using var foreignKey = _connection.CreateCommand();
        foreignKey.CommandText = "PRAGMA foreign_key_list(SourceAssociations);";
        using var reader = foreignKey.ExecuteReader();
        if (!reader.Read() || !string.Equals(reader["table"]?.ToString(), "LocalTracks", StringComparison.Ordinal))
            throw new InvalidDataException("SQLite schema version 1 is missing its SourceAssociations foreign key.");
    }

    private void UpsertRecord(LocalTrackRecord record,
        IReadOnlySet<(string Source, string SourceTrackId)> duplicates, SqliteTransaction transaction)
    {
        using (var command = _connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO LocalTracks(LocalTrackId, UserTitle, UserArtist, LyricsRelativePath,
                                        SidecarRelativePath, LyricsSource, Attribution)
                VALUES($id, $userTitle, $userArtist, $lyricsPath, $sidecarPath, $source, $attribution)
                ON CONFLICT(LocalTrackId) DO UPDATE SET
                    UserTitle=excluded.UserTitle, UserArtist=excluded.UserArtist,
                    LyricsRelativePath=excluded.LyricsRelativePath,
                    SidecarRelativePath=excluded.SidecarRelativePath,
                    LyricsSource=excluded.LyricsSource, Attribution=excluded.Attribution;
                DELETE FROM SourceAssociations WHERE LocalTrackId=$id;
                """;
            AddRecordParameters(command, record);
            command.ExecuteNonQuery();
        }
        foreach (var association in record.SourceAssociations)
        {
            if (duplicates.Contains((association.Source, association.SourceTrackId))) continue;
            using var command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO SourceAssociations(Source, SourceTrackId, LocalTrackId, SourceTitle,
                                               SourceArtist, SourceAlbum, DurationMs)
                VALUES($source, $sourceTrackId, $id, $title, $artist, $album, $duration);
                """;
            command.Parameters.AddWithValue("$source", association.Source);
            command.Parameters.AddWithValue("$sourceTrackId", association.SourceTrackId);
            command.Parameters.AddWithValue("$id", record.LocalTrackId);
            command.Parameters.AddWithValue("$title", association.Metadata.Title);
            command.Parameters.AddWithValue("$artist", association.Metadata.Artist);
            command.Parameters.AddWithValue("$album", (object?)association.Metadata.Album ?? DBNull.Value);
            command.Parameters.AddWithValue("$duration", association.Metadata.DurationMs);
            command.ExecuteNonQuery();
        }
    }

    private static LocalTrackRecord ReadBaseRecord(SqliteDataReader reader)
    {
        var id = reader.GetString(0);
        return new LocalTrackRecord(id,
            new UserTrackMetadata(reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)),
            reader.GetString(3), reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            Array.Empty<SourceTrackAssociation>());
    }

    private LocalTrackRecord? AddAssociations(LocalTrackRecord? record) => record is null
        ? null
        : record with { SourceAssociations = ReadAssociations(record.LocalTrackId) };

    private IReadOnlyList<SourceTrackAssociation> ReadAssociations(string id)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT Source, SourceTrackId, SourceTitle, SourceArtist, SourceAlbum, DurationMs
            FROM SourceAssociations WHERE LocalTrackId=$id ORDER BY Source, SourceTrackId;
            """;
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        var values = new List<SourceTrackAssociation>();
        while (reader.Read())
            values.Add(new(reader.GetString(0), reader.GetString(1),
                new(reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetInt64(5))));
        return values;
    }

    private static void AddRecordParameters(SqliteCommand command, LocalTrackRecord record)
    {
        command.Parameters.AddWithValue("$id", record.LocalTrackId);
        command.Parameters.AddWithValue("$userTitle", (object?)record.UserMetadata.Title ?? DBNull.Value);
        command.Parameters.AddWithValue("$userArtist", (object?)record.UserMetadata.Artist ?? DBNull.Value);
        command.Parameters.AddWithValue("$lyricsPath", record.LyricsRelativePath);
        command.Parameters.AddWithValue("$sidecarPath", record.SidecarRelativePath);
        command.Parameters.AddWithValue("$source", (object?)record.LyricsSource ?? DBNull.Value);
        command.Parameters.AddWithValue("$attribution", (object?)record.Attribution ?? DBNull.Value);
    }

    private void Delete(string id, SqliteTransaction transaction)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM LocalTracks WHERE LocalTrackId=$id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private void Execute(string sql, SqliteTransaction? transaction = null)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose() => _connection.Dispose();
}
