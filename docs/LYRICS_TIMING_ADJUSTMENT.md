# Lyrics Timing Adjustment

Milestone 8 introduced timing correction before the built-in editor existed. Its original workflow used a per-track sidecar `GlobalOffsetMs`, Reset, Bake, and a separate current-line quick adjustment.

Milestone 12 Task 6 supersedes that user-facing authoring model. Timing changes now belong to the Built-in Lyrics Editor and directly edit the current `EditorDocument`, using the editor's existing dirty state, Undo/Redo history, Save/Discard flow, validation, and safe-write path.

## Current authoring model

The editor exposes compact timing controls:

```text
00:27.137 | current lyric text

Shift All Timestamps
[-0.5s] [-0.1s] [+0.1s] [+0.5s]

Selected Line
[-0.5s] [-0.1s] [+0.1s] [+0.5s]

[Set Time]
```

There is no user-facing Global Offset / Reset / Bake workflow in the intended architecture.

## Shift All Timestamps

A Shift All action applies the chosen delta directly to timestamp occurrences in the editor document.

One button press:

- is one logical Undo/Redo unit
- marks the editor document dirty
- updates the editor's live preview/runtime projection
- is persisted only when the user performs the normal Save
- is discarded by the normal Discard flow

The operation is atomic with respect to negative-time safety. Ordinary timestamps must never be silently clamped to zero. If a non-protected timestamp would become negative, the shift is rejected as a whole.

The existing exact-zero semantic anchor rule remains narrow: an exact `00:00.000` occurrence on intrinsic blank/whitespace lyric text or an explicit semantic break marker such as the current default `♪` may remain anchored at zero for a negative shift. This is not a general `max(0, value)` rule. Other timestamp occurrences on the same row still shift normally and can still make the whole operation invalid.

Positive shifts do not receive special zero-anchor treatment.

## Selected Line

Playback current lyric and editor selection are separate concepts:

```text
Playback Current Line != Editor Selected Line
```

The Selected Line controls operate on the manually selected editor row. Selecting anywhere on that row is sufficient; the user does not need to select a particular timestamp cell.

The chosen delta applies to every timestamp occurrence on that row as one logical edit. Playback progression must never move the DataGrid selection or silently retarget the operation.

Any resulting chronology issue remains visible through the editor's existing diagnostics/validation model. The timing command must still respect hard safety rules such as invalid negative timestamps.

## Set Time

`Set Time` is playback-assisted authoring for the selected row. It is enabled only when the currently playing track identity matches the fixed editor track.

It appends the current playback position to the first free timestamp lane:

```text
0 timestamps -> T1
1 timestamp  -> T2
2 timestamps -> T3
3 timestamps -> T4
4 timestamps -> T5
5 timestamps -> disabled
```

It never overwrites an existing timestamp occurrence.

Switching playback to another track does not close or switch the editor. It only disables playback-dependent timing authoring. Switching back to the fixed editor track re-enables it.

## Live playback line

The editor shows a compact single-line playback indicator:

```text
00:27.137 | lyric text
```

The lyric text reflects the editor's current in-memory document, including committed unsaved text edits. This live display is informational only and does not drive selection.

If playback does not match the fixed editor track, the UI must not display a misleading lyric as though it belonged to the editor session.

## Persistence and authority

Timing edits are ordinary edits to the authoritative local LRC buffer. They do not create a second timing authority.

```text
EditorDocument change
        ↓
Dirty / Undo / Redo
        ↓
Save
        ↓
Authoritative local LRC
        ↓
Existing reload / timeline / presentation path
```

SQLite remains an index/cache and does not become timing authority.

The project is still in pre-release development. Superseded development-state timing mechanisms do not require migration or compatibility machinery unless explicitly requested. New work should follow the editor-document timing model rather than reintroducing sidecar offset authoring.

## Relationship to other surfaces

The ordinary Main Lyrics Window and Desktop Lyrics Overlay are presentation surfaces, not timing-authoring surfaces.

Timing controls belong in the Built-in Lyrics Editor. Shared context menus may offer **Open Built-in Editor**, but should not recreate a second timing workflow.

## Semantic break markers

A long timestamp gap alone never implies a break.

Blank/whitespace lyric text is intrinsically semantic-empty. The default explicit portable break marker remains:

```lrc
[00:13.000]♪
```

A later preference may define additional exact trim-matched marker strings. The same semantic classifier should be reusable by negative-shift zero-anchor safety, break-aware rendering, and future preparation-cue logic. Core semantics must not read Settings directly.
