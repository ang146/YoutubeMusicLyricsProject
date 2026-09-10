# Lyrics Displayer Protocol

## 1. Purpose

This document defines communication between:

```text
Firefox Extension
        ↓
LyricsDisplayer.NativeHost
        ↓
LyricsDisplayer.App
```

The protocol is versioned from the first implementation.

Milestone 4 adds track-oriented YouTube Music lyrics snapshots to real playback data and local Windows interpolation.

---

# 2. Protocol Versioning

Every protocol message must contain:

```json
{
  "protocolVersion": 1
}
```

`protocolVersion` represents the major compatibility version.

Breaking protocol changes require incrementing this value.

Additive optional fields that older implementations can safely ignore do not necessarily require a version increment.

For Milestone 1, implementations may require:

```text
protocolVersion == 1
```

Unsupported versions must be logged and rejected rather than silently interpreted incorrectly.

---

# 3. Message Envelope

Extension messages use a common envelope.

Example:

```json
{
  "protocolVersion": 1,
  "messageType": "playbackSnapshot",
  "source": "youtubeMusic",
  "sourceSessionId": "550e8400-e29b-41d4-a716-446655440000",
  "sequence": 42,
  "sentAtUtc": "2026-09-09T05:30:00.123Z",
  "payload": {}
}
```

## `protocolVersion`

Integer protocol compatibility version.

## `messageType`

Current message types:

```text
playbackSnapshot
lyricsSnapshot
diagnosticLog
```

## `source`

Playback-source adapter.

Initial value:

```text
youtubeMusic
```

Synthetic Milestone 1 playback messages may use this value because they simulate the future YouTube Music adapter.

## `sourceSessionId`

UUID identifying the current extension/source session.

It remains stable for the lifetime of one source ownership session.

A new session receives a new UUID.

## `sequence`

Monotonically increasing integer within one `sourceSessionId`.

Sequence numbers apply to messages produced within the source session.

Receivers may use them to identify stale or out-of-order state.

Gaps are valid.

## `sentAtUtc`

UTC timestamp indicating when the extension created the message.

This is diagnostic metadata.

It must not replace the authoritative playback position.

## `payload`

Message-specific content.

---

# 4. Playback Snapshot

The example below is the historical Milestone 1 synthetic payload. Milestone 4 senders keep the required `lyrics` member as a compatibility placeholder (`available=false`, `timed=false`, `source=null`, `lines=[]`). This placeholder is not authoritative lyrics state. Full lyrics and their status belong exclusively to `lyricsSnapshot`; periodic playback never clears or overwrites the current lyrics result for the same track.

Example:

```json
{
  "protocolVersion": 1,
  "messageType": "playbackSnapshot",
  "source": "youtubeMusic",
  "sourceSessionId": "550e8400-e29b-41d4-a716-446655440000",
  "sequence": 42,
  "sentAtUtc": "2026-09-09T05:30:00.123Z",
  "payload": {
    "track": {
      "sourceTrackId": "fake-video-7421",
      "title": "Synthetic Song 7421",
      "artist": "Synthetic Artist 18",
      "album": "Synthetic Album 4",
      "durationMs": 243520
    },
    "playback": {
      "positionMs": 53420,
      "playing": true,
      "playbackRate": 1.0
    },
    "lyrics": {
      "available": true,
      "timed": true,
      "source": "youtubeMusic",
      "lines": [
        {
          "startMs": 50000,
          "endMs": 55000,
          "text": "This is a synthetic lyric line."
        },
        {
          "startMs": 55000,
          "endMs": 60000,
          "text": "This is another synthetic lyric line."
        }
      ]
    }
  }
}
```

---

# 5. Track

```json
{
  "sourceTrackId": "fake-video-7421",
  "title": "Synthetic Song 7421",
  "artist": "Synthetic Artist 18",
  "album": "Synthetic Album 4",
  "durationMs": 243520
}
```

## `sourceTrackId`

Provider-specific track identity.

For real YouTube Music integration this will normally correspond to the relevant YouTube/YouTube Music identifier.

It must not be treated as the future universal local lyrics identity.

## `title`

Display title.

## `artist`

Display artist.

## `album`

Display album when a reliable album value is available.

The field may contain a string, an empty string, or `null`. Senders should use `null` when no reliable album value can be identified. Receivers must not interpret unrelated byline metadata, such as view counts, as an album.

## `durationMs`

Track duration in integer milliseconds.

---

# 6. Playback State

```json
{
  "positionMs": 53420,
  "playing": true,
  "playbackRate": 1.0
}
```

All playback timeline values use integer milliseconds.

## `positionMs`

Authoritative playback position from the playback source when the snapshot was created.

## `playing`

Current playback state.

## `playbackRate`

Playback speed.

Normal playback:

```text
1.0
```

---

# 7. Lyrics

Timed lyrics example:

```json
{
  "available": true,
  "timed": true,
  "source": "youtubeMusic",
  "lines": [
    {
      "startMs": 50000,
      "endMs": 55000,
      "text": "First line"
    },
    {
      "startMs": 55000,
      "endMs": 60000,
      "text": "Second line"
    }
  ]
}
```

Unavailable lyrics:

```json
{
  "available": false,
  "timed": false,
  "source": null,
  "lines": []
}
```

Untimed lyrics are not sufficient for the intended automatic karaoke/timeline display.

Future versions may carry untimed lyrics for editing or alternate display modes.

---

# 8. Lyrics Line

```json
{
  "startMs": 50000,
  "endMs": 55000,
  "text": "Example lyric"
}
```

Timestamps use integer milliseconds.

This avoids unnecessary floating-point ambiguity across transport, persistence and timing adjustment.

---

# 9. Diagnostic Log Message

The Firefox extension may send persistent diagnostic logging events to the NativeHost.

Example:

```json
{
  "protocolVersion": 1,
  "messageType": "diagnosticLog",
  "source": "youtubeMusic",
  "sourceSessionId": "550e8400-e29b-41d4-a716-446655440000",
  "sequence": 43,
  "sentAtUtc": "2026-09-09T05:30:00.250Z",
  "payload": {
    "level": "Information",
    "category": "PlaybackSource",
    "message": "Synthetic playback session started."
  }
}
```

Supported initial levels:

```text
Debug
Information
Warning
Error
```

The NativeHost persists these messages under:

```text
%LOCALAPPDATA%\LyricsDisplayer\Logs\FirefoxExtension\
```

A diagnostic-log message does NOT need to be forwarded to the WPF application.

Logging transport must not interfere with playback-state forwarding.

---

# 10. Snapshot Frequency

The extension sends periodic playback snapshots approximately every:

```text
500 ms
```

This is fixed for Milestone 3.

The extension also sends an immediate authoritative playback snapshot, where reliable media events permit it, after play/resume, pause, seek completion, playback-rate change, and current-track change. Closely duplicated events may be coalesced. These snapshots use the same `playbackSnapshot` message type and sequence rules as periodic snapshots.

Future preferences may make the interval configurable.

The Windows application must not assume snapshots arrive at exact intervals.

Transport and process-scheduling delays are expected.

---

# 11. Firefox Native Messaging Transport

Firefox communicates with `LyricsDisplayer.NativeHost` using Firefox Native Messaging.

Native Messaging framing must follow Firefox's required framing protocol.

Critical rule:

```text
stdout = Native Messaging protocol only
```

The NativeHost must not write arbitrary debug or logging text to stdout.

---

# 12. NativeHost to Windows App Transport

The NativeHost forwards application protocol messages to the WPF application using a Windows Named Pipe.

Milestone 1 pipe name:

```text
LyricsDisplayer.NativeHost.v1
```

The WPF application acts as the Named Pipe server.

The NativeHost acts as the Named Pipe client.

Messages on the Named Pipe are UTF-8 newline-delimited JSON.

Each message is serialised as one compact JSON line:

```text
<JSON>\n
```

JSON newline characters inside lyric text are escaped by the JSON serializer and therefore do not terminate framing.

`diagnosticLog` messages intended only for NativeHost persistence do not need to be forwarded over the pipe.

---

# 13. NativeHost Retry Behaviour

If the WPF application is not running or the Named Pipe is unavailable:

```text
attempt connection
        ↓
failure
        ↓
log failure
        ↓
wait ~5 seconds
        ↓
retry
```

The NativeHost continues retrying while the Firefox Native Messaging session remains alive.

Each failed retry attempt may be logged.

Expected retry frequency:

```text
approximately once every 5 seconds
```

This must use an asynchronous wait or equivalent blocking mechanism.

It must not busy-loop.

If a previously connected Named Pipe disconnects, the NativeHost returns to the retry loop.

The NativeHost exits normally when Firefox closes the Native Messaging connection or stdin reaches EOF.

No automatic WPF application launching is implemented in Milestone 1.

---

# 14. Logging File Layout

Persistent logs use:

```text
%LOCALAPPDATA%\LyricsDisplayer\Logs\
├─ FirefoxExtension\
│  └─ current.logs
├─ NativeHost\
│  └─ current.logs
└─ App\
   └─ current.logs
```

---

# 15. Log Rotation

Logs use both date-based and session-based rotation.

## Midnight Rotation

When the local calendar date changes, the active `current.logs` is rotated before new-date log entries are written.

Example:

```text
current.logs
↓
2026-09-09.logs
```

A new:

```text
current.logs
```

is then created.

## Session/Process Rotation

When a component starts and discovers a non-empty `current.logs` left by a previous session, it rotates that file before beginning the new session.

The date used should correspond to the previous log file's local date, based on the previous log content or filesystem timestamp as appropriate.

## Duplicate Filenames

If:

```text
2026-09-09.logs
```

already exists, use:

```text
2026-09-09-(1).logs
```

then:

```text
2026-09-09-(2).logs
2026-09-09-(3).logs
```

and so on.

Never overwrite an existing rotated log file.

---

# 16. Log Retention

Rotated logs are retained for no more than 30 days.

Cleanup should occur:

* when the relevant logger/component starts
* after log rotation

Only recognised Lyrics Displayer rotated log files in that component's log directory may be removed.

Do not recursively delete arbitrary files.

Do not delete the currently active:

```text
current.logs
```

---

# 17. Log Entry Format

Log lines should be human-readable.

An acceptable form is:

```text
2026-09-09T21:32:15.381+01:00 [Information] [NamedPipe] Connected to LyricsDisplayer.NativeHost.v1
```

Timestamps should use ISO-8601 and include the current local UTC offset.

Rotation is based on the local system calendar date.

---

# 18. Milestone 1 Synthetic Behaviour

The Firefox extension does not access YouTube Music yet.

It generates synthetic playback data.

Recommended behaviour:

1. Create one `sourceSessionId` when the synthetic session starts.
2. Send a playback snapshot approximately every 500 ms.
3. Increment `sequence` for messages.
4. Maintain a synthetic playback position.
5. While `playing == true`, advance position naturally.
6. Periodically change the synthetic track.
7. Generate synthetic timed lyric lines.
8. Use changing/random identifiers, titles and artists so transport updates are visually obvious.
9. Generate useful extension diagnostic messages at lifecycle events.
10. Do not flood persistent logs with one entry for every 500 ms playback snapshot unless explicitly running in verbose/debug mode.

The synthetic generator is test scaffolding and should be straightforward to replace in Milestone 2.

---

# 19. Milestone 1 App Display

The WPF diagnostic interface should display at least:

```text
Pipe Status
Source Session ID
Sequence
Last Message Time

Track ID
Title
Artist
Album
Duration

Position
Playing
Playback Rate

Lyrics Available
Lyrics Timed
Lyrics Lines

Raw JSON
```

No visual polish is required.

Correct communication is the goal.

---

# 20. Authority Rules

For one active `sourceSessionId`:

* Newer playback-state sequence numbers supersede older playback-state sequence numbers.
* Duplicate playback-state sequence numbers may be ignored.
* Lower playback-state sequence numbers must not replace newer state.
* Sequence gaps are valid.
* A new `sourceSessionId` resets sequence tracking for that source session.

Playback snapshots are authoritative browser state.

The Windows application interpolates locally between accepted snapshots using monotonic elapsed time and must correct itself using each subsequent accepted authoritative snapshot.

Duplicate and stale snapshots do not rebase the local clock. When the Named Pipe source connection is lost, the application freezes its current local estimate until a newly accepted snapshot rebases it after reconnection.

---

# 21. Lyrics Snapshot (Milestone 4)

`lyricsSnapshot` is an additive message under protocol version 1. Deploy the updated extension, NativeHost and App together: older receivers reject this unknown message type, but existing playback message fields and semantics remain supported.

```json
{
  "protocolVersion": 1,
  "messageType": "lyricsSnapshot",
  "source": "youtubeMusic",
  "sourceSessionId": "550e8400-e29b-41d4-a716-446655440000",
  "sequence": 812,
  "sentAtUtc": "2026-09-09T15:30:00.123Z",
  "payload": {
    "sourceTrackId": "abcdefghijk",
    "available": true,
    "timed": true,
    "source": "youtubeMusic",
    "attribution": null,
    "lines": [
      { "startMs": 9200, "endMs": 10630, "text": "Example line" }
    ]
  }
}
```

The required payload fields are `sourceTrackId`, `available`, `timed`, `source`, and `lines`. `attribution` is an optional string or null, preserving the backend's human-readable attribution. No browser credentials, visitor data, API keys, or browse IDs cross this boundary.

| Result | available | timed | lines |
| --- | --- | --- | --- |
| Line-timed lyrics | true | true | Non-empty timed lines |
| Lyrics without usable timestamps | true | false | Empty |
| Confirmed no lyrics | false | false | Empty |

`source` is `youtubeMusic` for available lyrics and null for unavailable lyrics. Every timed line contains non-negative integer `startMs` and `endMs`, with end not before start. Lines are ordered by start time; overlapping/equal-start cues and empty string text are permitted. Text is preserved, including Unicode, punctuation and newlines. The adapter skips malformed individual cues with a count-only warning and never synthesises timestamps. A completely unparseable result is a lookup failure, not evidence of no lyrics.

While lookup is pending or fails, no result is published. The App represents this as unknown, separately from confirmed unavailable. Failures are logged without responses or credentials and are not cached as no-lyrics results. A later visit to the track or owner content-script reload can retry. Playback continues independently.

## Authority and lifecycle

- Only an accepted playback snapshot establishes the current source session and track. Changing either clears current lyrics immediately.
- Lyrics must match the current envelope source, source session, and track ID. They cannot establish a new playback session or rebase the playback clock.
- Lyrics have their own sequence high-water mark. Duplicates/lower lyrics sequences are rejected; gaps from playback and diagnostics are valid. A lyrics result may predate the latest periodic playback snapshot.
- After a track transition observed within a session, lyrics must not predate that transition's playback sequence. This also rejects results from an earlier A visit after A → B → A.
- On an App's first snapshot or a newly observed session, retained lyrics may predate the latest playback snapshot, provided session/track match. This permits reconnect replay.
- The background verifies the current owner/session/track and request generation before publishing, even after cancellation. Only confirmed results are cached by `sourceTrackId` for the extension lifetime.

## Delivery and reconnect

The extension sends full lines once on lookup completion or a cache hit for a newly active track, and replays them after the next playback snapshot following a Native Messaging reconnect. It does not retransmit full lyrics every 500 ms.

NativeHost retains one compact message for each application message type in memory. Pending playback replaces older pending playback, but cannot evict a pending lyrics result. On pipe connection/reconnection it replays retained playback first, then retained lyrics. Between reconnects only changed message types are forwarded. The App rejects a retained lyrics result if its session/track is no longer current. NativeHost performs no YouTube API calls, lyric timing processing, or disk lyrics storage.
