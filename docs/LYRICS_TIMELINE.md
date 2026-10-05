# Lyrics Timeline

Milestone 6 connects accepted timed lyrics to the existing local playback clock. Timeline selection is Core domain logic; it has no dependency on WPF, Firefox, transport, SQLite, or the portable filesystem.

## Selection semantics

Selection uses line start timestamps only:

```text
position < first.startMs
  current = none
  next = first line

line[i].startMs <= position < line[i + 1].startMs
  current = line[i]
  next = line[i + 1]

position >= last.startMs
  current = last line
  next = none
```

`endMs` does not decide whether a line remains current. This preserves standard LRC behavior, including keeping the final line current after its start when no reliable end exists. Negative positions behave as before the first line; positions through `long.MaxValue` are handled without timestamp arithmetic.

Zero timed lines produce no current or next line. The same empty state applies to untimed and unavailable lyrics; timestamps are never invented. Empty or whitespace lyric text does not change selection.

## Ordering and equal timestamps

`LyricsTimeline` copies and stable-sorts its input once by `startMs` during construction. Returned indexes refer to this normalized order. Evaluation does not re-sort.

For equal start timestamps, the last entry in stable document order is current at that timestamp. Earlier equal-start entries remain in the normalized collection but are not individually selected at that instant. Before the shared timestamp, the first equal-start entry is still the next line. Translation or grouped-line semantics are deliberately not inferred.

## Lookup and playback behavior

Evaluation uses an upper-bound binary search for the greatest `startMs` not after the supplied integer-millisecond position. Normal lookup is `O(log n)`; construction is `O(n log n)` once per accepted lyrics document.

The diagnostic UI's existing approximately 33 ms timer asks the monotonic `PlaybackClock` for the current position and evaluates the timeline. The timer does not increment lyric time itself and Firefox's snapshot frequency is unchanged.

- Pausing freezes `PlaybackClock`, so repeated timeline evaluations naturally return the same line.
- Resuming lets the existing clock advance again; no lyrics-specific resume state exists.
- Forward and backward authoritative seeks rebase the clock and are reflected on the next evaluation without walking through intermediate lines.
- A track or source-session change clears the previous timeline before local or remote lyrics for the new authority are accepted.
- Loading an M5 local LRC constructs the timeline from its parsed lines. Later remote results cannot replace that local timeline source.
- A legitimate same-track transition from accepted remote lyrics to the newly imported local document rebuilds the timeline from the accepted local lines.

No current-line index or playback position is written to SQLite, sidecars, or LRC files. Milestone 10 watches only the active local LRC and rebuilds the timeline at the unchanged `PlaybackClock` position after valid external edits; see [EXTERNAL_EDITING.md](EXTERNAL_EDITING.md). Timing corrections are authored into LRC timestamps through the Built-in Lyrics Editor; the timeline evaluates the unmodified local playback position against those authored timestamps.
