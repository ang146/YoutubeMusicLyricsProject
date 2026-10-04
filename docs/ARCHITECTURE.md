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
* User-correctable track metadata
* Manual lyrics searching
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
│  ├─ PROTOCOL.md
│  ├─ YOUTUBE_MUSIC_LYRICS.md
│  ├─ LOCAL_LYRICS_LIBRARY.md
│  ├─ LYRICS_TIMELINE.md
│  ├─ DESKTOP_LYRICS_OVERLAY.md
│  ├─ LYRICS_TIMING_ADJUSTMENT.md
│  ├─ OVERLAY_INTERACTION.md
│  ├─ EXTERNAL_EDITING.md
│  ├─ BUILT_IN_EDITOR.md
│  └─ FUTURE_FEATURES.md
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
* User metadata overrides
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
* Managing source metadata and user metadata overrides.
* Maintaining the local SQLite library index.
* Querying remote lyrics providers.
* Performing automatic and manual lyrics searches.
* Saving imported lyrics locally.
* Applying timing offsets.
* Rendering desktop lyrics.
* Providing lyrics editing functionality.
* Managing application preferences.
* Maintaining application logging.

Through Milestone 11, the application still retains its legacy diagnostic/control-panel MainWindow but now owns the complete local-first authoring path: local persistence and SQLite indexing, timeline/overlay presentation, timing adjustment and safe bake, overlay interaction, external LRC reload, and an accepted built-in editor with structured LRC round-tripping, metadata overrides, multi-timestamp authoring, advisory validation, undo/redo, flexible timestamp input, and command-based cell/row editing. Storage, indexing, timeline selection, editor state, overlay presentation mapping, interaction state, settings parsing, geometry validation, and file-change coordination live outside WPF visual code. The implemented formats and rules are documented in [LOCAL_LYRICS_LIBRARY.md](LOCAL_LYRICS_LIBRARY.md), [LYRICS_TIMELINE.md](LYRICS_TIMELINE.md), [DESKTOP_LYRICS_OVERLAY.md](DESKTOP_LYRICS_OVERLAY.md), [LYRICS_TIMING_ADJUSTMENT.md](LYRICS_TIMING_ADJUSTMENT.md), [OVERLAY_INTERACTION.md](OVERLAY_INTERACTION.md), [EXTERNAL_EDITING.md](EXTERNAL_EDITING.md), and [BUILT_IN_EDITOR.md](BUILT_IN_EDITOR.md). Deferred cross-cutting features and the revised post-M11 roadmap are collected in [FUTURE_FEATURES.md](FUTURE_FEATURES.md).

### User-Facing Window Roles

The target desktop UI separates ordinary lyrics reading, desktop-overlay presentation, media transport, editing, and settings/debugging into distinct surfaces. The existing `MainWindow`/Control Panel is the implementation predecessor of the future Main Lyrics Window; it should evolve toward that role rather than becoming a permanent diagnostics dashboard.

Conceptually:

```text
LyricsDisplayer.App
├─ Main Lyrics Window
│  ├─ ordinary focusable Windows window
│  ├─ full-document All Lyrics presentation only (no One/Two/All selector)
│  ├─ shared lyrics context menu
│  ├─ explicit Settings / Built-in Editor / Media Controller actions
│  └─ small transport-status footer
│
├─ Desktop Lyrics Overlay
│  ├─ permanently Topmost whenever visible, except temporary editor suppression
│  ├─ OneLine / TwoLines / contextual AllLyrics presentation modes
│  ├─ click-through / lock / drag / resize behaviour
│  └─ shared lyrics context menu
│
├─ Media Controller (future)
│  ├─ independent show/hide lifecycle
│  ├─ track metadata and playback state
│  ├─ transport/progress controls
│  └─ optional docking beside the Desktop Lyrics Overlay
│
├─ Built-in Lyrics Editor Dialog
│  ├─ modal single-track editing workspace
│  └─ owner of lyrics timing-adjustment UI
│
├─ Settings Window (future restructuring)
│  ├─ left-side section navigation
│  ├─ one independent page at a time on the right
│  └─ Debug page for transport/library/raw-data diagnostics and log access
│
└─ System Tray
   ├─ open the Main Lyrics Window
   ├─ show/hide the Desktop Lyrics Overlay
   └─ explicit application exit
```

The Main Lyrics Window is intentionally different from the overlay's existing `AllLyrics` mode. The Main Lyrics Window is the complete lyrics-reading surface and should expose the full lyrics document without a content-mode selector. The overlay's `AllLyrics` mode remains a bounded contextual viewport around the current timeline occurrence.

The Main Lyrics Window should remain a normal non-Topmost window. Users who want lyrics permanently above other applications use the Desktop Lyrics Overlay; users who prefer an ordinary window can leave the overlay hidden and use the Main Lyrics Window instead.

Settings are not intended to be one long anchor-scrolled page. Selecting a section in the left navigation replaces the right-hand page. Planned sections include General, Lyrics Overlay, Lyrics Window, Media Controller, Editor, and Debug. Developer-oriented data such as `LocalTrackId`, library/index paths, raw playback/lyrics JSON, provider/source diagnostics, and log/crash-report access belongs under Debug rather than the Main Lyrics Window.

Track title/artist, playback position/state, and transport controls belong to the future Media Controller rather than the Main Lyrics Window. Existing timing-adjustment controls are also planned to move out of the Main Window/overlay quick-action UI and into the Built-in Lyrics Editor; the underlying M8 timing semantics remain unchanged.

The current close-to-tray implementation may continue to refer to the Control Panel/MainWindow until the UI restructuring is implemented. Its target semantics are simply to hide the Main Lyrics Window while background services, overlay, tray, and hotkeys remain active; Tray Exit remains the explicit application shutdown path.

### Shared application commands and entry points

User-facing surfaces should invoke shared application commands rather than owning duplicate feature logic. A command may be exposed from multiple surfaces while keeping one implementation and one set of enablement rules.

For example, Milestone 11's built-in editor is opened through one application command:

```text
OpenBuiltInEditorCommand
        ↓
Built-in Editor Dialog
```

The current MainWindow/Control Panel and Desktop Lyrics Overlay context menu may both expose this command, and the future Main Lyrics Window and Media Controller should reuse the same command. Opening the editor is therefore not tied to any one surface. The same principle applies to editor row actions: toolbar/buttons, context menus, and keyboard bindings should route to the same editor commands.

Command identity and command execution are separate from input bindings. This separation allows future user-configurable hotkeys without rewriting feature logic. Global/application/editor shortcut scopes may be introduced later; Milestone 11 only needs the editor command/binding architecture described in [BUILT_IN_EDITOR.md](BUILT_IN_EDITOR.md).

### Desktop overlay window-state invariant

The Desktop Lyrics Overlay is a floating, application-managed surface, not a conventional application window. Its only supported state is `WindowState.Normal`; minimized and maximized states are prevented or immediately normalized. Native Windows caption movement and Snap placement are not used for moving lyrics. During a drag, the cursor selects the target monitor and the full overlay rectangle is constrained to that monitor's usable work area (`MONITORINFO.rcWork`), including taskbar exclusions. Crossing onto another monitor immediately switches the movement constraint to that monitor. Mixed-DPI movement keeps cursor/work-area/window positioning in physical screen pixels and converts WPF size/grab offsets with the target monitor's scale; a window larger than the destination work area is reduced only as much as needed to fit. Native edge/corner resizing stays pinned to the monitor where the resize started and is clamped to its work area, while position lock continues to disable movement without disabling resize. Overlay geometry is persisted only from a normal floating state, and show/recovery normalizes the state and reapplies validated saved geometry inside a current work area before displaying the singleton window.

### Renderer dimensions

The renderer keeps these concerns separate:

```text
Lyrics state → Content mode → Layout style → Appearance → Optional karaoke animation → WPF visuals
```

Milestone 9 overlay content modes describe quantity: `OneLine`, `TwoLines`, and `AllLyrics`. These modes belong to the Desktop Lyrics Overlay. The future Main Lyrics Window does not expose this selector and instead remains a full-document All Lyrics reading surface. Overlay `Left`, `Top`, `Width`, and `Height` are persistent window geometry independent of content mode; switching modes never resizes the window. `AllLyrics` is a contextual multi-line viewport around the exact current timeline occurrence, not a rendering of the whole lyrics document. Its nearby past/upcoming lines fit the available geometry; it does not use a scrollbar. The current layout is `CenterStacked`. A future `KaraokeAlternating` layout may use the same `TwoLines` content mode with different positioning; karaoke is not a line-count mode.

Presentation lines carry semantic roles such as Past, Current, Upcoming, and Status. In All Lyrics, viewport selection is separate from timeline semantics and follows the strict sequence `Current, Upcoming +1, Previous -1, Upcoming +2, Previous -2, ...`. Current is mandatory when one exists. Fitting may select only a prefix of this sequence; measurement decides visible count, while semantic priority decides line identity. If a candidate does not fit, selection stops rather than scanning for a shorter lower-priority replacement. Selected rows are sorted into normal document order only after fitting. Out-of-range neighbours are omitted from the sequence, so at the start available Upcoming rows fill the viewport and near the end available Past rows can. Resizing immediately rebuilds this bounded contextual subset. No scrollbar, persistent scroll position, or full-document visual tree is used.

All Lyrics appearance combines semantic role (`Past`, `Current`, or `Upcoming`) with distance from Current. Current is the strongest focal row, Upcoming is clearly readable, and Past is lighter/contextual than Upcoming at equal distance. Size and opacity fall off with distance and retain readability bounds. These are presentation defaults, not lyrics-domain semantics; the WPF appearance layer determines the actual emphasis. Future appearance preferences may include font family/size/weight, role-based colors, alignment, opacity, outline, shadow, spacing, and background opacity. User preferences may override the defaults. Karaoke may add sung/unsung colors or progressive fill. Those customizations and animation are not part of M9.

The overlay remains a presentation surface over application state rather than accumulating playback, storage, or search logic.

### Future Media Controller and playback controls

The future Media Controller is an independent application surface rather than content owned by the overlay. It may be shown/hidden independently and optionally dock beside the Desktop Lyrics Overlay on the left or right while remaining a separate window/lifecycle. Potential controls include current track title and artist, elapsed time, duration, Previous, Play/Pause, Next, a seekable progress bar when the matched source supports seeking, and explicit application actions such as **Open Built-in Editor**. Editor opening must invoke the same shared application command used by the Main Lyrics Window/current MainWindow and overlay context menu; the Media Controller must not own a separate editor path. Previous and Next refer only to transport buttons; do not show previous-track or next-track metadata. This controller and its transport commands are not implemented.

Display state should primarily reuse Lyrics Displayer's existing playback model (title, artist, duration, playback position, and playing/paused state). Commands belong to a future provider-independent `MediaControlService`. Do not assume Windows `GetCurrentSession()` identifies the YouTube Music session Lyrics Displayer is tracking. The future implementation should enumerate `GlobalSystemMediaTransportControlsSessionManager.GetSessions()` and attempt to match the tracked playback source using multiple signals such as source-application identity, media title, artist, duration, playback position, and playback state. No single weak signal is authoritative.

Matching must fail safe: enable transport only for exactly one sufficiently strong match; disable it for zero strong matches or multiple ambiguous matches. Do not attempt to force YouTube Music to become Windows' current/priority media session; find and control the matching session directly. Firefox behavior with multiple media-producing tabs must be empirically verified rather than assuming one Windows session per tab or one session for the whole browser. If the tracked YouTube Music source is not uniquely controllable, generic Windows media controls remain disabled rather than risking control of another application.

Seek is a desired future command: a drag requests playback-position change through `MediaControlService` on the matched session only when that session accepts seeking. Lyrics Displayer's existing `PlaybackClock` remains the preferred source for smooth displayed elapsed time. If seeking is unsupported or rejected, playback progress may remain visible while seek is disabled or safely reverted. Future matched-session commands may include Play/Pause, Previous, Next, and Seek. No Firefox extension media-command path or Windows media-control implementation is part of M9.

---

## 3.4 LyricsDisplayer.Core

Core contains application-domain types and logic that are not tied to WPF, Firefox or a particular transport.

Examples of future domain types include:

* Track
* TrackIdentity
* TrackMetadata
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

# 8. Track Metadata and User Overrides

Playback-source metadata is not assumed to be perfectly clean or canonical.

For example, a YouTube Music source may provide:

* a video title rather than a clean song title
* an uploader/channel name rather than the canonical artist
* incomplete or unavailable album information

The original playback-source metadata must be preserved.

Users may optionally define their own title and artist overrides for a track.

Conceptually:

```text
Source metadata
├─ SourceTitle
├─ SourceArtist
└─ SourceAlbum

Optional user overrides
├─ UserTitle
└─ UserArtist

        ↓

Effective metadata
├─ EffectiveTitle  = UserTitle  ?? SourceTitle
└─ EffectiveArtist = UserArtist ?? SourceArtist
```

User overrides must not overwrite or destroy the original source metadata.

A missing override is represented as `null`, meaning "use the source value".

An empty value entered through the UI should normally be interpreted as clearing the override rather than as meaningful metadata.

## 8.1 Effective Metadata

For fields that support user overrides:

```text
EffectiveTitle  = UserTitle  ?? SourceTitle
EffectiveArtist = UserArtist ?? SourceArtist
```

Effective metadata is used when an application feature needs the user's best known representation of the song.

In particular, remote lyrics-provider searches should prefer effective metadata.

The playback source itself continues to report its original source metadata.

The Firefox extension does not need to know about user overrides.

---

## 8.2 Track Identity vs Search Metadata

Title and artist are search hints.

They are NOT the authoritative identity of a track.

Lyrics associations must use stable local track identity and playback-source associations.

Conceptually:

```text
LocalTrackId
    │
    ├─ youtubeMusic:<sourceTrackId>
    ├─ futurePlaybackSource:<sourceTrackId>
    └─ ...
```

A YouTube Music `sourceTrackId` identifies the playback-source association, not the universal local track identity.

Changing a title or artist override must not change the track identity or break an existing lyrics association.

Once a local LRC has been associated with a track, future playback of that same associated track should load the local lyrics directly without requiring another title/artist search.

This remains true even if:

* the playback-source title changes
* the uploader/channel display name changes
* the user changes the metadata override
* a different search query would now be generated

Track identity answers:

```text
Which playback item is this?
```

Metadata answers:

```text
What should we call/search for this song?
```

These concepts must remain separate.

---

## 8.3 Lyrics Search Metadata

When searching an external lyrics provider, Lyrics Displayer should use effective metadata by default:

```text
EffectiveTitle  = UserTitle  ?? SourceTitle
EffectiveArtist = UserArtist ?? SourceArtist
```

This allows poor playback-source metadata to be corrected without modifying the original source data.

Example:

```text
Playback source:

Title:
【HD】Some Song Official MV 中文字幕

Artist:
SomeUploader123

User overrides:

Title:
Some Song

Artist:
Actual Artist
```

Remote lyrics providers should search for:

```text
Some Song
Actual Artist
```

rather than the raw playback-source values.

---

## 8.4 Manual Lyrics Search Workflow

If an automatic lyrics search fails or produces poor results, the user should eventually be able to open a manual lyrics-search interface.

The intended workflow is similar to traditional desktop lyrics players.

The user may:

1. Review the title and artist currently being used for searching.
2. Edit the search title.
3. Edit the search artist.
4. Search again.
5. Review provider results.
6. Select the correct lyrics result.
7. Associate the selected lyrics with the current stable track identity.
8. Save the selected/imported LRC locally.
9. Optionally save the entered title and artist as persistent user metadata overrides.

Example future UI:

```text
Lyrics Search

Title:
[ Some Song ]

Artist:
[ Actual Artist ]

[x] Save these values as metadata overrides

[ Search ]
```

The user should also be able to perform a one-off modified search without permanently changing track metadata.

Therefore:

```text
Search query metadata
```

and:

```text
Persistent user metadata overrides
```

must not be treated as exactly the same thing.

The search UI may initialise its values from effective metadata, while allowing the user to decide whether edited values should be persisted.

Once the user selects a correct lyrics result, that lyrics file is associated with the stable track identity.

On later playback of the same track:

```text
Track association found
        ↓
Local lyrics found
        ↓
Use local lyrics
```

A new provider search is not required merely because the original playback-source metadata is poor.

---

# 9. Lyrics Files

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
* source track metadata
* user-defined title/artist overrides
* global timing offset
* user-modified state
* source version or hash information

The sidecar schema is versioned and becomes concrete as part of Milestone 5.

Portable metadata that should follow the lyrics library between machines must not exist only in the machine-local SQLite index.

---

# 10. Local Application Storage

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

Not all files or directories need to exist in early milestones.

---

# 11. Configurable Lyrics Library

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

This includes persistent user metadata corrections that should follow the lyrics library between computers.

Machine-specific runtime state, including the SQLite library index, should remain under local application data.

---

# 12. Local Index / SQLite

SQLite is the machine-local searchable index for the lyrics library.

It is a required library component from Milestone 5 onward, but it is NOT the authoritative storage for lyrics or portable metadata.

The database remains under:

```text
%LOCALAPPDATA%\LyricsDisplayer\library-index.db
```

The SQLite index exists to provide fast lookup and search without repeatedly scanning and deserializing every sidecar file, especially when the configured lyrics library is large or hosted on SMB/NAS storage.

Typical indexed information includes:

* local track identity
* playback-source associations
* source title/artist/album
* user title/artist overrides
* effective title/artist
* duration
* local LRC and sidecar locations
* lyrics provider/source and attribution
* file metadata useful for detecting library changes

The ordinary lyrics text remains in the `.lrc` file.

Portable metadata remains in the versioned sidecar file.

Data such as user metadata overrides may be duplicated into SQLite for efficient search, but the portable sidecar remains authoritative for information that should follow the lyrics library between computers.

Do not use a SQLite database directly hosted on SMB as the normal multi-machine sharing mechanism.

Each computer using a shared lyrics library maintains its own local `library-index.db`.

The database must be disposable and rebuildable:

```text
Delete/corrupt/missing local index
        ↓
Scan configured lyrics library
        ↓
Read versioned sidecars and LRC assets
        ↓
Rebuild SQLite index
```

The portable files are authoritative.

The SQLite index is a local acceleration/search layer.

A missing or corrupt SQLite index must not imply loss of the user's lyrics library.

Milestone 5 introduces the first concrete SQLite schema and library rebuild/synchronisation behaviour.

---

# 13. Lyrics Timing Adjustment

Milestone 8 supports per-local-track operations:

* -0.5 seconds
* -0.1 seconds
* +0.1 seconds
* +0.5 seconds
* reset

Timing adjustment is non-destructive by default. `GlobalOffsetMs` is stored in the portable sidecar and is loaded as zero when the optional field is absent. Positive values make lyrics happen later and negative values make them happen earlier.

Example:

```text
GlobalOffsetMs = -500
```

The original LRC timestamps remain unchanged while the existing timeline evaluates `PlaybackPositionMs - GlobalOffsetMs`. `PlaybackClock` itself remains the unadjusted media clock.

An explicit, confirmed Bake operation can apply the offset to every valid raw LRC timestamp token and then reset the sidecar value to zero. The rewriter preserves unrelated physical LRC content and rejects negative or overflowing results before committing either file. The LRC and sidecar are prepared through same-directory temporary files; failure after the first replacement triggers a best-effort restoration of the original LRC.

Silent destructive rewriting is not acceptable.

A persistent correction requires an authoritative local timed lyrics document. Controls remain disabled for pending, untimed, unavailable, or runtime-only results. SQLite does not store the offset. See [LYRICS_TIMING_ADJUSTMENT.md](LYRICS_TIMING_ADJUSTMENT.md) for the implemented contract and safety details.

The Milestone 8 Current Line follow-up is deliberately different: its explicit ±0.1/±0.5-second buttons directly edit only the current local LRC timestamp occurrence. Local parsing retains exact source locations and a loaded-content hash for safe targeting and stale-file rejection. Negative timestamps and crossings of adjacent starts are rejected; equal starts remain deterministic. The sidecar/global offset is unchanged, no per-line offsets are stored, and a successful save rebuilds the current timeline immediately. This is not a full editor; the active-file watcher is added separately in Milestone 10.

## 13.1 Explicit Break Markers and Future Karaoke Presentation

A timestamp gap by itself must never be interpreted as an instrumental/vocal break.

Breaks require explicit notation.

The preferred portable representation is a normal timed LRC line using the default explicit marker:

```lrc
[00:13.000]♪
```

Blank/whitespace lyric text is intrinsically semantic-empty. `♪` is the current default explicit marker. A later preference may let the user define additional trim-and-exact-match marker strings; those preferences classify existing lyric text at runtime and do not rewrite the authoritative LRC. Shared classification should be reused by Bake safety, break-aware rendering, and preparation-cue logic instead of giving each feature its own hard-coded marker list.

This line semantically means:

```text
The previous sung lyric has ended.
From this timestamp until the next timed line, there is no sung lyric.
```

The duration of the break is irrelevant. A break may be short or long.

Do not infer a break from rules such as:

```text
next lyric start - current lyric start >= N seconds
```

because a provider may assign a long timing interval even when the vocal phrase ends much earlier.

Example:

```text
Provider timing:
10.000-20.000  xxxx
20.000-30.000  yyyy

Actual vocal phrase:
10.000-13.000  xxxx
13.000-20.000  no sung lyric
```

A future editor may let the user insert:

```lrc
[00:10.000]xxxx
[00:13.000]♪
[00:20.000]yyyy
```

The ordinary LRC timeline then naturally represents:

```text
10.000-13.000  xxxx
13.000-20.000  explicit break
20.000+        yyyy
```

This is preferable to storing a proprietary per-line end-time override.

The canonical marker should remain visible in the portable LRC so that the break is explicit and externally editable. The domain model may classify it as a break line, but the desktop renderer does not have to display the `♪` glyph.

Future two-line karaoke presentation should treat an explicit break as a real semantic boundary.

Example:

```text
[00-10] aaaa
[10-20] xxxx
[20-40] ♪
[40-50] yyyy
[50-60] zzzz
```

Intended presentation concept:

```text
00-10:
Primary   = aaaa
Secondary = xxxx

10-20:
Primary   = xxxx
Secondary = blank
```

The renderer must not skip over the explicit break and prematurely use `yyyy` as the normal next line.

At the start of the explicit break:

```text
20s:
Primary   = blank
Secondary = blank
```

The renderer may later pre-display the next sung lyric before it begins so that the singer can prepare.

This preview lead time should be configurable rather than treated as semantic timing. An initial future default around 10 seconds may be reasonable.

Preparation marking is a separate renderer concern.

The intended future rule is:

```text
If an explicit break exists and the interval before the next sung lyric
is longer than the preparation threshold:
    a prepare indicator may be used.

If the break interval is 3 seconds or less:
    do not require a separate prepare indicator;
    directly pre-display the upcoming lyric.
```

The current intended preparation threshold is approximately:

```text
3 seconds
```

This threshold does NOT decide whether a break exists. Only explicit break notation establishes a break.

For a longer break, a future renderer may use a staged presentation such as:

```text
early break
→ blank presentation

upcoming-lyric preview window
→ show the next sung lyric in advance

final preparation window
→ animate a prepare/count-in indicator

next lyric start
→ begin normal karaoke rendering
```

The exact visuals are intentionally deferred.

Future long-line rendering should also support alternatives to wrapping.

Likely modes include:

```text
Wrap
Horizontal pan
```

For horizontal pan, the font size may remain fixed while an overlong line moves horizontally during its active interval.

The movement should not necessarily begin at the exact lyric start or finish at the exact lyric end. A future renderer should allow a leading hold and trailing hold so that the line can:

```text
appear
→ remain briefly stable
→ pan from right toward left
→ finish panning before the vocal interval ends
→ remain briefly stable
```

This behaviour belongs to karaoke/presentation customisation and must not alter the underlying LRC timestamps.

---

# 14. Editing

Milestone 10 completed safe external editing/reload. Milestone 11 adds the built-in editor foundation documented in [BUILT_IN_EDITOR.md](BUILT_IN_EDITOR.md). The built-in editor is a modal, single-track workspace over the same authoritative local LRC/sidecar data; it is not a second playback surface.

While the modal editor is open, it must remain visually above the Desktop Lyrics Overlay. The overlay is permanently Topmost whenever visible during normal operation, so editor opening temporarily suppresses that invariant for the editor lifetime and restores Topmost after the editor actually closes. The editor itself is not made permanently Topmost. Fatal/accepted application shutdown does not perform a late visual restoration.

Core invariants:

```text
Playback Current Line ≠ Editor Selection

Playback may provide current time to an explicit editor command.
Playback must not move selection, switch the editor document, or mutate the editor buffer.
```

The editor is pinned to the `LocalTrackId` it was opened for. Playback may switch to other tracks while the modal dialog remains open; this never closes, switches, saves, discards, or prompts the editor. **Set Current Playback Time** is enabled only while the current playback track matches the fixed editor track. The command is row-oriented: it appends the current playback position into the first free timestamp slot, up to five UI-managed occurrences; with five existing timestamps its `CanExecute` is false. Existing source data with more occurrences must still round-trip losslessly.

The editor uses a structured `EditorDocument`, not a simple list of timestamp/text pairs. It preserves ordered physical LRC structure, existing multi-timestamp lines, stable editor-row identity, validation diagnostics, dirty state, and undo/redo history. Recognized metadata may be hidden from the lyric grid but remains preserved; every other physical line, including blank, unknown, and malformed content, maps to one editor row. A lyric row supports zero or more timestamps from the beginning. Document order remains user-controlled and is never silently resorted by timestamp.

Core row commands include:

```text
Insert Row Above
Insert Row Below
Append Row
Delete Row
Set Current Playback Time
Undo
Redo
Save
```

Inserted/appended rows begin with an empty lyric cell and no timestamps. No timestamp is interpolated or inferred from neighbouring rows. The user may type timing explicitly or invoke the playback-time command. A user-created empty lyric row is meaningful editor content and must survive Save -> Close -> Reopen rather than being collapsed out of `EditorDocument`.

Editor operations are commands first. Buttons/toolbars, row context menus, and keyboard shortcuts are bindings over the same commands. Initial navigation follows grid conventions such as Enter/Shift+Enter for next/previous row in the same logical column and Tab/Shift+Tab for adjacent editable cells. Stable command IDs/scopes should leave room for later user-configurable shortcuts without coupling feature logic to key-event handlers.

Validation is advisory. Errors use clear red emphasis and warnings clear yellow emphasis on the actual timestamp or Lyrics cell named by diagnostic metadata. A neutral `DataGrid.RowHeader` numbers visible editor rows (including blanks, excluding hidden recognized metadata); clicking it must not select a row or change the current cell. Diagnostics without a specific cell target remain summary-only. The bottom warning/error counts expose all diagnostic messages in deterministic visible-line order, with Error before Warning on the same line, and no tooltip when there are no diagnostics. The advisory notice appears only while issues exist, and validation never blocks Save. M10 runtime safety remains separate: a saved malformed LRC may remain on disk exactly as authored while active playback retains last-known-good runtime lyrics.

Dirty close/exit behavior uses `Save / Discard / Cancel`. If the authoritative file changes externally while the editor has unsaved changes, conflict handling remains intentionally simple: `Discard My Changes / Reload`, `Overwrite External Changes`, or `Cancel`; no three-way/collaborative merge is required.

The editor header may edit portable `UserTitle` / `UserArtist` overrides while showing source metadata for reference and allowing the override to be cleared. These remain sidecar metadata according to the existing effective-metadata rules; the editor must not silently create a second authority by writing user overrides into LRC `[ti:]` / `[ar:]` tags.

The canonical portable explicit break marker remains:

```lrc
[timestamp]♪
```

Break insertion/removal/retiming, richer multi-timestamp management, bulk timing tools, destructive Traditional/Simplified editor conversion, custom hotkeys, and other advanced commands are later additive features rather than requirements for the M11 foundation. See [FUTURE_FEATURES.md](FUTURE_FEATURES.md).

---

# 15. Logging Architecture

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

## 15.1 Rotation Rules

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

## 15.2 Session Startup Rotation

On startup:

* If `current.logs` does not exist, create it.
* If it exists but is empty, it may be reused.
* If it contains logs from a previous session, rotate it before writing the new session.

For the Firefox extension, a new extension/background source session is treated as a new logging session.

For the NativeHost, a new process is treated as a new logging session.

For the WPF application, a new application process is treated as a new logging session.

## 15.3 Midnight Rotation

While a component is running, the logger must detect when the local calendar date has changed.

Before writing the first entry belonging to the new date:

1. Close/flush the current log.
2. Rotate the previous `current.logs`.
3. Create a fresh `current.logs`.
4. Continue logging.

Exact execution at precisely `00:00:00.000` is not required.

The requirement is that entries belonging to the new local date do not continue indefinitely in the previous day's active log.

## 15.4 Retention

Rotated logs are retained for a maximum of 30 days.

Cleanup should occur at least:

* on logger/component startup
* after a rotation

Logs older than the retention period should be deleted automatically.

`current.logs` is not deleted by retention cleanup.

Retention cleanup should only affect recognised Lyrics Displayer rotated log files in the relevant component log directory.

## 15.5 Firefox Extension Logging

The Firefox extension should continue to log useful information to the Firefox developer console.

For persistent logging, the extension sends diagnostic log messages through Native Messaging.

`LyricsDisplayer.NativeHost` writes those entries to:

```text
%LOCALAPPDATA%\LyricsDisplayer\Logs\FirefoxExtension\
```

The extension must not directly access the Windows filesystem.

If the native connection is unavailable, console logging remains the fallback.

## 15.6 NativeHost Logging

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

## 15.7 Windows App Logging

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

## 15.8 Application Fatal Exception Reporting

Standalone crash reports are written lazily under:

```text
%LOCALAPPDATA%\LyricsDisplayer\Logs\Crash\
```

An unexpected fatal exception is written to the existing application logger at
`Fatal` severity and also to one uniquely named standalone report. The report
includes runtime/process context and the exception/inner-exception chain. It
does not include lyrics contents, browser contents, credentials, or exception
data values. If the crash directory cannot be written, reporting attempts a
minimal fallback there and then under `%TEMP%\LyricsDisplayer\Crash\`.

Fatal log entries use readable multiline output with indented continuation
lines; ordinary log entries retain their single-line escaping. One process
fatal incident owns at most one fatal dialog and one controlled shutdown.
Secondary exceptions during fatal shutdown do not trigger another dialog,
shutdown request, or primary crash report. The AppDomain handler remains
best-effort and does not show UI.

WPF dispatcher exceptions trigger a best-effort fatal dialog followed by
controlled application shutdown; fatal shutdown bypasses the normal
close-to-tray path and does not attempt to save user data. The application
publishes a shared lifetime state (`None` or `FatalException`).
The primary fatal owner transitions it to `FatalException` before diagnostics
or notification. Editor close handling checks this state before committing
pending grid edits: fatal shutdown closes without a dirty prompt, save, or
cancel opportunity. Normal editor close and Tray Exit retain Save / Discard /
Cancel behavior. Unobserved task exceptions are logged at `Error` severity
and do not by themselves create a crash report or request application shutdown.

The current app logger writes and closes each entry synchronously and has no
separate flush API, so the fatal log write is the available best-effort flush.

---

# 16. Automatic App Launch

Initial behaviour:

```text
Auto-launch Lyrics Displayer = disabled
```

The user manually starts the WPF application.

A future preference may enable automatic launching when the Firefox source needs the application.

Automatic launching must remain optional.

The architecture must not assume that auto-launch is always enabled.

---

# 17. Milestones

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
* Automated NUnit tests are part of the milestone definition of done.
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
* artist/uploader
* album only when reliably identifiable; otherwise `null`
* duration

Playback-source metadata is accepted as source metadata even when it is not clean/canonical.

Examples include:

* video-style titles
* uploader/channel names instead of canonical artists

Do not substitute unrelated values such as view counts into semantic fields such as album.

User metadata correction belongs to later local-library functionality.

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

The implemented browser adapter retains approximately 500 ms periodic snapshots and adds coalesced immediate snapshots for reliable media lifecycle events. The Windows application rebases an anchor-based monotonic clock only from snapshots accepted by the existing session/sequence authority rules, freezes it on source disconnection, and exposes both authoritative and locally interpolated positions in the diagnostic UI.

---

## Milestone 4 — YouTube Music Timed Lyrics

Read YouTube Music's own timed lyrics when available.

The visible desktop YouTube Music lyrics UI must not be assumed to contain timestamp information.

If required, timed lyrics may be obtained through YouTube Music's internal mobile-client flow rather than scraping the visible desktop lyrics text.

No remote fallback providers yet.

Milestone 4 retrieves lyrics inside the owning Firefox tab using the current YouTube Music session. The adapter follows the referenced `ytmusicapi` watch-playlist → lyrics browse ID → mobile lyrics flow; see [YOUTUBE_MUSIC_LYRICS.md](YOUTUBE_MUSIC_LYRICS.md) for request/parser details and verification limitations.

Confirmed results are cached in extension memory by source track ID. Track/session changes cancel outstanding work and every asynchronous result is checked against the current owner and request generation. Full lyrics travel in a separate `lyricsSnapshot`, not in each periodic playback snapshot. The Windows diagnostic state clears lyrics on authoritative track/session changes and independently rejects mismatched or stale results. Unknown/failed lookups are distinct from confirmed no-lyrics results.

NativeHost retains the latest message per application type and replays playback before lyrics after pipe reconnects. This prevents playback coalescing from losing a one-off lyrics result. Lyrics storage, timeline selection and rendering remain later milestones.

---

## Milestone 5 — Local Lyrics Library

Introduce:

* local-first loading
* standard UTF-8 LRC persistence
* versioned portable sidecar metadata
* stable `LocalTrackId`
* playback-source track associations
* source metadata preservation
* optional user-defined title/artist overrides
* effective metadata calculation
* configurable lyrics-library path
* SMB/UNC-compatible portable library design
* machine-local SQLite `library-index.db`
* fast association/title/artist lookup through SQLite
* startup/library scan and SQLite rebuild/synchronisation
* local-first precedence over later remote provider results
* protection against silent overwrite of user-edited local lyrics

SQLite is required as the local searchable/indexing layer in this milestone, but the `.lrc` and sidecar files remain authoritative and portable.

Available YouTube Music lyrics may be materialised through this same local-first workflow whether timed or untimed. Untimed provider text is stored as ordinary UTF-8 lines in `track.lrc` without fabricated timing; a valid untimed local file remains authoritative and externally editable. The existing parser determines when valid timestamps make a subsequently edited document timed. See [LOCAL_LYRICS_LIBRARY.md](LOCAL_LYRICS_LIBRARY.md) and [EXTERNAL_EDITING.md](EXTERNAL_EDITING.md).

Deleting the local SQLite database must not delete or invalidate the lyrics library; the index must be reconstructable from the configured library files.

---

## Milestone 6 — Lyrics Timeline Engine

Select the current and next lyric from accepted timed lyrics using the local playback clock. Selection is start-time based, normalizes ordering once, and uses binary search without persisting runtime line state. See [LYRICS_TIMELINE.md](LYRICS_TIMELINE.md).

---

## Milestone 7 — Desktop Overlay

Add:

* transparent window
* always-on-top
* current/next line display
* draggable position with safe shared-settings persistence
* visible fallback after monitor topology changes
* non-activating updates and singleton show/hide lifecycle
* restrained explicit status text when timed lyrics cannot be displayed

The overlay should never leave stale lyrics visible when the current track has no usable timed lyrics.

Preferred user-facing status text:

```text
Confirmed no lyrics:
暫無可用歌詞

Lyrics exist but are untimed:
此歌曲暫無同步歌詞
```

These messages are presentation state only. They must not change the underlying lyrics availability/timed classification.

The implemented overlay is a view over the already resolved Milestone 6 timeline. It does not own a playback clock or duplicate lyric selection. See [DESKTOP_LYRICS_OVERLAY.md](DESKTOP_LYRICS_OVERLAY.md).

Long-term, the Desktop Lyrics Overlay remains the permanently-on-top ambient lyrics surface, while the existing MainWindow evolves into an ordinary Main Lyrics Window that always presents the full lyrics document. Preferences/diagnostics move to a separate Settings surface. Advanced overlay customisation, explicit break rendering, karaoke preparation cues and long-line horizontal panning remain later work.

---

## Milestone 8 — Timing Adjustment

Implemented:

* ±0.1 second
* ±0.5 second
* persistent global offset
* explicit bake-to-file operation

The offset is global only within one local lyrics document, not across the application. Runtime adjustment is sidecar-only; Bake is confirmed, structure-preserving, validated, and resets the offset after coordinated file replacement.

---

## Milestone 9 — Overlay Interaction

Implemented:

* lock/unlock
* partial click-through: empty overlay regions pass input through while lyric text remains interactive
* global shortcuts
* persistent overlay preferences
* permanent Topmost behaviour while visible, with temporary modal-editor suppression
* persistent left/top/width/height geometry independent of content mode, with invisible borderless edge/corner resizing while click-through is off and visible-work-area recovery
* application-managed lyric dragging that bypasses the native caption move/Snap workflow, with minimized/maximized states blocked and normalized before show/recovery
* `OneLine`, `TwoLines`, and `AllLyrics` content modes
* bounded Past/Current/Upcoming context for All Lyrics, selected by exact timeline occurrence and fitted as a prefix of `Current, Upcoming +1, Previous -1, Upcoming +2, Previous -2, ...` without scrolling
* direct, clearly scoped Current Line and Global timing quick actions in the overlay context menu, routed to existing M8 operations
* system tray lifecycle/recovery with optional close-to-tray behavior
* explicit tray exit that unregisters hotkeys and shuts down the App

The overlay remains the always-on-top ambient lyrics surface. The current MainWindow is still the legacy Control Panel during this milestone, but the target architecture evolves it into the ordinary full-document Main Lyrics Window; the tray remains available when that window is hidden.

The interaction state is owned by the overlay controller and persisted in the existing machine-local settings file. A focused `WM_NCHITTEST` hook selects interactive lyric/grip regions; it does not apply whole-window `WS_EX_TRANSPARENT`. Two fixed `RegisterHotKey` shortcuts remain available while the current MainWindow is hidden. The implemented M9 timing quick actions still route to the existing playback/timing coordinator, but the target desktop-surface redesign moves all timing-adjustment UI into the Built-in Lyrics Editor and removes timing controls from the overlay/Main Lyrics Window. See [OVERLAY_INTERACTION.md](OVERLAY_INTERACTION.md).

---

## Milestone 10 — External Editing

Implemented:

* open the usable active local LRC with the Windows default associated application from the Control Panel or overlay context menu
* watch only the active LRC's containing directory (and its track-directory parent for recreation), debounce events for 300 ms, and accept content only after two matching SHA-256 reads within six bounded attempts
* handle Changed/Created/Deleted/Renamed events and atomic replacement saves, with bounded retry and watcher recovery; activation and overlay-show fingerprint checks provide a low-frequency fallback for SMB/NAS watcher gaps
* validate/reload changed local lyrics read-only, reject fatal timestamp-syntax diagnostics as a whole (never partially accepting a malformed document), preserve `GlobalOffsetMs` and `PlaybackClock`, rebuild timeline/source occurrences and overlay immediately, and retain last-known-good runtime lyrics for invalid edits without changing user files; valid untimed content remains distinct from malformed syntax
* allow a usable untimed local LRC to be opened externally and watched by the same active-file watcher; clean text edits remain untimed, while adding valid timestamps transitions the same local record to timed playback immediately without synthesising timestamps
* mark persistently missing local LRC unavailable without creating it or falling back to provider lyrics; continue watching for automatic recovery
* bind asynchronous observations to the active local track/path generation; keep watching while UI surfaces are hidden and dispose watchers at application shutdown
* deduplicate identical content, including app-owned Current Line/Bake writes, without broad time-based suppression

See [EXTERNAL_EDITING.md](EXTERNAL_EDITING.md) for behavior, limitations, and SMB/UNC considerations. This milestone does not add a built-in editor or library-wide watcher.

---

## Milestone 11 — Built-in Lyrics Editor Foundation

Implemented and manually accepted. See [BUILT_IN_EDITOR.md](BUILT_IN_EDITOR.md).

The accepted foundation includes:

* modal, single-track editor dialog pinned to one `LocalTrackId`
* modal editor above the normally-Topmost Desktop Lyrics Overlay through temporary overlay suppression
* one shared `OpenBuiltInEditorCommand` used by MainWindow and overlay entry points
* structured round-trippable `EditorDocument` preserving recognized metadata, unknown content, blank physical rows, and existing multi-timestamp lines
* stable row identity and explicit row/cell/timestamp selection independent of playback current line
* lyric-text and timestamp editing, Insert Above / Insert Below / Append / Delete Row, and command-based Delete-cell clearing with Undo/Redo
* Set Current Playback Time appending into the first free timestamp occurrence up to five UI-managed occurrences
* commit-time flexible timestamp input (`0`, `0:1`, `1:2`, `1:12.3`, etc.) with canonical `mm:ss.fff` normalization; unsupported plain integers and malformed input remain advisory-invalid rather than guessed
* spreadsheet-style Enter/Shift+Enter and Tab/Shift+Tab navigation, including dynamic timestamp columns
* independent per-occurrence timestamp-lane validation plus within-row timestamp ordering, with cell-specific diagnostics and visible row-number gutter
* safe save, dirty-state tracking, external-file conflict handling, and editor/application `Save / Discard / Cancel` close guards
* portable `UserTitle` / `UserArtist` override editing
* negative Bake protection for exact-zero intro/break anchors: `00:00.000` on blank/whitespace lyric text or the current default explicit break marker remains zero, while ordinary negative results still reject the whole bake

The milestone was exercised with real authoring of multiple songs, including converting untimed lyrics into timed/multi-timestamp local LRC content and round-tripping through the existing M10 reload path into synchronized overlay playback. No blocking M11 issue is currently known. Advanced editor productivity commands remain additive later milestones rather than reasons to keep M11 open.

---

## Milestone 12 — Application UI Foundation and Desktop Surface Restructure

This is the next milestone. It turns the current developer-oriented MainWindow into the intended product surface before more feature-heavy windows are added.

Primary work:

* introduce a small reusable ViewModel foundation (`INotifyPropertyChanged`, `SetProperty`, dependent-property notification, selective command invalidation) and migrate incrementally rather than creating a god ViewModel or rewriting the entire app at once
* evolve the current MainWindow/Control Panel into the normal non-Topmost **Main Lyrics Window**
* make the Main Lyrics Window a full-document lyrics-reading surface rather than a diagnostics dashboard; it has no One/Two/All selector
* introduce the dedicated **Settings** window with left-side section navigation and one independent page at a time
* move developer/transport/library/raw-data/log/crash-report information into **Settings > Debug**
* keep the Desktop Lyrics Overlay as the permanently-Topmost ambient surface with its existing OneLine/TwoLines/contextual-AllLyrics modes
* move remaining timing-authoring controls out of MainWindow/overlay presentation and into the Built-in Lyrics Editor while preserving M8 semantics
* consolidate shared application commands/context-menu actions across lyrics surfaces
* establish placeholders/settings boundaries for later Media Controller, renderer appearance, semantic break markers, and provider configuration without implementing those later features prematurely

The App-side ViewModel foundation uses ordinary typed backing fields. Generic `SetProperty` raises only the changed property's notification; dependent-property notifications stay explicit, and command invalidation remains an explicit operation owned by the relevant application state rather than a global requery. Domain logic remains in Core and application services.

M12 is primarily an application-structure and user-surface milestone. It should reduce accumulated MainWindow coupling before Lyrics Search, Media Controller, and richer renderer work add more state.

---

## Milestone 13 — Lyrics Search and Provider Framework

Add the provider/search layer without coupling provider-native formats to the editor, timeline, or renderer. Playback source and lyrics provider remain independent.

The milestone introduces a canonical provider boundary (`ILyricsProvider` or equivalent), capability/normalization model, provider registry/configuration, cancellation/stale-result protection, preview, and explicit **Use This Lyrics** import into the local-first library. Provider-native LRC/QRC/YRC/TTML/JSON/etc. formats normalize before reaching application presentation/editing code.

The first practical resolver/search flow should support the current YouTube/YouTube-Music identity well:

* if the playback source already has a known YouTube `videoId`, probe that candidate directly first where applicable
* otherwise/default fallback search uses effective title/artist metadata and retrieves a bounded candidate pool, initially about 5–10 YouTube Music candidates
* candidate discovery and lyric availability are separate stages
* probe lyric availability lazily and progressively rather than eagerly fetching every candidate at once; roughly one candidate per second is an acceptable initial pacing
* only candidates whose lyric fetch succeeds appear in the visible Lyrics Search results
* silently skip candidates with no lyrics
* do not display match/confidence percentages
* do not keep expanding unrelated search candidates merely to fill a result list; if the bounded candidate pool yields nothing, show **No lyrics found**
* changing query/track or choosing **Use This Lyrics** cancels remaining probes and stale results are ignored

Visible results should focus on useful metadata: Title, Artist, optional Album, Duration, Lyrics Source, Timed/Untimed, Preview, and **Use This Lyrics**. Import/replacement is explicit; preview never mutates local authority. Later provider refreshes must never silently overwrite the imported/editable local copy.

Potential built-in providers include YouTube Music resolution/search, LRCLib, and other community/provider-native adapters. Declarative custom HTTP sources may be supported for bounded known response shapes; arbitrary user scripts are not a generic provider mechanism.

An immutable original-import snapshot is a desirable safety feature for this provider/import stage so later user editing does not destroy the first imported baseline; resetting to it remains an explicit destructive command.

---

## Milestone 14 — Editor Productivity and Semantic Breaks

Build higher-level authoring tools on the accepted M11 command/document foundation. Likely work includes:

* configurable semantic break/blank markers: blank/whitespace remains intrinsic; the default explicit marker is `♪`; users may add/remove exact trim-matched marker strings without modifying the LRC
* one shared runtime break classifier used by negative-Bake zero-anchor protection, renderer break semantics, and future preparation cues
* **Fill Timestamp Pattern Down** / timing-lane propagation for repeated choruses: given a populated target anchor and a reference timestamp lane, fill selected blank target cells with `targetAnchor + (referenceRow - referenceAnchor)`
* Fill Timestamp Pattern affects only the user-selected contiguous range, skips rows lacking a reference timestamp, preserves existing populated target cells by default, keeps millisecond precision, and is one Undo/Redo unit
* richer add/remove/reorder management for timestamp occurrences
* explicit break insertion/removal/retiming commands
* multi-row/bulk timing operations and other proven authoring helpers
* optional destructive Traditional/Simplified conversion of editor lyric text as an explicit undoable buffer command

Excel-style drag-handle UI for pattern filling is desirable but should be layered over the same domain command; a simpler command/context-menu implementation may come first. No automatic chorus detection or AI timing inference is required.

---

## Milestone 15 — Renderer, Appearance and Karaoke Presentation

Expand presentation after the main surface/settings foundation is stable:

* shared lyrics presentation where practical between Main Lyrics Window and Desktop Lyrics Overlay, while preserving their different window/content-mode roles
* appearance preferences such as font, size/weight, role colours, opacity, alignment, outline/shadow, spacing, and background opacity
* display-only Original / Traditional / Simplified conversion
* configurable long-line wrapping or horizontal panning with lead/tail holds
* semantic-break-aware rendering and upcoming/preparation cues; elapsed gap length alone never creates a break
* karaoke layouts/animation, including progressive sung/unsung presentation when the canonical lyrics capability provides suitable timing

Content mode, layout style, appearance, and karaoke animation remain separate dimensions.

---

## Milestone 16 — Media Controller

Add the independent Media Controller surface described earlier:

* independent show/hide lifecycle and optional docking beside the overlay
* title/artist, elapsed/duration, playing/paused state
* Previous / Play-Pause / Next and seek where the matched source supports it
* shared **Open Built-in Editor** and other application commands rather than controller-owned duplicates
* provider-independent `MediaControlService` with conservative Windows media-session matching; ambiguous/no strong match disables transport controls rather than controlling the wrong player

The existing Lyrics Displayer playback model remains the preferred display-state source. Media Controller does not redefine playback-source identity.

---

## Milestone 17 — Playback Source and Browser Expansion

Add additional playback-source adapters only after the source-independent app boundaries and provider/search system are stable. Candidate work includes:

* ordinary YouTube tabs in Firefox, reusing a known YouTube `videoId` for direct YouTube Music lyric probing before metadata fallback search
* Chromium runtime support (Chrome/Edge/Brave) using shared playback/source logic with thin browser-specific adapters
* Spotify account/API integration as a playback source only, not a lyrics provider; its metadata/IDs feed the same local association and lyrics-resolution pipeline
* other Windows/local-player/GSMTC adapters where reliable identity can be established
* persistent source associations such as playback-source track ID ↔ `LocalTrackId` ↔ resolved lyrics-source identity

Playback-source expansion must not place source-specific code in WPF renderer/editor logic.

---

## Milestone 18 — Distribution and Product Polish

Prepare a user-facing release rather than a development checkout:

* installer and reliable Native Messaging Host registration
* signed/distributed browser extension flow where required
* optional auto-launch preference
* user-configurable hotkeys built on stable command IDs/scopes
* provider-management polish and diagnostics
* final accessibility/keyboard/error-state/UI consistency work
* release packaging, upgrade/uninstall behavior, and remaining product-level cleanup

M18 is a release/polish milestone, not a dumping ground for architectural features that should have been completed earlier.

The milestone map is intentionally revisable. M12–M16 describe the current core-product path; M17–M18 are later expansion/release work and may be split if one area becomes large enough to deserve its own milestone.
