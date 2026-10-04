# Future Features and Roadmap

This document collects agreed future directions that should not be silently folded into earlier milestones. It is a roadmap, not a promise that every item ships in the listed order. Existing local-first authority, protocol boundaries, playback ownership, and user-controlled editing rules remain the foundation.

## Current milestone status

Milestones 1–11 are complete through transport, YouTube Music integration, local-first storage, timeline/overlay, timing adjustment, overlay interaction, external/untimed local editing, and the manually accepted Built-in Lyrics Editor Foundation. The editor architecture and accepted M11 contract are documented in [BUILT_IN_EDITOR.md](BUILT_IN_EDITOR.md).

The post-M11 roadmap has expanded because the intended product now has clearer desktop surfaces, provider/search workflows, richer authoring tools, renderer customization, media controls, and additional playback-source goals. The current planning map is:

```text
M12  Application UI Foundation + Desktop Surface Restructure
M13  Lyrics Search + Provider Framework
M14  Editor Productivity + Semantic Breaks
M15  Renderer + Appearance + Karaoke Presentation
M16  Media Controller
M17  Playback Source + Browser Expansion
M18  Distribution + Product Polish
```

This numbering is planning guidance, not a promise that later expansion milestones can never split again. M12–M16 form the current core-product path; M17–M18 are later expansion/release work.

## Desktop surface and Settings restructuring

Milestone 12 performs the restructuring now that the M11 editor foundation is accepted. The existing diagnostic/control-panel `MainWindow` should become clearer user-facing surfaces rather than continuing to accumulate unrelated controls. Before/while doing this, introduce a small reusable ViewModel foundation (`INotifyPropertyChanged`, `SetProperty`, dependent-property notification and selective command invalidation) and migrate incrementally; do not create a global property-bag/god ViewModel or require a wholesale rewrite.

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
- explicit break insertion, removal, and retiming
- **Fill Timestamp Pattern Down** for repeated sections such as choruses
- bulk timing shift/transform tools
- multi-row selection and bulk operations
- import/export helpers
- richer search/replace
- user-selected script conversion of editor text

### Fill Timestamp Pattern Down

When one timestamp lane already describes a repeated section and the user has anchored the first cell of another lane, the remaining target cells can reuse the reference lane's relative spacing:

```text
target[row] = target[anchor] + (reference[row] - reference[anchor])
```

The first implementation should remain explicit and predictable:

- operate only on the contiguous range selected/dragged by the user; do not auto-detect chorus boundaries
- fill blank target cells only by default; preserve existing user-authored target timestamps
- skip rows whose reference-lane timestamp is missing rather than failing unrelated rows
- allow the same mechanism to work between any applicable timestamp lanes rather than hard-coding only T1 → T2
- preserve exact millisecond deltas
- record one command/Undo unit for the whole fill
- refresh validation after the fill

The core/domain command should not depend on an Excel-style mouse gesture. A context-menu/command UX may ship first, while a later cell fill-handle drag UI calls the same command.

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

## User-configurable semantic break markers

Blank/whitespace lyric text is intrinsically semantic-empty. The default explicit break marker is:

```text
♪
```

A later Settings preference should allow the user to define additional explicit marker strings such as `♫`, `[Music]`, or `間奏`. The initial matching rule should remain deliberately simple and safe:

```text
trim surrounding whitespace
+ exact ordinal match
```

Do not begin with regex or wildcard matching. A user-defined marker changes how existing lyrics are interpreted at runtime; it does not rewrite the authoritative LRC. Removing a custom marker similarly changes classification without mutating lyric text.

The semantic rule is shared application logic rather than a Renderer-only or Bake-only preference. Conceptually:

```text
Preferences effective marker set
        ↓
shared break-marker classifier
        ├─ negative-Bake zero-anchor protection
        ├─ renderer break semantics
        └─ preparation / pre-show cue logic
```

Core semantics should not reach upward to read Settings directly. Blank/whitespace remains intrinsic, while Settings supplies the effective explicit-marker set to a pure classifier/helper. The current default set contains `♪`.

Physical blank LRC rows and timed semantic break markers remain distinct concepts: a physical blank is document structure, while `[timestamp]♪` (or another configured explicit marker) is a timed semantic break.

## Karaoke and explicit breaks

The preferred/default portable break marker remains:

```lrc
[timestamp]♪
```

User-configured semantic markers may classify other exact lyric strings as breaks without rewriting the file. Breaks are explicit; elapsed gap length alone must never create one.

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

Milestone 13 is the provider/search expansion milestone. Playback source and lyrics provider are separate concepts:

```text
Where music is playing
!=
Where lyrics came from
```

Provider-specific formats normalize into a Lyrics Displayer canonical lyrics model before reaching editor/timeline/renderer code. The canonical model should distinguish at least Untimed, LineTimed, and future WordTimed capability. A provider boundary should expose stable identity, capabilities, search/fetch, cancellation and normalization responsibilities; provider configuration should support enable/disable, priority/order, bounded timeouts, and provider-specific settings without letting one failing provider stall unrelated work.

### YouTube Music candidate resolution

The first practical search/resolution path should exploit the existing YouTube/YouTube-Music identity:

1. If playback already supplies a known YouTube `videoId`, probe that candidate directly first where applicable.
2. If direct resolution fails or a manual metadata search is requested, search using effective or one-off title/artist values and keep a bounded initial pool of roughly 5–10 YouTube Music candidates.
3. Candidate discovery is not the same as a visible Lyrics Search result. Probe lyric availability lazily/sequentially, initially around one candidate per second.
4. Only candidates whose lyric fetch succeeds are appended to the visible results. Candidates with no lyrics are silently skipped.
5. Do not invent a match/confidence percentage. Use the provider/search ranking and useful metadata directly.
6. Do not expand indefinitely just to fill a result list. If the bounded candidate pool yields no usable lyrics, show **No lyrics found**.
7. Changing track/query or choosing **Use This Lyrics** cancels remaining probes; stale results from an older search generation are ignored.

Visible results should focus on:

```text
Title
Artist
Album (optional)
Duration
Lyrics Source
Timed / Untimed
Preview
Use This Lyrics
```

Preview must not mutate the local library. **Use This Lyrics** is the explicit import point and cancels remaining unnecessary probe work. Replacing an existing authoritative local LRC requires clear confirmation. Once imported and associated with a stable local track, the local copy is authoritative and later provider fetches must never silently overwrite user edits.

Other built-in providers may include LRCLib and community/provider-native adapters. Cross-provider discovery may run providers independently/progressively, but a provider is free to use its own bounded candidate-probing strategy such as the YouTube Music flow above.

Manual search should default to effective metadata while allowing one-off query edits without forcing permanent metadata changes; saving `UserTitle`/`UserArtist` remains explicit. Custom sources should begin as declarative HTTP integrations for supported raw-LRC/mapped-JSON shapes. Do not execute arbitrary user-provided scripts merely to support custom providers.

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

Milestone 17 expands playback sources only after source-independent app/core boundaries and the M13 lyrics-resolution/provider pipeline are stable. Playback source remains independent from lyrics provider.

Likely adapters include:

- ordinary YouTube tabs in Firefox; reuse the video's `videoId` for a direct YouTube Music lyric probe where possible, then fall back to the normal metadata search/resolution flow
- Chromium-hosted YouTube/YouTube Music through shared source logic plus a thin browser-runtime adapter
- Spotify as a playback source only, not a lyrics provider; track title/artist/album/duration/IDs feed the existing local association and provider-search pipeline
- other local/Windows media sources where a stable enough identity can be established

Persistent association should remain conceptually source-neutral:

```text
Playback-source track identity
↔ LocalTrackId
↔ imported/resolved lyrics associations
```

New adapters should feed the existing playback/application model rather than adding source-specific logic directly to WPF presentation code.

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
