# Lyrics Displayer Architecture

## 1. Project Overview

Lyrics Displayer is a Windows desktop lyrics application built around a local-first lyrics library.

The initial playback source is YouTube Music running in Firefox.

A Firefox extension observes the active YouTube Music playback session and forwards playback state and available timed lyrics to the Windows side.

The long-term architecture must not couple the Windows application specifically to YouTube Music.

YouTube Music is the first playback source adapter, not the core of the application.

The application should eventually support:

* Desktop always-on-top lyrics
* Timed lyrics playback
* Local persistent LRC files
* User timing adjustments
* Built-in and external lyrics editing
* Multiple remote lyrics providers
* Local or network-hosted lyrics libraries
* Additional playback sources in the future

---

# 2. Repository Structure

```text
LyricsDisplayer/
├─ firefox-extension/
│
├─ windows/
│  ├─ LyricsDisplayer.App/
│  ├─ LyricsDisplayer.Core/
│  ├─ LyricsDisplayer.NativeHost/
│  │
│  ├─ Tests/
│  │  ├─ LyricsDisplayer.App.Tests/
│  │  ├─ LyricsDisplayer.Core.Tests/
│  │  └─ LyricsDisplayer.NativeHost.Tests/
│  │
│  └─ LyricsDisplayer.slnx
│
├─ docs/
│  ├─ ARCHITECTURE.md
│  └─ PROTOCOL.md
│
├─ scripts/
│  ├─ install-native-host.ps1
│  └─ uninstall-native-host.ps1
│
├─ README.md
└─ .gitignore
```

The Windows projects use .NET 10.

`LyricsDisplayer.App` is a WPF application.

---

# 3. Component Responsibilities

## 3.1 Firefox Extension

The Firefox extension is a playback source adapter.

Its responsibilities are:

* Observe YouTube Music playback.
* Detect the current track.
* Read track metadata.
* Read playback position.
* Detect play/pause state.
* Detect playback rate.
* Detect track/session changes.
* Obtain YouTube Music timed lyrics when available.
* Send authoritative playback snapshots to the native side.
* Manage ownership when multiple YouTube Music tabs exist.
* Produce extension diagnostic logging events.

The extension should NOT become the main lyrics engine.

It should not eventually be responsible for:

* LRCLib lookup
* Community lyrics providers
* Local lyrics storage
* Lyrics matching between providers
* Lyrics editing
* Local lyrics caching
* Timing adjustment persistence
* Desktop rendering

Those responsibilities belong to the Windows application.

The Firefox extension does not directly write files under `%LOCALAPPDATA%`.

Persistent extension logs are forwarded through Native Messaging and written by `LyricsDisplayer.NativeHost`.

Browser console logging remains available for development and as a fallback.

---

## 3.2 LyricsDisplayer.NativeHost

The NativeHost is a small transport bridge.

Its responsibilities are:

* Communicate with Firefox using Firefox Native Messaging.
* Receive versioned messages from the Firefox extension.
* Validate basic protocol framing/version information.
* Persist extension diagnostic log messages.
* Forward application messages to the Windows application using a Named Pipe.
* Maintain transport-related logging.
* Remain lightweight and contain no lyrics/business logic.

The NativeHost must never use stdout for ordinary logging because stdout belongs exclusively to the Firefox Native Messaging protocol.

Logging must use files and may additionally use stderr for development diagnostics.

### App Connection Behaviour

The Windows application is manually started initially.

If the Named Pipe server is unavailable, the NativeHost remains alive while the Firefox Native Messaging session remains active.

It should:

1. Attempt to connect to the Windows application.
2. If connection fails, log the failed attempt.
3. Wait approximately 5 seconds.
4. Retry.
5. Continue until:

   * the Windows application becomes available, or
   * Firefox closes the Native Messaging connection / stdin reaches EOF.

If an established Named Pipe connection is later lost, the NativeHost returns to the same retry loop.

The retry loop must not busy-wait.

Automatic application launching is intentionally NOT part of the initial implementation.

A future preference may allow Lyrics Displayer to launch automatically when required.

---

## 3.3 LyricsDisplayer.App

The WPF application is the user-facing Windows application.

Long-term responsibilities include:

* Maintaining playback state.
* Maintaining a local playback clock between browser snapshots.
* Loading and selecting lyrics.
* Managing the local lyrics library.
* Querying remote lyrics providers.
* Saving imported lyrics locally.
* Applying timing offsets.
* Rendering desktop lyrics.
* Providing lyrics editing functionality.
* Managing application preferences.
* Maintaining application logging.

For Milestone 1, the application is only a diagnostic receiver.

It receives messages from the NativeHost and displays the received data.

---

## 3.4 LyricsDisplayer.Core

Core contains application-domain types and logic that are not tied to WPF, Firefox or a particular transport.

Examples of future domain types include:

* Track
* TrackIdentity
* PlaybackState
* LyricsDocument
* TimedLyrics
* LyricsLine
* LyricsSource
* LyricsAssociation

UI-specific code must not be placed in Core.

Firefox Native Messaging-specific code must not be placed in Core.

Do not introduce abstractions simply because they may theoretically be useful later.

---

# 4. Playback State Model

The Firefox extension sends snapshots of the current authoritative playback state.

The system prefers snapshots over command-style messages.

For example:

```text
Current track = X
Position = 52.381 seconds
Playing = true
Playback rate = 1.0
```

is preferred over maintaining a required command history such as:

```text
User clicked play
User clicked next
User seeked forward
```

Snapshots allow the Windows application to recover after:

* reconnecting
* restarting
* temporary transport interruption
* delayed messages

Future event messages may exist for latency-sensitive situations, but playback snapshots remain authoritative.

---

# 5. Playback Synchronisation

The intended default snapshot interval is approximately:

```text
500 ms
```

This should eventually be configurable in preferences.

Configuration is not required in early milestones.

The Windows application must not render lyrics only when a new snapshot arrives.

Instead:

1. Receive authoritative browser playback position.
2. Synchronise a local high-resolution playback clock.
3. Interpolate locally between browser snapshots.
4. Correct drift whenever a new authoritative snapshot arrives.

Example:

```text
Firefox snapshot: 52.000s

Windows:
52.016
52.032
52.048
...
52.496

Firefox snapshot: 52.502s

Windows clock is corrected.
```

This allows smooth rendering without browser-to-native communication at rendering frame rate.

---

# 6. Multiple YouTube Music Tabs

YouTube Music tabs use first-in-first-served session ownership.

The first eligible tab that begins playback claims ownership.

Once a tab owns the playback session:

* Other YouTube Music tabs are ignored.
* Pausing does not release ownership.
* Changing tracks does not release ownership.
* Another tab beginning playback does not steal ownership.
* An inactive owner remains the owner until its source session ends.

Ownership is released when the owning source session ends, for example when:

* the tab is closed
* the tab navigates away from YouTube Music
* the extension determines that the relevant source session has been destroyed

After ownership is released, the next eligible tab that begins playback may claim ownership.

This arbitration belongs entirely to the Firefox extension.

The Windows application receives only one authoritative playback source and should not need to understand browser tab arbitration.

---

# 7. Local-First Lyrics Philosophy

Local lyrics are a first-class persistent asset.

They are NOT merely a temporary cache.

Remote lyrics services are import sources.

Conceptually:

```text
Local lyrics available?
    │
    ├─ Yes → use local copy
    │
    └─ No
         ↓
     Remote providers
         ↓
     Select/import lyrics
         ↓
     Save locally
         ↓
     Use local copy
```

Possible remote providers eventually include:

* YouTube Music official timed lyrics
* LRCLib
* Community-hosted lyrics services
* Other providers added later

Once a local copy has been created, user edits must never be silently overwritten by a remote provider.

---

# 8. Lyrics Files

The intended primary editable lyrics format is standard UTF-8 `.lrc`.

Lyrics files should remain normal text files that can be opened using external tools.

The application must not require a proprietary lyrics format for ordinary timed lyrics.

Future application-specific metadata may be stored in a portable sidecar file alongside the LRC file.

Example:

```text
song.lrc
song.lyrics.json
```

Potential portable metadata includes:

* local track identity
* playback-source associations
* provider identifiers
* source information
* global timing offset
* user-modified state
* source version or hash information

The exact sidecar schema will be designed in a later milestone.

---

# 9. Local Application Storage

Machine-local application data belongs under:

```text
%LOCALAPPDATA%\LyricsDisplayer\
```

The intended structure is conceptually:

```text
%LOCALAPPDATA%\LyricsDisplayer\
├─ Logs\
│  ├─ FirefoxExtension\
│  ├─ NativeHost\
│  └─ App\
├─ Cache\
├─ Runtime\
├─ settings.json
└─ library-index.db
```

Not all files or directories need to exist in Milestone 1.

---

# 10. Configurable Lyrics Library

The lyrics library path must eventually be configurable.

Default:

```text
%LOCALAPPDATA%\LyricsDisplayer\Lyrics\
```

The architecture must also support a network path such as:

```text
\\NAS\Media\Lyrics\
```

This allows the same lyrics library to be used across multiple computers through SMB or another filesystem-accessible share.

Portable lyrics assets and portable metadata may live on the network share.

Machine-specific runtime state should remain under local application data.

---

# 11. Local Index / SQLite

If SQLite is introduced, it should be treated as a local searchable index/cache rather than the sole authoritative representation of the lyrics library.

The database should remain under:

```text
%LOCALAPPDATA%\LyricsDisplayer\
```

Do not use a SQLite database directly hosted on SMB as the normal multi-machine sharing mechanism.

Ideally, the application should eventually be capable of:

```text
Delete local index
        ↓
Scan configured lyrics library
        ↓
Rebuild local index
```

The portable files are authoritative.

The local index is disposable.

---

# 12. Lyrics Timing Adjustment

A future lyrics timing adjustment feature will support operations such as:

* -0.5 seconds
* -0.1 seconds
* +0.1 seconds
* +0.5 seconds
* reset

Timing adjustment should initially be non-destructive.

Example:

```text
GlobalOffsetMs = -500
```

The original LRC timestamps remain unchanged while rendering applies the offset.

A separate explicit operation may later bake the offset into the LRC timestamps.

Silent destructive rewriting is not acceptable.

A portable timing offset should be stored with the lyrics-library metadata so that the same correction can follow the lyrics across machines.

---

# 13. Editing

Future editing features may include:

* Built-in timed lyrics editor
* Edit current line text
* Set current line timestamp from current playback position
* Bulk timing adjustment
* Open the current LRC in an external editor
* Detect external file changes and reload

These are deliberately later milestones.

---

# 14. Logging Architecture

Logging exists from Milestone 1.

Logs are separated by component:

```text
%LOCALAPPDATA%\LyricsDisplayer\Logs\
├─ FirefoxExtension\
├─ NativeHost\
└─ App\
```

Each component uses an active log named:

```text
current.logs
```

## 14.1 Rotation Rules

Logs use a hybrid rotation strategy.

A log is rotated when either:

1. The local system date passes midnight while the component is running.
2. A new component/process/session starts and an existing `current.logs` file from the previous session exists.

The old `current.logs` file is renamed using the local date associated with that log.

Example:

```text
2026-09-09.logs
```

If that filename already exists:

```text
2026-09-09-(1).logs
2026-09-09-(2).logs
2026-09-09-(3).logs
```

and so on.

The next available index is used.

After rotation, a new:

```text
current.logs
```

is created.

Rotation is based on local system time, not UTC.

Log entries themselves should use an unambiguous ISO-8601 timestamp including the local UTC offset.

Example:

```text
2026-09-09T23:58:42.381+01:00
```

## 14.2 Session Startup Rotation

On startup:

* If `current.logs` does not exist, create it.
* If it exists but is empty, it may be reused.
* If it contains logs from a previous session, rotate it before writing the new session.

For the Firefox extension, a new extension/background source session is treated as a new logging session.

For the NativeHost, a new process is treated as a new logging session.

For the WPF application, a new application process is treated as a new logging session.

## 14.3 Midnight Rotation

While a component is running, the logger must detect when the local calendar date has changed.

Before writing the first entry belonging to the new date:

1. Close/flush the current log.
2. Rotate the previous `current.logs`.
3. Create a fresh `current.logs`.
4. Continue logging.

Exact execution at precisely `00:00:00.000` is not required.

The requirement is that entries belonging to the new local date do not continue indefinitely in the previous day's active log.

## 14.4 Retention

Rotated logs are retained for a maximum of 30 days.

Cleanup should occur at least:

* on logger/component startup
* after a rotation

Logs older than the retention period should be deleted automatically.

`current.logs` is not deleted by retention cleanup.

Retention cleanup should only affect recognised Lyrics Displayer rotated log files in the relevant component log directory.

## 14.5 Firefox Extension Logging

The Firefox extension should continue to log useful information to the Firefox developer console.

For persistent logging, the extension sends diagnostic log messages through Native Messaging.

`LyricsDisplayer.NativeHost` writes those entries to:

```text
%LOCALAPPDATA%\LyricsDisplayer\Logs\FirefoxExtension\
```

The extension must not directly access the Windows filesystem.

If the native connection is unavailable, console logging remains the fallback.

## 14.6 NativeHost Logging

NativeHost logs belong under:

```text
%LOCALAPPDATA%\LyricsDisplayer\Logs\NativeHost\
```

Useful entries include:

* process startup
* protocol messages received
* protocol validation failure
* Named Pipe connection attempts
* Named Pipe connection failures
* retry attempts
* Named Pipe connection success
* Pipe disconnection
* forwarded message information
* Firefox EOF/disconnect
* graceful shutdown

Every failed Named Pipe retry attempt may be logged.

The expected retry frequency is approximately once every 5 seconds, so this logging rate is acceptable.

NativeHost stdout must never contain normal log text.

## 14.7 Windows App Logging

Application logs belong under:

```text
%LOCALAPPDATA%\LyricsDisplayer\Logs\App\
```

Useful entries include:

* application startup
* Named Pipe server startup
* NativeHost connection
* NativeHost disconnection
* protocol validation failure
* stale sequence rejection
* application shutdown

Avoid repeatedly writing full lyrics payloads to logs during normal operation.

---

# 15. Automatic App Launch

Initial behaviour:

```text
Auto-launch Lyrics Displayer = disabled
```

The user manually starts the WPF application.

A future preference may enable automatic launching when the Firefox source needs the application.

Automatic launching must remain optional.

The architecture must not assume that auto-launch is always enabled.

---

# 16. Milestones

## Milestone 1 — Transport Proof

Goal:

Prove that Firefox can reliably communicate synthetic playback data to a manually running WPF application.

Communication path:

```text
Firefox Extension
    ↓ Native Messaging
NativeHost
    ↓ Named Pipe
WPF App
```

Requirements:

* Versioned protocol from the first message.
* Firefox extension generates synthetic playback snapshots.
* Snapshot interval approximately 500 ms.
* Synthetic values include track metadata, playback state and timed lyrics.
* NativeHost receives Firefox Native Messaging messages.
* NativeHost forwards application messages to the WPF application.
* NativeHost persists extension diagnostic logs.
* NativeHost retries the Named Pipe connection approximately every 5 seconds while Firefox remains connected.
* Each failed retry is logged.
* WPF app is manually started and hosts the Named Pipe server.
* WPF app displays parsed values and raw JSON.
* Hybrid log rotation is implemented.
* Logs rotate on midnight and component restart/session restart.
* Logs are retained for 30 days.
* No auto-launch.
* No real YouTube Music integration.
* No lyrics storage.
* No SQLite.
* No desktop overlay.
* No playback interpolation.
* No external lyrics providers.

Success criterion:

Changing/generated data in the Firefox extension is visibly reflected in the running WPF application.

The user must also be able to:

1. Start Firefox/extension before the WPF app.
2. Observe NativeHost retries.
3. Start the WPF app later.
4. See the next retry successfully connect without restarting Firefox.

---

## Milestone 2 — Real YouTube Music Track Detection

Detect:

* track/video ID
* title
* artist
* album when available
* duration

No real lyrics required yet.

---

## Milestone 3 — Playback Synchronisation

Add:

* current position
* play/pause
* seek
* playback rate
* track changes
* local Windows playback-clock interpolation
* snapshot correction

---

## Milestone 4 — YouTube Music Timed Lyrics

Read YouTube Music's own timed lyrics when available.

No remote fallback providers yet.

---

## Milestone 5 — Local Lyrics Library

Introduce:

* local-first loading
* LRC persistence
* portable metadata
* configurable lyrics-library path
* SMB-compatible design
* local disposable index if required

---

## Milestone 6 — Lyrics Timeline Engine

Select the correct lyrics line based on local playback time.

---

## Milestone 7 — Desktop Overlay

Add:

* transparent window
* always-on-top
* current/next line display
* positioning and sizing

---

## Milestone 8 — Timing Adjustment

Add:

* ±0.1 second
* ±0.5 second
* persistent global offset
* explicit bake-to-file operation

---

## Milestone 9 — Overlay Interaction

Add:

* lock/unlock
* click-through
* global shortcuts
* preferences

---

## Milestone 10 — External Editing

Add:

* open LRC externally
* detect external changes
* reload safely

---

## Milestone 11 — Built-in Lyrics Editor

Add interactive lyrics editing and timestamp authoring.

---

## Milestone 12 — Additional Lyrics Providers

Add providers such as:

* LRCLib
* community lyrics sources

Remote providers remain importers into the local-first library.

---

## Milestone 13 — Polish

Potential work includes:

* karaoke-style animation
* advanced visual preferences
* installer
* optional auto-launch
* provider management
* additional playback-source adapters
