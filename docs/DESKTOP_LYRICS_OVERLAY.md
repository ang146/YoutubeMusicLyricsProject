# Desktop Lyrics Overlay

Milestone 7 adds a user-facing WPF desktop lyrics window alongside the existing diagnostics window. It is a presentation endpoint: the overlay does not retrieve lyrics, read the local library, calculate playback time, or select lyric lines.

## State flow and display semantics

The existing local `PlaybackClock` and Milestone 6 `LyricsTimeline` continue to resolve a `LyricsTimelinePosition`. A small presentation mapping converts only that resolved state:

```text
PlaybackClock -> LyricsTimeline -> LyricsOverlayPresentationState -> LyricsOverlayWindow
```

- Current and next lines become primary and secondary text respectively.
- Before the first timestamp, primary is blank and the first line is secondary.
- After the final timestamp, the final line remains primary and secondary is blank.
- Pending/unknown state, including the interval after a track change and before a result is accepted, produces two blank fields. It is not misrepresented as a confirmed negative result.
- A confirmed unavailable result displays `暫無可用歌詞` as primary text with blank secondary text.
- An available but untimed result displays `此歌曲暫無同步歌詞` as primary text with blank secondary text.
- Timed lyrics replace either status with the normal current/next presentation. Status strings are local presentation state only and are never stored as lyric lines or sent through the protocol.
- Seeks jump directly to the newly resolved state. Pause requires no overlay state because the shared playback clock and timeline remain unchanged.

The approximately 33 ms diagnostics refresh evaluates the existing timeline and offers the result to the overlay controller. Equal presentation values are ignored, so WPF text changes occur only when current or next text changes. This path performs no SQLite query, filesystem read, LRC parsing, or independent clock update.

## Window and lifecycle

`LyricsOverlayWindow` is separate from `MainWindow`. It has no system chrome, does not appear on the taskbar, uses a transparent WPF surface with a subtle translucent backing and text shadow, and keeps the lyric text at normal opacity. Current text is larger and heavier than next text. Both use WPF wrapping and normal Windows font fallback, including Chinese, Japanese, Korean, punctuation, and musical symbols.

Normal WPF `Topmost` behavior keeps the overlay above ordinary application windows. There is no aggressive Z-order loop and no attempt to cover secure desktop, protected system UI, or exclusive fullscreen content.

The window has `ShowActivated=false` and is shown through a non-activating lifecycle path. Lyric updates only assign changed `TextBlock.Text` values and never call `Activate()` or `Focus()`. Clicking and dragging the interactive M7 surface may naturally interact with the window; click-through is deferred.

The **Show Desktop Lyrics** checkbox in the diagnostics window shows or hides one lazily created overlay instance. Repeated show operations reuse it. An ordinary overlay close request is intercepted as hide and does not stop the diagnostics application. Genuine main-application shutdown closes the overlay so it cannot keep the process alive.

## Dragging and position persistence

The broad overlay surface can be moved with a left-button drag. WPF `DragMove` owns movement and raises one application event when the drag finishes. Only that completion event writes the final position; mouse movement does not write settings.

The shared machine-local file remains:

```text
%LOCALAPPDATA%\LyricsDisplayer\settings.json
```

Example:

```json
{
  "lyricsLibraryPath": "\\\\NAS\\Media\\Lyrics",
  "overlay": {
    "left": 500,
    "top": 800
  }
}
```

The coherent settings reader accepts either section independently. Overlay coordinates must both be finite JSON numbers. Missing or malformed overlay data uses the default without discarding a valid library path. Saving replaces only the `overlay` object, preserves `lyricsLibraryPath` and unknown top-level settings, writes a unique same-directory temporary file, flushes it, and replaces the settings file.

Positions use WPF device-independent coordinates. A saved overlay rectangle must retain a usable intersection with a supplied visible work area. Otherwise the overlay falls back horizontally centered in the lower portion of the primary work area. The geometry decision is independent of physical monitors and is covered using synthetic work-area data.

At runtime, WPF's primary work area is used for fallback and its device-independent virtual-screen bounds permit basic restoration on secondary monitors. This intentionally avoids mixing physical pixel coordinates with WPF window coordinates. A saved position from a removed monitor falls outside the new virtual screen and recovers. Advanced handling of irregular gaps inside a virtual-screen bounding rectangle and per-monitor profiles is not part of M7.

## Current limitations and future break design

M7 renders whole lines only. It does not provide click-through, an overlay lock, resizing preferences, skins, global hotkeys, lyric editing, timing offsets, progress fill, karaoke animation, word/character timing, countdowns, or preparation animation.

Breaks must eventually be represented explicitly. Timestamp gap length alone must never imply a break. The proposed canonical LRC marker is:

```lrc
[00:13.000]♪
```

In M7, `♪` is ordinary lyric text with no special behavior. A future renderer may, only during an explicit break, show a separate preparation indicator when the next lyric is **more than three seconds** away. When the next lyric is three seconds or less away, it should show the upcoming lyric directly with no separate prepare cue. Three seconds controls future prepare-cue presentation only; it does not determine whether a break exists. Break semantics, editing, and karaoke rendering remain unimplemented.
