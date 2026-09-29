# Built-in Lyrics Editor

Milestone 11 introduces a built-in, single-track lyrics editing workspace. The editor is a modal dialog owned by `LyricsDisplayer.App`; it edits one fixed local track at a time and reuses the local-first LRC/sidecar authority established by earlier milestones.

The editor is not a playback surface and playback is not allowed to mutate editor state. It may read current playback position only when the user invokes an explicit editor command such as **Set Current Time as Timestamp**.

## Current implementation notes (manual acceptance pending)

The M11 implementation adds a WPF modal dialog, a shared application-scoped `EditorCommand` for Control Panel and overlay entry points, and a WPF-independent editor document/buffer in `LyricsDisplayer.Core`. The document stores ordered physical lines with their original line endings; unchanged entries serialize from their original text, while edited/new lyric rows serialize from structured text and timestamp occurrences. Malformed or unsupported physical lines remain opaque and diagnostic rather than being dropped.

The editor loads the current authoritative disk LRC (not the runtime last-known-good snapshot), and uses `LyricsLibrary` content hashes plus guarded atomic replacement for saves. A clean editor reloads external changes; dirty conflicts retain the buffer and offer reload, overwrite, or cancel. LRC and portable sidecar files are watched while the dialog is open, and save still performs a content-identity check if notifications are missed. The application close path asks the dialog to resolve its dirty state before shutdown. The dialog is owned by the Control Panel even when that window is hidden, so opening from the overlay does not show the Control Panel.

Validation is advisory: malformed timestamp text remains editable, is marked on its grid cell and listed in diagnostics, and does not disable Save. The initial grid exposes every existing timestamp occurrence plus one empty occurrence column; it does not yet provide dedicated add/remove/reorder occurrence commands. Unsupported physical lines are preserved but not directly editable. Runtime reload remains the existing M10 watcher’s responsibility. Automated tests and builds are not a substitute for the manual acceptance checklist below; this milestone is ready for manual review, not marked fully accepted.

## Entry points and shared command

Opening the editor is an application command, not Control Panel-specific UI logic. Any user surface may invoke the same command when the current local track is editable.

Initial/future entry points include:

- Control Panel
- Desktop Lyrics Overlay context menu
- future Media Controller surface
- other explicit application surfaces added later

Conceptually:

```text
OpenBuiltInEditorCommand
        ↓
Built-in Editor Dialog
```

Each surface binds to the same command and command-state rules. No surface owns editor business logic.

Only one built-in editor dialog is active at a time. It is modal relative to the Control Panel/application owner window so a forgotten background editor cannot silently coexist with another editing session. Playback, NativeHost communication, the overlay, and the playback clock continue while the dialog is open.

## Fixed editor track

An editor session is pinned to the track that was opened:

```text
EditorTrack
EditorDocument
EditorSelection
DirtyState
```

Changing playback to another song does not close the editor, switch its document, change its selection, or prompt the user to leave. The editor continues to represent its original `LocalTrackId` until explicitly closed.

Playback current line and editor selection are independent concepts:

```text
Playback Current Line ≠ Editor Selected Line
```

The user may edit the final lyric row while playback is at the beginning of the song. Playback progression may provide non-mutating visual information if useful, but it must never move the editor selection or edit the buffer.

## Playback-assisted timestamp command

The editor may read `PlaybackClock.Position` through an explicit command:

```text
SetSelectedTimestampFromPlayback
```

The command is enabled only when the currently playing track identity matches the fixed `EditorTrack`. If playback switches to another track, the editor remains fully usable but this command is disabled. Switching playback back to the editor's track re-enables it.

Initial semantics:

- if the selected timestamp cell exists, replace that selected timestamp with the current playback position
- if the selected row has no timestamp, create its first timestamp from the current playback position
- do not guess whether the user wants a second timestamp when one already exists

A future command may explicitly add the current playback time as an additional timestamp occurrence.

## Editor document model

The editor must not reduce an LRC file to a simple `List<(timestamp, text)>`. It needs a structured editable buffer that can preserve real LRC structure and future extensions.

Conceptually:

```text
EditorDocument
├─ stable document/file identity
├─ ordered physical entries
├─ metadata / unknown / blank structure
├─ editable lyric rows
├─ dirty state
├─ validation diagnostics
└─ undo/redo history

EditorLyricRow
├─ EditorLineId
├─ LyricsText
├─ Timestamps[]
├─ source/physical-line identity
└─ validation state
```

`EditorLineId` is editor-buffer identity, not the row index. Inserting a row must not redefine every following row's identity.

The model supports zero or more timestamps per lyric row from the beginning. Existing multi-timestamp physical lines must round-trip without loss even if the first UI version exposes only a limited set of management commands.

Unedited LRC structure should be preserved as far as practical. Metadata, unknown harmless tags, blank lines, and multi-timestamp physical lines must not be silently discarded or reordered merely because the file passed through the built-in editor.

## Row operations

Core editor commands include:

```text
Insert Row Above
Insert Row Below
Append Row
Delete Row
```

New rows contain no inferred timing:

```text
LyricsText = ""
Timestamps = []
```

Lyrics timing is never interpolated from neighbouring rows. A new line between 10 s and 20 s does not receive 15 s automatically. The user must type a timestamp or explicitly assign the current playback time.

Document order is user-controlled. The editor does not automatically sort rows by timestamp. Out-of-order timing may be shown as a diagnostic, but saving does not silently reorder the document.

## Selection and spreadsheet-style navigation

The editing surface is row/cell-oriented, similar to a compact spreadsheet-style lyrics editor.

Selection state is explicit:

```text
EditorSelection
├─ SelectedRowId
├─ SelectedColumn
└─ SelectedTimestampIndex?  // where applicable
```

Initial keyboard behaviour should follow familiar grid conventions:

- `Enter`: commit the current cell and move to the next row in the same logical column
- `Shift+Enter`: move to the previous row in the same logical column
- `Tab`: move to the next editable column/cell
- `Shift+Tab`: move to the previous editable column/cell
- arrow keys: normal row/cell navigation where the grid control permits it

Exact command bindings remain presentation/input configuration, not editor-domain logic.

## Command architecture

Editor operations are commands first. Buttons, context menus, and keyboard shortcuts bind to the same command instances or command IDs.

Examples:

```text
OpenBuiltInEditorCommand
InsertRowAboveCommand
InsertRowBelowCommand
AppendRowCommand
DeleteRowCommand
SetTimestampFromPlaybackCommand
UndoCommand
RedoCommand
SaveCommand
```

Commands expose appropriate `CanExecute` state. For example, row-relative commands require a selected row while Append may remain available with no row selected.

A row context menu should expose the same operations as other bindings, for example:

```text
Set Current Time as Timestamp
--------------------
Insert Row Above
Insert Row Below
Append Row
Delete Row
--------------------
Undo
Redo
```

Future commands may add/remove additional timestamp occurrences, insert explicit break markers, convert script, or perform bulk timing operations without changing the command-binding architecture.

## Future custom hotkeys

Milestone 11 should avoid hard-wiring editor behaviour directly to key-event code. Commands should have stable IDs and an input scope so later user-configurable shortcuts can remap bindings without rewriting editor logic.

Conceptually:

```text
HotkeyScope
├─ Global
├─ Application
└─ Editor
```

Editor navigation keys such as Enter/Tab are editor-local. Existing overlay visibility/click-through shortcuts are global/application shortcuts. The settings UI for custom hotkeys is a later feature, not an M11 requirement.

## Validation policy

Built-in editor validation is advisory, not permission enforcement.

Malformed or suspicious cells may use a clear visual diagnostic such as a red background and tooltip/message, but the user remains allowed to save the exact content they chose.

```text
Validation = awareness
not permission
```

This preserves user ownership of the local file. M10 runtime reload safety remains separate: if a saved file contains fatal malformed timestamp syntax, active playback may retain its last-known-good runtime lyrics while the file remains exactly as the user saved it.

The editor must not act as an automatic corrector. It must not silently repair, reorder, interpolate, normalize, or regionalize lyric content.

## Dirty state and closing

The editor owns a dirty buffer independent of playback track changes.

Playback changing tracks does not prompt, save, discard, or close the editor.

Closing the editor, or exiting the application while an editor has unsaved changes, requires a normal close guard:

```text
Save
Discard
Cancel
```

`Cancel` aborts the close/exit request.

## External-file conflict

The editor records the file/content identity from which its buffer was loaded. If the authoritative LRC changes externally while the editor has unsaved changes, the editor must never silently overwrite either side.

Keep the conflict UX deliberately simple:

```text
Discard My Changes / Reload External Version
Overwrite External Changes
Cancel
```

No three-way merge, per-line merge UI, or collaborative editing system is required for M11. More advanced conflict handling may be added later if there is a real need.

## Safe save and M10 integration

Saving uses the same local-first authority and safe-write principles already used by timing adjustment and external editing. A successful save updates the authoritative local LRC/portable metadata and then lets the existing reload/timeline path observe the new file state.

The editor may intentionally save content that carries validation errors; that decision belongs to the user. Safe save refers to file replacement/concurrency integrity, not forbidding user-authored content.

An important end-to-end workflow is:

```text
untimed local LRC
→ open Built-in Editor
→ select row
→ play song to desired moment
→ Set Current Time as Timestamp
→ Enter to next row
→ repeat
→ Save
→ M10 reload
→ document becomes timed
→ timeline/overlay begin synchronized presentation
```

## Title and artist overrides

The editor header may edit the current local track's persistent user metadata overrides:

```text
UserTitle
UserArtist
```

Source title/artist should remain visible for reference, with a clear way to remove an override and fall back to the source value.

Overrides remain authoritative in `track.lyrics.json` according to the existing effective-metadata rule:

```text
EffectiveTitle  = UserTitle  ?? SourceTitle
EffectiveArtist = UserArtist ?? SourceArtist
```

The editor must not create a second metadata authority by silently writing these overrides into LRC `[ti:]` / `[ar:]` tags. A future explicit export operation may choose to do that, but it is not M11 behaviour.

## Modal lifecycle

The built-in editor is a modal editing workspace, not another permanent application window.

While open:

- playback and `PlaybackClock` continue
- NativeHost/browser communication continues
- the desktop overlay continues updating normally
- the Control Panel cannot open another editor instance
- the editor remains pinned to its own track even if playback changes

Application shutdown must route through the editor's dirty close guard before final exit.

## Milestone 11 scope

M11 is the editor foundation, not the final form of every editing feature.

Required foundation:

- modal single-track editor session
- shared application command entry point
- structured round-trippable `EditorDocument`
- stable row identity
- row/cell selection independent of playback
- lyric text editing
- timestamp editing
- zero/multiple-timestamp-capable model
- insert above / insert below / append / delete
- no synthetic timestamps for new rows
- Set Current Time as Timestamp with track-identity safety
- spreadsheet-style navigation
- command-driven buttons/context menu/keyboard bindings
- undo/redo
- dirty state
- advisory validation
- safe save
- simple external-file conflict handling
- close/exit Save / Discard / Cancel
- title/artist user overrides

Deferred features are tracked in [FUTURE_FEATURES.md](FUTURE_FEATURES.md).
