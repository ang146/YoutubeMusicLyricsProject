# Future Features and Roadmap

This document collects agreed future directions that should not be silently folded into earlier milestones. It is a roadmap, not a promise that every item ships in the listed order. Existing local-first authority, protocol boundaries, playback ownership, and user-controlled editing rules remain the foundation.

## Current milestone status

Milestones 1–10 are complete through transport, YouTube Music integration, local-first storage, timeline/overlay, timing adjustment, overlay interaction, and external/untimed local editing.

Milestone 11 — Built-in Lyrics Editor Foundation is currently implemented and under manual acceptance/refinement. The editor architecture and current acceptance contract are documented in [BUILT_IN_EDITOR.md](BUILT_IN_EDITOR.md).

## Desktop surface and Settings restructuring

After the M11 editor foundation is stabilized, the existing diagnostic/control-panel `MainWindow` should be restructured into clearer user-facing surfaces rather than continuing to accumulate unrelated controls.

Target surface model:

```text
Main Lyrics Window
├─ normal focusable non-Topmost window
├─ full-document All Lyrics renderer only
├─ no One/Two/All content-mode selector
├─ shared lyrics context menu
├─ Settings / Built-in Editor / Media Controller actions
└─ compact transport-status footer

Desktop Lyrics Overlay
├─ permanently Topmost whenever visible
├─ temporary Topmost suppression while modal editor is open
├─ OneLine / TwoLines / bounded contextual AllLyrics
├─ click-through / lock / drag / resize
└─ shared lyrics context menu

Media Controller
├─ independent show/hide window
├─ title / artist / playback state
├─ transport + progress
└─ optional docking beside the overlay

Built-in Lyrics Editor
├─ lyrics/timestamp authoring
├─ metadata overrides
└─ all timing-adjustment UI

Settings
├─ General
├─ Lyrics Overlay
├─ Lyrics Window
├─ Media Controller
├─ Editor
└─ Debug
```

The Main Lyrics Window's All Lyrics presentation is not the overlay's current `AllLyrics` content mode. The main window is intended to expose the complete lyrics document for reading; the overlay `AllLyrics` mode remains a geometry-bounded contextual subset around the current timeline occurrence.

Settings navigation should use a persistent left sidebar with one independent section page displayed on the right. It should not be implemented as one long vertically connected settings document where sidebar selection merely scrolls to anchors.

Developer-oriented information should leave the Main Lyrics Window. The Debug settings page is the intended home for transport/source diagnostics, `LocalTrackId`, library/index paths, raw playback/lyrics/sidecar JSON, provider diagnostics, **Open Logs**, and **Open Crash Reports**. Ordinary track title/artist, playback position/state, and media transport belong to the Media Controller instead.

All timing-adjustment controls should converge on the Built-in Lyrics Editor: global offset adjustment/reset, Bake, current-line/exact-occurrence timing edits, and playback-assisted timestamp authoring. Existing MainWindow/overlay timing controls are transitional UI and should be removed when this restructuring is implemented; the established M8 persistence and safety semantics remain authoritative.

The Main Lyrics Window and Desktop Lyrics Overlay should use shared application commands for common lyrics actions and should converge on the same right-click lyrics menu where an action applies to both. Overlay-only interaction settings such as content mode, click-through, lock, drag/resize behaviour remain surface-specific.

## Editor expansion after M11 foundation

Potential editor commands/features include:

- richer add/remove/reorder management for existing timestamp occurrences beyond the M11 row-level playback-time command
- explicit `♪` break insertion, removal, and retiming
- bulk timing shift/transform tools
- multi-row selection and bulk operations
- import/export helpers
- improved timestamp formatting/input helpers
- richer search/replace
- user-selected script conversion of editor text

### Traditional / Simplified Chinese conversion in the editor

An editor conversion is an explicit destructive buffer transformation initiated by the user:

```text
Editor buffer
→ Convert Lyrics to Traditional / Simplified
→ user reviews result
→ normal Save required
```

It should be undoable and should modify lyric text only unless an explicit command says otherwise. Timestamps, line order, and multi-timestamp structure remain intact.

The app should not perform regional wording localization. The goal is script conversion, not rewriting lyrics for Hong Kong/Taiwan/Mainland vocabulary.

## Presentation-only script conversion

A separate later presentation preference may convert lyric text only for rendering:

```text
Original
Traditional
Simplified
```

This is non-destructive and must not modify the authoritative LRC or editor buffer. It belongs with appearance/presentation settings, not the M11 editor foundation.

## Customisable hotkeys

The command architecture should later allow users to change shortcuts without changing command behaviour.

Scopes should remain distinct:

```text
Global
Application
Editor
```

Examples:

- global/application: show/hide overlay, toggle click-through
- editor: next/previous row, insert row, Set Current Playback Time, save, undo/redo

Context menus and other command surfaces should display the current binding where useful. The hotkey editor/settings UI is deferred until after M11 foundation.

## Karaoke and explicit breaks

The canonical portable break marker remains:

```lrc
[timestamp]♪
```

Breaks are explicit; elapsed gap length alone must never create one.

Future work may include:

- break-aware end-of-line semantics
- sung/unsung progressive karaoke rendering
- preparation/count-in cues during sufficiently long explicit breaks
- karaoke alternating layout
- per-character/word timing if a future source format supports it
- long-line wrapping or horizontal pan with configurable lead/tail holds

## Appearance and renderer customisation

Potential preferences include:

- font family
- font size
- font weight
- Current / Upcoming / Past colours
- opacity
- alignment
- outline/shadow
- line spacing
- background opacity
- role/distance styling controls
- Original / Traditional / Simplified display conversion

Content mode, layout style, appearance, and karaoke animation remain separate concepts.

## Media Controller

A future optional Media Controller is an independent window that may be shown/hidden separately and optionally dock beside the Desktop Lyrics Overlay on the left or right.

Potential display/actions:

```text
Title / Artist
Previous
Play / Pause
Next
Elapsed / Duration
Seekable progress
Open Built-in Editor
```

`Open Built-in Editor` is the same application command used by the current MainWindow/future Main Lyrics Window and overlay context menu; the Media Controller must not implement a separate editor-opening path.

Playback display should reuse the existing Lyrics Displayer playback model. Transport commands should use a provider-independent `MediaControlService` and fail safe when Windows media-session matching is ambiguous. Do not assume `GetCurrentSession()` is the tracked YouTube Music session.

## Additional lyrics providers and manual search

Milestone 12 remains the provider/search expansion milestone. Potential sources include LRCLib, community lyrics services, and provider-specific adapters for services whose native formats are not ordinary LRC.

Playback source and lyrics provider are separate concepts:

```text
Where music is playing
!=
Where lyrics came from
```

For example, YouTube Music may remain the playback source while the chosen lyrics are imported from LRCLib, another community/provider adapter, or a configured custom HTTP source. Provider-specific formats must normalize into a Lyrics Displayer canonical lyrics model before reaching editor/timeline/renderer code. The canonical model should be able to distinguish at least untimed, line-timed, and future word-timed capability without forcing provider-specific QRC/YRC/TTML/LRC syntax into presentation code.

A provider abstraction should expose stable identity, capabilities, search, fetch, and normalization responsibilities. Conceptually:

```text
ILyricsProvider
├─ Provider identity
├─ Capabilities
├─ SearchAsync(...)
├─ FetchLyricsAsync(...)
└─ Normalize(...)
```

Provider configuration should eventually support enable/disable, priority/order, provider-specific settings, and bounded timeouts. One provider failing or timing out must not prevent results from other enabled providers.

Search should fan out to enabled providers in parallel and support cancellation when the user changes the query. Results may appear progressively as providers return. Search results should initially be lightweight metadata rather than eagerly downloading every lyric payload. Selecting a result lazily fetches that result for preview, and fetched previews may be cached by provider/result identity.

Conceptually:

```text
Search enabled providers
        ↓
metadata results
        ↓ select one result
lazy FetchLyrics
        ↓
Preview
        ↓ explicit user action
Use This Lyrics
        ↓
normalize + import local authoritative copy
```

Preview must not mutate the local library. Importing/replacing local lyrics is an explicit user action, and replacing an existing authoritative local LRC requires clear confirmation. After import, the local copy remains authoritative and must never be silently overwritten by later provider fetches.

Manual search should default to effective metadata but allow one-off search text without forcing persistent metadata changes. User title/artist overrides may optionally be saved when explicitly requested.

Custom sources should begin as declarative HTTP integrations for supported response shapes such as raw LRC or mapped JSON fields. Do not execute arbitrary user-provided scripts merely to support custom providers. More complex authenticated/encrypted/provider-native formats belong in built-in provider adapters.

## Original imported lyrics snapshot and reset

A future safety/authoring feature should preserve an immutable snapshot of the lyrics as originally imported/materialised from a provider or other source, separate from the editable authoritative local LRC.

Conceptually:

```text
Imported Source Snapshot   // immutable baseline
        +
Editable Authoritative Local LRC
```

Normal editing operations must not mutate the original snapshot. This includes built-in editor saves, external editor saves, Current Line Adjustment, Bake, future script conversion, bulk timing tools, and other local modifications.

A future explicit command may provide:

```text
Reset Lyrics to Original...
```

Reset is destructive to the editable authoritative LRC and therefore requires clear confirmation. Provider re-fetches must not silently redefine what "Original" means. Replacing/re-importing the source snapshot should itself be an explicit user action. The exact portable storage representation for this snapshot is intentionally deferred until the feature is implemented.

## Additional browser platforms

Chromium support is deliberately late-stage platform expansion.

The preferred architecture is shared source logic with thin browser-runtime adapters rather than independent copies that drift:

```text
Browser Extension
├─ shared YouTube Music/source logic
├─ Firefox runtime/manifest adapter
└─ Chromium runtime/manifest adapter
```

Chrome, Edge, Brave, and similar Chromium browsers should reuse the same source detection, lyrics retrieval, ownership, and protocol logic where platform APIs permit it.

Browser host/runtime is separate from playback source identity:

```text
Playback source: YouTube Music
Browser host: Firefox / Chromium
```

Chromium support should be tackled after the core Windows app, editing, installer/distribution, and major presentation work are stable.

## Extension distribution and installation

Development may continue using Firefox temporary add-ons. Production distribution should eventually include a signed Firefox extension and a reliable Native Messaging Host installation/registration flow.

Installer/distribution work should keep browser-extension installation/confirmation separate from Windows app/native-host installation where browser security models require explicit user approval.

## Additional playback sources

Future playback-source adapters may be added only after the source-independent app/core boundaries remain stable. New adapters should feed the existing playback/lyrics protocol rather than adding provider-specific logic directly to WPF presentation code.

## Feature-boundary rules

Future work should preserve these boundaries:

```text
Lyrics source data
≠ local authoritative user data

Playback source
≠ lyrics provider

Playback current line
≠ editor selection

Editor command
≠ button/context-menu/hotkey binding

Content mode
≠ layout style
≠ appearance
≠ karaoke animation

Display conversion
≠ destructive editor conversion

Browser host
≠ playback source
```

These separations are intended to keep later features additive instead of forcing rewrites of the earlier milestones.
