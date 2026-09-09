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

Milestone 1 uses synthetic playback data only.

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

Milestone 1 defines:

```text
playbackSnapshot
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

Milestone 1 sends playback snapshots approximately every:

```text
500 ms
```

This is fixed for Milestone 1.

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

The Windows application may eventually interpolate locally between snapshots but must correct itself using subsequent authoritative snapshots.

Playback interpolation itself is NOT implemented in Milestone 1.
