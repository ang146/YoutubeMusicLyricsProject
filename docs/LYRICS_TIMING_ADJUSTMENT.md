# Lyrics Timing Adjustment

Milestone 8 adds a non-destructive timing correction to each local timed lyrics asset. The correction is an integer number of milliseconds named `GlobalOffsetMs`; it is not an application-wide preference.

## Sign and runtime behavior

The sign convention is:

- positive offsets make lyrics happen later
- negative offsets make lyrics happen earlier

For example, a line at `10.000` with `GlobalOffsetMs = +500` becomes effective at `10.500`. The LRC is not changed during an ordinary adjustment. Instead, the existing lyrics timeline is evaluated with:

```text
TimelinePositionMs = PlaybackPositionMs - GlobalOffsetMs
```

`PlaybackClock` continues to expose the real media position. Only the input to `LyricsTimeline` is adjusted, so pause, resume, playback-rate changes, and forward/backward seeks retain their existing clock behavior. The desktop overlay receives the resulting current/next timeline state and contains no offset arithmetic of its own.

The Control Panel exposes `-0.5s`, `-0.1s`, `Reset`, `+0.1s`, `+0.5s`, and `Bake into LRC`. Button changes use checked integer arithmetic, apply immediately, and perform one sidecar save per successful click. The displayed value uses signed seconds with millisecond precision, such as `+0.500s` or `-1.200s`.

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

Bake is the only Milestone 8 operation that changes `track.lrc`, and it requires explicit confirmation. A zero offset is not baked. On success:

```text
new LRC timestamp = old LRC timestamp + GlobalOffsetMs
new GlobalOffsetMs = 0
```

Consequently, effective playback timing is unchanged across the bake. A focused timestamp rewriter handles the supported `[mm:ss.xx]` and `[mm:ss.xxx]` tokens and emits changed tokens as `[mm:ss.fff]`. It edits tokens in the original physical text instead of serializing parsed lyric lines, preserving metadata and unknown tags, blank lines, line endings, multiple timestamps on one line, lyric text, Unicode, and an explicit `♪` line. Milestone 8 assigns no rendering semantics to `♪`.

The complete rewrite is validated before either authoritative file changes. Bake is rejected if any adjusted timestamp would be negative or if parsing/addition/formatting would overflow. Rejection leaves the LRC, sidecar offset, and runtime timeline unchanged.

For a valid bake, complete LRC and sidecar contents are written to unique same-directory temporary files and flushed before commit. The LRC is replaced first and the sidecar-with-zero second. If the second replacement fails, the app attempts to restore the original LRC and reports a storage failure; owned temporary files are cleaned up on a best-effort basis. This is a small best-effort filesystem transaction, not a cross-filesystem or power-loss-safe transactional store. A rollback failure is surfaced and logged rather than presented as success.

After a successful bake, the local LRC is parsed again and the current timeline is rebuilt from its new timestamps. The local-first ownership rule remains unchanged: later provider results cannot overwrite the user-owned adjusted or baked LRC.

## Deliberate scope limits

Milestone 8 does not add timing state to SQLite, alter Firefox or NativeHost messages, change `PlaybackClock`, or add controls to the desktop overlay. Click-through, shortcuts, overlay preferences, break rendering, preparation cues, karaoke progress, and word/character timing remain later work.
