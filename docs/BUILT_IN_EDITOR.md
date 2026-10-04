# Built-in Lyrics Editor

Milestone 11 introduces a built-in, single-track lyrics editing workspace. The editor is a modal dialog owned by `LyricsDisplayer.App`; it edits one fixed local track at a time and reuses the local-first LRC/sidecar authority established by earlier milestones.

The editor is not a playback surface and playback is not allowed to mutate editor state. It may read current playback position only when the user invokes an explicit editor command such as **Set Current Playback Time**.

## Current implementation notes (accepted M11 foundation)

The M11 implementation adds a WPF modal dialog, a shared application-scoped `EditorCommand` for Control Panel and overlay entry points, and a WPF-independent editor document/buffer in `LyricsDisplayer.Core`. The document stores ordered physical lines with their original line endings; unchanged entries serialize from their original text, while edited/new lyric rows serialize from structured text and timestamp occurrences. Recognized metadata lines remain hidden but preserved. Every other physical line—including blank lines and unknown or malformed content—is an editor row; unknown or malformed content carries advisory diagnostics where applicable and remains unchanged unless edited.

The editor loads the current authoritative disk LRC (not the runtime last-known-good snapshot), and uses `LyricsLibrary` content hashes plus guarded atomic replacement for saves. A clean editor reloads external changes; dirty conflicts retain the buffer and offer reload, overwrite, or cancel. LRC and portable sidecar files are watched while the dialog is open, and save still performs a content-identity check if notifications are missed. The application close path asks the dialog to resolve its dirty state before shutdown. The dialog is owned by the Control Panel even when that window is hidden, so opening from the overlay does not show the Control Panel; the overlay itself is not made the modal owner. The overlay is Topmost whenever visible during normal operation, with only an explicit editor-modal suppression state temporarily lowering it. Legacy `overlay.topmost` settings are ignored and removed the next time settings are saved. Suppression is released only after `ShowDialog()` returns, so a cancelled close attempt leaves the editor above the overlay. On fatal or accepted application shutdown it remains suppressed until overlay teardown, avoiding a late Z-order raise during exit.

Validation is advisory: malformed timestamp text remains visible/editable, is listed in diagnostics, and does not disable Save. Cross-row chronology is validated independently per timestamp occurrence lane, while within-row occurrences are checked in displayed order. The grid exposes existing timestamp occurrences plus a spare editable occurrence column up to the current five-occurrence authoring limit; dedicated add/remove/reorder occurrence commands remain future work. Selected-cell Delete clears only that logical value through the editor command/Undo system, and timestamp edits normalize only when edit mode commits. Runtime reload remains the existing M10 watcher’s responsibility.

M11 is manually accepted. Real authoring was exercised across multiple songs, including creating timestamps for previously untimed lyrics, repeated multi-timestamp sections, metadata overrides, Delete/Undo/Redo, flexible timestamp input, save/reopen, and transition through M10 reload into timed overlay presentation. No blocking M11 issue is currently known.

## Entry points and shared command

Opening the editor is an application command, not Control Panel-specific UI logic. Any user surface may invoke the same command when the current local track is editable.

Initial/future entry points include:

- current MainWindow / future Main Lyrics Window
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

Only one built-in editor dialog is active at a time. It is modal relative to the application's ordinary MainWindow/owner window so a forgotten background editor cannot silently coexist with another editing session. Playback, NativeHost communication, the overlay, and the playback clock continue while the dialog is open.

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

The editor may read `PlaybackClock.Position` through one explicit row command:

```text
SetCurrentPlaybackTimeCommand
```

The command is enabled only when the currently playing track identity matches the fixed `EditorTrack`. If playback switches to another track, the editor remains fully usable but this command is disabled. Switching playback back to the editor's track re-enables it.

The command adds the current playback position to the selected lyric row using the first free timestamp slot. It is deliberately row-oriented rather than requiring the user to preselect an empty timestamp cell:

```text
0 existing timestamps -> Timestamp 1
1 existing timestamp  -> Timestamp 2
2 existing timestamps -> Timestamp 3
3 existing timestamps -> Timestamp 4
4 existing timestamps -> Timestamp 5
5 existing timestamps -> CanExecute = false
```

Five timestamp occurrences per lyric row is the agreed editor UI limit for this command. Existing source data with more occurrences must still be preserved losslessly even if the initial UI cannot add beyond five. Editing/replacing an existing timestamp remains a normal cell edit rather than a separate playback command. Do not split this workflow into separate **Set Current Time** and **Add Current Time** commands.

## Manual timestamp input

Timestamp cells keep the exact staged text while the user edits. On the normal DataGrid commit path (including Tab, Shift+Tab, Enter, Shift+Enter, and focus leaving the cell), changed valid input is normalized with the existing canonical formatter to `mm:ss.fff`. The editor accepts `0` as `00:00.000`, and `minutes:seconds[.fraction]` with one- or two-digit seconds and one to three fractional digits; fractional digits are right-padded to milliseconds. Minutes may exceed 59, while seconds must remain below 60.

Empty or whitespace-only input clears the occurrence. Unsupported or invalid text is retained for the existing advisory validation flow. Plain integers other than the special `0` shortcut are not interpreted as total seconds (`72` does not mean 72 seconds). This is an editor-input grammar only; the authoritative LRC parser stays strict, and valid edits are stored/saved in canonical timestamp format.

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

The canonical projection rule is based only on recognized metadata classification: a physical line recognized as metadata may be hidden from the lyric grid but must remain preserved; every other physical line maps to exactly one stable-ID editor row. This includes timed and untimed lyrics, blank lines (including consecutive, leading, and trailing blank lines), whitespace-only lines, unknown tags/content, and malformed timestamp-like content. Do not infer why a line is blank or collapse adjacent blanks. The line-ending-aware physical reader preserves only rows represented by the input text: a final line terminator ends the preceding line and does not invent an additional row, while each additional blank physical line remains its own row. Save -> Close -> Reopen preserves visible row count, order, and content, subject only to recognized metadata being hidden.

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
- `Delete` on a selected editable cell outside text-edit mode clears only that cell; while its text editor is active, Delete remains normal character deletion. Empty cells are no-ops, and this key does not invoke **Delete Row**.
- arrow keys: normal row/cell navigation where the grid control permits it

Timestamp occurrences use the editor's existing dense ordered collection and LRC serialization has no empty occurrence marker. Clearing a timestamp therefore removes that occurrence; later occurrences compact left in the ordered list. The selected row and logical column are retained where possible. Sparse timestamp-slot identity is not persisted by this feature.

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
SetCurrentPlaybackTimeCommand
UndoCommand
RedoCommand
SaveCommand
```

Commands expose appropriate `CanExecute` state. For example, row-relative commands require a selected row while Append may remain available with no row selected.

A row context menu should expose the same operations as other bindings, for example:

```text
Set Current Playback Time
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

## Future timestamp-pattern filling

Repeated sections such as choruses often reuse the same relative timing pattern. A later editor command may propagate a reference timestamp lane into another lane once the target lane has an anchor. For a selected range:

```text
target[row] = target[anchor] + (reference[row] - reference[anchor])
```

Example: a complete `Timestamp 1` chorus plus the first `Timestamp 2` occurrence provides enough information to fill the remaining blank `Timestamp 2` cells with the same relative spacing. The operation should be explicit and bounded by the user's selected range; it must not infer where a chorus begins/ends. Missing reference timestamps are skipped, existing populated target cells are preserved by default, millisecond offsets are exact, and the whole fill is one Undo/Redo command.

The domain operation should exist independently of presentation. A command/context-menu implementation may come first; an Excel-style fill handle that drags down the grid can later invoke the same command. No automatic chorus detection or AI timing inference is required.

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

Validation severity should be immediately visible in the owning data cell:

- malformed or invalid timestamps are Errors; out-of-order timestamps and unrecognised/unsupported bracketed text are Warnings
- cross-row timestamp chronology is checked independently for each occurrence index (`Timestamp[1]`, `Timestamp[2]`, etc.); within each row, populated timestamps are also checked in their displayed order. Empty timestamp slots are skipped, equality is allowed, and validation never sorts or rewrites timestamps
- `Timestamp[n]` diagnostics style only that timestamp cell; lyric-text diagnostics style only the Lyrics cell; Error styling wins if one cell has both severities
- diagnostics that have no explicit cell target remain summary-only and do not colour an arbitrary cell or the row-number gutter
- a neutral `DataGrid.RowHeader` displays one-based numbering of visible editor rows; hidden recognized metadata does not count, while blank visible rows do

The validation area should summarize rather than render an unbounded list of messages, for example:

```text
Warnings: 4    Errors: 1
```

Hovering the bottom `Warnings: N    Errors: N` summary shows every diagnostic as `Severity: Line N - message`, using the same visible-row numbering as the gutter. Entries are ordered by visible line, Error before Warning on the same line, then stable validation order. The tooltip is absent when there are no diagnostics. Counts remain visible at zero; the advisory notice appears only while at least one diagnostic exists and explains that saving is still allowed.

Validation remains non-blocking: the user may save the exact content they chose even when warnings or errors are present.

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
→ Set Current Playback Time
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

## Timing-adjustment UI ownership

The M8 timing engine remains independent of editor presentation, but the agreed target desktop UI places all user-facing timing authoring inside the Built-in Lyrics Editor rather than the ordinary Main Lyrics Window or Desktop Lyrics Overlay. This includes:

```text
GlobalOffsetMs -0.5 / -0.1 / Reset / +0.1 / +0.5
Bake into LRC
Current-line / exact-occurrence timing adjustment
playback-assisted timestamp assignment
```

The existing M8/M9 MainWindow and overlay quick-action controls are legacy surfaces until this relocation is implemented; moving the UI must reuse the existing safe timing coordinator/storage semantics rather than inventing a second timing model. Playback current line remains distinct from editor selection, so any current-playback timing action must make its target identity explicit and must not silently retarget the editor selection.

## Modal lifecycle

The built-in editor is a modal editing workspace, not another permanent application window.

While open:

- playback and `PlaybackClock` continue
- NativeHost/browser communication continues
- the desktop overlay continues updating normally
- the current MainWindow/future Main Lyrics Window cannot open another editor instance
- the editor remains pinned to its own track even if playback changes
- the editor dialog must remain visually above the normally Topmost Desktop Lyrics Overlay

Temporarily suppress the overlay's Topmost state for the lifetime of the modal editor. When the editor closes, restore Topmost unless application shutdown is already in progress. Do not make the editor a permanently Topmost window merely to outrank the overlay.

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
- Set Current Playback Time with track-identity safety
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
