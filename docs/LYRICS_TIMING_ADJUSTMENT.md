# Lyrics Timing Adjustment

Milestone 8 adds a non-destructive timing correction to each local timed lyrics asset. The correction is an integer number of milliseconds named `GlobalOffsetMs`; it is not an application-wide preference. The Current Line follow-up adds a separate explicit edit of one authoritative LRC timestamp; it does not introduce per-line offsets.

## Sign and runtime behavior

The sign convention is:

- positive offsets make lyrics happen later
- negative offsets make lyrics happen earlier

For example, a line at `10.000` with `GlobalOffsetMs = +500` becomes effective at `10.500`. The LRC is not changed during an ordinary adjustment. Instead, the existing lyrics timeline is evaluated with:

```text
TimelinePositionMs = PlaybackPositionMs - GlobalOffsetMs
```

`PlaybackClock` continues to expose the real media position. Only the input to `LyricsTimeline` is adjusted, so pause, resume, playback-rate changes, and forward/backward seeks retain their existing clock behavior. The desktop overlay receives the resulting current/next timeline state and contains no offset arithmetic of its own.

The Milestone 8 implementation exposes `-0.5s`, `-0.1s`, `Reset`, `+0.1s`, `+0.5s`, and `Bake into LRC` in the current MainWindow/Control Panel. The agreed target desktop UI relocates all of these authoring controls into the Built-in Lyrics Editor; the ordinary Main Lyrics Window and Desktop Lyrics Overlay should not own timing-adjustment UI after that restructuring. Button changes use checked integer arithmetic, apply immediately, and perform one sidecar save per successful click. The displayed value uses signed seconds with millisecond precision, such as `+0.500s` or `-1.200s`.

The controls are available only when the current track has an authoritative local timed lyrics document. They remain disabled for pending, unavailable, untimed, or runtime-only lyrics, and those states do not create timing records.

## Portable persistence

The portable `track.lyrics.json` sidecar is authoritative:

```json
"timing": {
  "globalOffsetMs": 500
}
```

This is an optional additive field in sidecar schema version 1. Existing version 1 sidecars without `timing` load as zero and are not rewritten merely because the effective value is zero. SQLite has no timing column; deleting and rebuilding the machine-local index does not lose the correction.

Offsets are loaded from the sidecar whenever a local track is resolved. Adjustment and bake operations are bound to `LocalTrackId`, preventing one track's offset from leaking into another. The app also captures the track ID and offset before showing the Bake confirmation and cancels if either is no longer current when the user confirms.

`Reset` sets and persists `GlobalOffsetMs = 0`, immediately re-evaluates the original timeline, and never rewrites the LRC.

## Bake into LRC

Bake is the explicit operation that permanently applies the global offset to every timestamp in `track.lrc`, and it requires confirmation. Current Line buttons separately edit one timestamp directly, as described below. A zero global offset is not baked. On success:

```text
new LRC timestamp = old LRC timestamp + GlobalOffsetMs
new GlobalOffsetMs = 0
```

Consequently, effective playback timing is unchanged across the bake. A focused timestamp rewriter handles the supported `[mm:ss.xx]` and `[mm:ss.xxx]` tokens and emits changed tokens as `[mm:ss.fff]`. It edits tokens in the original physical text instead of serializing parsed lyric lines, preserving metadata and unknown tags, blank lines, line endings, multiple timestamps on one line, lyric text, Unicode, and an explicit `♪` line. Milestone 8 assigns no rendering semantics to `♪`.

The complete rewrite is validated before either authoritative file changes. Bake is rejected if any adjusted timestamp would be negative or if parsing/addition/formatting would overflow. During a negative bake only, an exact zero timestamp on a blank/whitespace-only lyric row or on the canonical explicit break marker `♪` remains at zero; other timestamps, including ordinary lyrics at zero and any near-zero timestamp, are still rejected if they would become negative. The blank/break classification does not normalize or rewrite lyric text. Rejection leaves the LRC, sidecar offset, and runtime timeline unchanged.

For a valid bake, complete LRC and sidecar contents are written to unique same-directory temporary files and flushed before commit. The LRC is replaced first and the sidecar-with-zero second. If the second replacement fails, the app attempts to restore the original LRC and reports a storage failure; owned temporary files are cleaned up on a best-effort basis. This is a small best-effort filesystem transaction, not a cross-filesystem or power-loss-safe transactional store. A rollback failure is surfaced and logged rather than presented as success.

After a successful bake, the local LRC is parsed again and the current timeline is rebuilt from its new timestamps. The local-first ownership rule remains unchanged: later provider results cannot overwrite the user-owned adjusted or baked LRC.

## Current Line adjustment

The current Milestone 8 MainWindow's Current Line section shows the current M6 timeline index (displayed one-based), original LRC start timestamp, and lyric text. In the target desktop UI this capability moves into the Built-in Lyrics Editor together with the global-offset controls and Bake. Its `-0.5s`, `-0.1s`, `+0.1s`, and `+0.5s` buttons are explicit destructive edits: each changes only that selected timestamp token in the local LRC and persists immediately. They do not require Bake or an additional confirmation. There is no arbitrary line selection or text editor.

The two mechanisms remain independent:

```text
Global Timing: sidecar GlobalOffsetMs, non-destructive, entire document
Current Line: authoritative LRC timestamp, explicit direct edit, one occurrence

EffectiveLineStartMs = LrcLineStartMs + GlobalOffsetMs
```

For example, editing a `20.000` line by `+100` with a global offset of `+500` produces an LRC start of `20.100` and an effective start of `20.600`. The sidecar is not modified. A later global Bake produces `20.600` in the LRC and resets the global offset to zero, preserving the same effective timing exactly once.

### Target identity and boundaries

Local LRC parsing retains each usable timestamp token's character index and length alongside the stable normalized document ordering. The current timeline index maps to that exact source occurrence, rather than matching text or timestamps alone. Duplicate text, duplicate timestamps, unsorted physical lines, and multiple timestamps on one physical line therefore do not select the wrong token. For `[00:10.000][00:20.000]Same text`, editing the second timeline entry changes only `[00:20.000]`. These locations are local-library metadata only; nothing is added to the playback protocol, sidecar, or SQLite.

The app captures the local document, its `LocalTrackId`, relative LRC path, loaded-content hash, and current index at the click. A delayed operation is rejected if the current local asset/version has changed. Persistence remains bound to the captured asset; if a track change occurs during saving, another track's file and runtime state are never replaced by the result.

Checked integer addition must produce a timestamp at or above zero. When present, adjacent lines impose inclusive bounds:

```text
previous.StartMs <= adjusted.StartMs <= next.StartMs
```

Crossing a neighbour or creating a negative/overflowing timestamp rejects the edit without changing files. Equality is allowed and follows the existing stable duplicate-timestamp selection rule. The first line has no previous boundary; the last line has no next boundary or artificial duration maximum. A current `♪` line uses exactly these same rules, without new break-rendering behavior.

### Saving and re-evaluation

The loaded LRC byte content has a SHA-256 identity. Before editing, the library re-reads and validates the authoritative asset and compares its path, global offset, and LRC identity with the captured document. After preparing and flushing a unique same-directory temporary file, it checks the LRC hash again immediately before replacement. An unexpected external change aborts the edit and safely reloads the current local document; an unusable changed document clears the stale timeline. Milestone 10 adds a read-only active-file watcher; watcher reloads never write the LRC. Content identity deduplicates notifications from Current Line and Bake self-writes. See [EXTERNAL_EDITING.md](EXTERNAL_EDITING.md).

Only the selected raw timestamp token is replaced, using canonical millisecond precision. Other timestamps, metadata/unknown tags, blank lines, mixed line endings, Unicode text, and existing encoding/BOM are retained. Failed writes clean up owned temporary files where possible and leave the previous valid runtime timing intact. File writability is checked when loading and again when saving; read-only assets have disabled current-line controls.

After successful persistence, the app rebuilds the timeline from the edited parsed document and refreshed source occurrences/content identity, retaining the global offset and actual `PlaybackClock` position. Current/Next and the overlay are re-evaluated immediately. This may change which line is current; the next button click targets the newly current line, not a pinned prior selection. Later remote lyrics remain unable to overwrite the local edit.

The pre-replacement hash check is best-effort write safety, not a filesystem compare-and-swap lock against arbitrary concurrent external replacements. External edits refresh parsed source-occurrence mappings before later Current Line edits. No per-line metadata or library-wide external-edit synchronization system is added.

## Deliberate scope limits

Milestone 8 does not add timing state to SQLite, alter Firefox or NativeHost messages, or change `PlaybackClock`. Milestone 9 currently exposes quick current-line and global timing actions in the overlay context menu by routing them to these existing coordinator operations, while Bake remains on the current MainWindow. The agreed desktop-surface redesign removes those presentation shortcuts and consolidates all timing-adjustment UI in the Built-in Lyrics Editor without changing the underlying coordinator, persistence, target-identity, or safety rules. Break rendering, preparation cues, karaoke progress, and word/character timing remain later work.
