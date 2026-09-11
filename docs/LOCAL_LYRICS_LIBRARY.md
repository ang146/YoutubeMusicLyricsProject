# Local Lyrics Library

Milestone 5 makes Lyrics Displayer local-first. Standard LRC files and their sidecars are the portable authority; SQLite is a disposable, machine-local lookup index.

## Locations and configuration

The default portable library is:

```text
%LOCALAPPDATA%\LyricsDisplayer\Lyrics
```

An absolute local or UNC path can be selected in the machine-local file `%LOCALAPPDATA%\LyricsDisplayer\settings.json`:

```json
{
  "lyricsLibraryPath": "\\\\NAS\\Media\\Lyrics"
}
```

A missing, null, or blank property uses the default. A malformed or relative value is logged and also uses the default. A valid configured path that is unavailable is different: the App reports the library unavailable and does not silently create or use a second default library. Playback and runtime YouTube Music lyrics continue, but imports are not persisted until that configured library is available.

The index always remains local, even when the portable library is on SMB/NAS:

```text
%LOCALAPPDATA%\LyricsDisplayer\library-index.db
```

Normal filesystem access is used for UNC paths. Lyrics Displayer does not implement SMB or put SQLite on the share.

## Portable directory layout

```text
Lyrics\
└─ tracks\
   └─ <LocalTrackId>\
      ├─ track.lrc
      └─ track.lyrics.json
```

`LocalTrackId` is a generated UUID in `D` format. It is stable and deliberately independent of a title, filename, or provider ID. Provider identities are associations, such as `youtubeMusic:P4SDPyGfxho`; the sidecar model permits multiple associations for one local track.

## Sidecar schema version 1

```json
{
  "schemaVersion": 1,
  "localTrackId": "8a963ffc-49eb-478e-a2ee-bff9e6e765cc",
  "sourceAssociations": [
    {
      "source": "youtubeMusic",
      "sourceTrackId": "P4SDPyGfxho",
      "metadata": {
        "title": "Source title",
        "artist": "Source artist",
        "album": null,
        "durationMs": 244000
      }
    }
  ],
  "userMetadata": {
    "title": null,
    "artist": null
  },
  "lyrics": {
    "file": "track.lrc",
    "source": "youtubeMusic",
    "attribution": "Musixmatch",
    "importedAtUtc": "2026-09-12T00:00:00+00:00"
  }
}
```

Source metadata records what the playback source supplied. `album` remains `null` when no reliable album value exists. User title and artist values are portable overrides; blank overrides normalize to `null`. Display metadata is resolved independently for each field in this order:

```text
user override -> current live source value -> stored source value
```

Editing the sidecar while the App is stopped takes effect during the next startup scan. Future sidecar schema versions are not assumed compatible and are left untouched.

## LRC format

Imported timed lyrics are UTF-8 without a byte-order mark and use one standard millisecond timestamp per line:

```text
[00:12.340]A lyric line
```

The reader accepts both `[mm:ss.ff]` and `[mm:ss.fff]`, ignores common `ar`, `ti`, `al`, `by`, `offset`, `re`, `ve`, and `length` metadata tags, expands multiple timestamps on one physical line, and isolates malformed lines when other usable lines remain. It does not perform Chinese-script conversion.

LRC stores start times only. At runtime, each line ends at the next line's start. The final line ends at a reliable track duration when that duration is later than the final start; otherwise its end equals its start. Timeline highlighting remains outside Milestone 5.

## SQLite index

Lyrics Displayer uses `Microsoft.Data.Sqlite` directly. `PRAGMA user_version` is `1`, foreign-key enforcement is enabled, and the schema is:

```text
LocalTracks
  LocalTrackId TEXT PRIMARY KEY
  UserTitle TEXT NULL
  UserArtist TEXT NULL
  LyricsRelativePath TEXT NOT NULL
  SidecarRelativePath TEXT NOT NULL UNIQUE
  LyricsSource TEXT NULL
  Attribution TEXT NULL

SourceAssociations
  Source TEXT
  SourceTrackId TEXT
  LocalTrackId TEXT -> LocalTracks(LocalTrackId) ON DELETE CASCADE
  SourceTitle TEXT
  SourceArtist TEXT
  SourceAlbum TEXT NULL
  DurationMs INTEGER
  PRIMARY KEY (Source, SourceTrackId)

Indexes
  IX_SourceAssociations_LocalTrackId
  IX_SourceAssociations_TitleArtist
```

The repository supports lookup by `(source, sourceTrackId)`, lookup by `LocalTrackId`, and effective title/artist search using SQL `COALESCE`. Lyrics text is not duplicated into SQLite.

At startup, the App opens the local index, scans portable track directories, validates sidecars and LRC files, and synchronizes both tables in a transaction. Sidecar values replace stale indexed metadata. A completed scan prunes rows whose authoritative track directory is genuinely gone; an unavailable library or failed scan never means “delete everything.”

Deleting `library-index.db` is safe. The next startup creates schema version 1 and rebuilds associations from portable sidecars. If SQLite reports a corrupt database during opening, the unusable file is preserved with a `.corrupt-<timestamp>-<id>` suffix and a new index is built. Portable files are not modified during either operation.

## Local-first and import behavior

When the authoritative track changes, the App queries SQLite by source association and then reads the authoritative sidecar and LRC. A valid local result is displayed immediately. A later Firefox `lyricsSnapshot` may still arrive, but it cannot replace the local text, timing, metadata, provider, or attribution.

If no association exists, the first remote result with `available=true`, `timed=true`, and at least one line is imported:

1. Generate a UUID `LocalTrackId`.
2. Create a uniquely named temporary track directory.
3. Write and flush the LRC and sidecar through unique same-directory temporary files.
4. Move the complete directory to its final UUID name without overwrite.
5. Insert the track and associations into SQLite in one transaction.
6. Reload and use the new local document.

Files are committed before the index because they are the authority. If indexing fails afterward, the files survive and a later scan can recover them. Untimed or unavailable remote results create no directory, empty LRC, fake timestamps, or permanent negative-cache record.

## Conflicts and damaged records

Invalid JSON, unsupported schemas, mismatched IDs, missing/unusable LRC files, and duplicate associations are logged and do not stop other valid tracks from indexing. User files are never automatically deleted. When a damaged sidecar still exposes a source association, that identity is retained as a conservative block against automatic re-import.

If two valid sidecars claim the same source identity, neither is selected by directory enumeration order. The conflict is reported, the association is excluded from the index, both portable records remain untouched, and automatic import for that identity is blocked. SQLite's primary key supplies an additional uniqueness guard.

There is no automatic refresh or overwrite operation in Milestone 5. Fix questionable portable files manually while the App is stopped, or remove the complete intended track directory yourself if a fresh automatic import is desired.
