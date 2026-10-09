# Future Features and Roadmap

This document collects agreed future directions that should not be silently folded into earlier milestones. It is a roadmap, not a promise that every item ships in the listed order. Existing local-first authority, protocol boundaries, playback ownership, and user-controlled editing rules remain the foundation.

## Current milestone status

Milestones 1–11 are complete through transport, YouTube Music integration, local-first storage, timeline/overlay, timing authoring, overlay interaction, external/untimed local editing, and the manually accepted Built-in Lyrics Editor Foundation. M12 implementation is integrated, including the application/ViewModel foundation, shared lyrics presentation, Main Lyrics Window, Settings and Debug pages, editor timing authoring, shared lyrics-surface actions/context menus, centralized UI strings, and Main Window view decomposition. Final developer acceptance for M12 remains pending; this status is not an acceptance declaration. M13 remains planned and has not started.

Repository-root `AGENTS.md` records the mandatory coding-agent conventions. M12's application-infrastructure work is implemented: Unity composition root, explicit constructor injection, ViewModel interfaces, focused runtime factories, typed/category logging, generic command infrastructure, application exception handling, and NSubstitute-based tests.

The broader planning map remains:

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

M12 establishes the main desktop surfaces and the ViewModel/presentation foundation. The former diagnostic/control-panel `MainWindow` is now the user-facing Main Lyrics Window; its shell composes separate header, lyrics, and status views over one ViewModel. Settings is a separate shell with page views, and technical state lives under Settings > Debug rather than accumulating in the main lyrics surface.

Current/target surface model:

```text
Main Lyrics Window
├─ normal focusable non-Topmost window
├─ full-document lyrics renderer only
├─ no One/Two/All content-mode selector
├─ current-line auto-follow with temporary manual-scroll override
├─ effective title / artist header
├─ shared lyrics actions
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
└─ full-document and selected-row timing authoring

Shared lyrics-surface context menus
└─ Current Line / All Lyrics immediate authoritative-LRC timing corrections

Settings
├─ fixed left navigation + independent right-side page views
├─ General
├─ Lyrics Overlay
├─ Lyrics Window
├─ Media Controller
├─ Editor
└─ Debug (live diagnostics / paths / logs / prettified raw snapshots)
```

The Main Lyrics Window's All Lyrics presentation is not the overlay's current `AllLyrics` content mode. The main window is intended to expose the complete lyrics document for reading; the overlay `AllLyrics` mode remains a geometry-bounded contextual subset around the current timeline occurrence.

Current Main Lyrics Window follow behaviour centers the current lyric where document bounds permit. Manual scrolling suspends follow and roughly five seconds of inactivity recenters the then-current lyric. Future Lyrics Window preferences may refine this with viewport edge whitespace so the first/last lyric can also sit at the chosen anchor position.

Long track metadata currently trims safely instead of overlapping action buttons. Future title-overflow options may include:
- Ellipsis / no animation
- slow pan with start/end holds and eased speed
- continuous marquee with a gap before the title repeats

Future input preferences may also choose whether mouse wheel/dragging scrolls lyrics or controls playback progress; these are later behaviour settings, not current M12 requirements.

Settings navigation now uses a persistent left sidebar with one independent section view displayed on the right; `SettingsWindow` is only the shell/host. Each section has its own view under `Views/Settings/`.

Developer-oriented information has left the Main Lyrics Window. The Debug page exposes live track/source/storage/transport/playback/lyrics-provider state, library/index/log/crash paths, folder-open actions, and prettified raw playback/lyrics snapshots. The Main Lyrics Window may show compact effective title/artist metadata; richer playback transport remains future Media Controller responsibility.

Full-document timing authoring has converged on the Built-in Lyrics Editor. `Shift All Timestamps` and `Selected Line` directly edit the editor document and participate in dirty/Undo/Redo/Save/Discard. `Set Time` appends the current playback position into the first free T1–T5 slot when playback identity matches the fixed editor track. Playback current lyric remains independent from editor selection. Shared lyrics-surface context menus expose Current Line and All Lyrics adjustments that persist directly to the current authoritative local LRC; they are not Editor-document authoring controls. There is no runtime offset or Reset action.

The Main Lyrics Window and Desktop Lyrics Overlay use shared application services/commands for common lyrics actions and separate right-click menus. Shared actions include LRC/editor entry points and Current Line / All Lyrics timing corrections; overlay-only items such as content mode, click-through, lock, drag/resize behaviour, and hide remain surface-specific, while Main Lyrics Window Settings stays Main-only.

## Application infrastructure conventions

M12 implements a dedicated infrastructure foundation before feature-heavy milestones continue:

```text
Unity composition root
        ├─ constructor injection
        ├─ IXxxViewModel -> XxxViewModel registrations
        ├─ typed/category ILogger<T>
        ├─ ICommandFactory
        │   ├─ RelayCommand
        │   └─ AsyncRelayCommand
        ├─ IExceptionHandler
        └─ runtime/session factories where creation parameters/lifecycle require them
```

Ordinary ViewModels and services must not call `Container.Resolve<T>()` or obtain the Unity container through a service locator. Resolve belongs at composition/factory/framework boundaries. Fixed object graphs use container constructor injection; runtime/session creation uses a focused factory.

Each main ViewModel exposes an `IXxxViewModel` contract. Tests of a ViewModel instantiate the concrete implementation; consumers should normally depend on/mock the interface. NUnit remains mandatory and NSubstitute is the preferred mocking library unless the repository already establishes another framework.

ViewModels receive `ILogger<ConcreteViewModel>` and pass it to `ViewModelBase`, which exposes a protected `ILogger Logger` while retaining the concrete category. Log lifecycle, command execution, meaningful user/application operations, state transitions, and failures; do not log every playback tick, `PropertyChanged`, or renderer refresh.

Commands are created through `ICommandFactory`; app-wide command infrastructure replaces the overly specific `EditorCommand` naming. Async commands are non-reentrant by default. Command failures should be logged and routed through application exception handling; the global crash reporter remains the last-resort fatal boundary.

These permanent coding-agent rules live in repository-root `AGENTS.md` so future task prompts can stay short.

## Editor expansion after M11 foundation

Potential editor commands/features include:

- richer add/remove/reorder management for existing timestamp occurrences beyond the M11 row-level playback-time command
- explicit break insertion, removal, and retiming
- **Fill Timestamp Pattern Down** for repeated sections such as choruses
- richer multi-row/bulk timing transforms beyond the current whole-document/selected-row shifts
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
- editor: next/previous row, insert row, Set Time, save, undo/redo

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

The semantic rule is shared application logic rather than a Renderer-only or timing-command-only preference. Conceptually:

```text
Preferences effective marker set
        ↓
shared break-marker classifier
        ├─ negative timestamp-shift zero-anchor protection
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

`Open Built-in Editor` is the same application command used by the Main Lyrics Window and overlay context menu; the Media Controller must not implement a separate editor-opening path.

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

Normal editing operations must not mutate the original snapshot. This includes built-in editor saves, external editor saves, selected-row/whole-document timestamp shifts, future script conversion, bulk timing tools, and other local modifications.

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

Application/editor command
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
