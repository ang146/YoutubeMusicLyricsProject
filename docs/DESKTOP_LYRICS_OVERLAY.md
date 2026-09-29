# Desktop Lyrics Overlay

Milestone 7 adds a user-facing WPF desktop lyrics window alongside the existing diagnostics window. It is a presentation endpoint: the overlay does not retrieve lyrics, read the local library, calculate playback time, or select lyric lines.

## State flow and display semantics

The existing local `PlaybackClock` and Milestone 6 `LyricsTimeline` continue to resolve a `LyricsTimelinePosition`. A small presentation mapping converts only that resolved state:

```text
PlaybackClock -> LyricsTimeline -> LyricsOverlayPresentationState -> LyricsOverlayWindow
```

- Current and next lines become primary and secondary text respectively in Two Lines mode.
- Before the first timestamp, primary is blank and the first line is secondary.
- After the final timestamp, the final line remains primary and secondary is blank.
- Pending/unknown state, including the interval after a track change and before a result is accepted, produces two blank fields. It is not misrepresented as a confirmed negative result.
- A confirmed unavailable result displays `暫無可用歌詞` as primary text with blank secondary text.
- An available but untimed result displays `此歌曲暫無同步歌詞` as primary text with blank secondary text.
- Timed lyrics replace either status with the normal current/next presentation. Status strings are local presentation state only and are never stored as lyric lines or sent through the protocol.
- Seeks jump directly to the newly resolved state. Pause requires no overlay state because the shared playback clock and timeline remain unchanged.

The approximately 33 ms diagnostics refresh evaluates the existing timeline and offers the result to the overlay controller. Equal presentation values are ignored, so WPF text changes occur only when current or next text changes. This path performs no SQLite query, filesystem read, LRC parsing, or independent clock update.

## Window and lifecycle

`LyricsOverlayWindow` is separate from `MainWindow`. It has no system chrome, does not appear on the taskbar, uses a transparent WPF surface with a subtle translucent backing and text shadow, and keeps the lyric text at normal opacity. One-line and two-line modes retain the primary/secondary sizes. All Lyrics is a contextual multi-line viewport around the current timeline line, not the complete document. Only nearby past/current/upcoming rows are rendered, with no scrollbar. WPF wrapping and normal Windows font fallback support Chinese, Japanese, Korean, punctuation, and musical symbols.

Normal WPF `Topmost` behavior keeps the overlay above ordinary application windows. There is no aggressive Z-order loop and no attempt to cover secure desktop, protected system UI, or exclusive fullscreen content.

The window has `ShowActivated=false`, suppresses mouse activation, and is shown through a non-activating lifecycle path. Lyric updates do not activate or focus the window. With click-through off, invisible edge/corner hit regions resize the borderless window. With click-through on, the backing disappears and empty regions including edges pass through while lyric text remains interactive. See [OVERLAY_INTERACTION.md](OVERLAY_INTERACTION.md).

The **Show Desktop Lyrics** checkbox in the Control Panel, global shortcut, and tray menu show or hide one lazily created overlay instance. Repeated show operations reuse it. An ordinary overlay close request is intercepted as hide. If **Close Control Panel to system tray** is enabled, closing MainWindow hides it while the application continues; otherwise it keeps the existing exit behavior. Tray Exit performs real application shutdown and closes the overlay.

## Dragging and position persistence

The lyric surface can be moved with a left-button drag when Lock Position is off. Native `WM_NCHITTEST` edge/corner results provide invisible borderless resizing while click-through is off; lyric regions take priority over resize zones. Lock Position prevents movement but not resizing. Overlay `Left`, `Top`, `Width`, and `Height` are persistent user-controlled geometry independent of content mode, so switching One Line, Two Lines, and All Lyrics never changes the window dimensions. Minimum size is 420 × 120 WPF device-independent pixels. All Lyrics fits estimated wrapped text heights to the available window height and recalculates on width/height changes.

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
    "top": 800,
    "width": 900,
    "height": 220,
    "contentMode": "twoLines",
    "locked": false,
    "clickThrough": false,
    "topmost": true
  }
}
```

The coherent settings reader accepts either section independently. Overlay coordinates must both be finite JSON numbers. Missing or malformed overlay data uses the default without discarding a valid library path. Position and preference saves merge into the `overlay` object, preserve unknown nested and top-level settings, write a unique same-directory temporary file, flush it, and replace the settings file.

Positions use WPF device-independent coordinates. A saved overlay rectangle must retain a usable intersection with a supplied visible work area. Otherwise the overlay falls back horizontally centered in the lower portion of the primary work area. The geometry decision is independent of physical monitors and is covered using synthetic work-area data.

At runtime, WPF's primary work area is used for fallback and its device-independent virtual-screen bounds permit basic restoration on secondary monitors. Resize completion runs the same geometry resolver against the current rectangle; valid custom positions are retained. This avoids mixing physical pixels with WPF window coordinates. Advanced handling of irregular gaps inside a virtual-screen bounding rectangle and per-monitor profiles is not implemented.

## Current limitations and future break design

The renderer consumes the existing timeline result for One Line, Two Lines, and All Lyrics; the All Lyrics contextual viewport follows the exact current occurrence, places nearby past/upcoming lines around it, and gives it the strongest presentation emphasis with distance falloff. Milestone 9 adds lock, partial click-through, persistent independent geometry/content preferences, timing quick actions, global shortcuts, and tray lifecycle around that presentation; see [OVERLAY_INTERACTION.md](OVERLAY_INTERACTION.md). Milestone 10 allows opening and reloading the active LRC externally, so the overlay reflects valid external edits immediately; the overlay itself does not own editing. Built-in lyric editing, skins, progress fill, karaoke layouts/animation, word/character timing, countdowns, and preparation animation remain outside the overlay implementation.

Breaks must eventually be represented explicitly. Timestamp gap length alone must never imply a break. The proposed canonical LRC marker is:

```lrc
[00:13.000]♪
```

In M7, `♪` is ordinary lyric text with no special behavior. A future renderer may, only during an explicit break, show a separate preparation indicator when the next lyric is **more than three seconds** away. When the next lyric is three seconds or less away, it should show the upcoming lyric directly with no separate prepare cue. Three seconds controls future prepare-cue presentation only; it does not determine whether a break exists. Break semantics, editing, and karaoke rendering remain unimplemented.
