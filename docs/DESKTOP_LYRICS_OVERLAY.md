# Desktop Lyrics Overlay

Milestone 7 introduced the Desktop Lyrics Overlay as the ambient WPF lyrics surface. It is separate from the Main Lyrics Window and does not retrieve lyrics, read the local library, calculate playback time, or select lyric lines.

## State flow and display semantics

The existing local `PlaybackClock` and Milestone 6 `LyricsTimeline` continue to resolve a `LyricsTimelinePosition`. Canonical lyrics/timeline state is distinct from presentation state: the surface-neutral `LyricsPresentationState` retains the full source-ordered document, semantic line roles, and the resolved current/next occurrence. A small presentation mapping adapts that state to the overlay:

```text
PlaybackClock -> LyricsTimeline -> LyricsPresentationState -> Overlay viewport/layout -> LyricsOverlayWindow
```

The Desktop Overlay applies its own One/Two/contextual-All viewport and appearance policy; the contextual All view is bounded and does not truncate the shared full-document representation. The Main Lyrics Window consumes the same semantic state while presenting the complete document as a normal reading surface. Topmost, click-through, geometry, hit testing, and other window behavior remain outside shared lyrics presentation.

- Current and next lines become primary and secondary text respectively in Two Lines mode.
- Before the first timestamp, primary is blank and the first line is secondary.
- After the final timestamp, the final line remains primary and secondary is blank.
- Pending/unknown state, including the interval after a track change and before a result is accepted, produces two blank fields. It is not misrepresented as a confirmed negative result.
- A confirmed unavailable result displays `暫無可用歌詞` as primary text with blank secondary text.
- An available but untimed result displays `此歌曲暫無同步歌詞` as primary text with blank secondary text.
- When an authoritative local LRC is expected but remains missing after M10 watcher debounce/recovery checks, the overlay displays `本機歌詞檔案遺失`; this is distinct from provider-level no-lyrics and untimed states. Restoring the file clears the status automatically.
- Timed lyrics replace either status with the normal current/next presentation. Status strings are local presentation state only and are never stored as lyric lines or sent through the protocol.
- Seeks jump directly to the newly resolved state. Pause requires no overlay state because the shared playback clock and timeline remain unchanged.

The approximately 33 ms diagnostics refresh evaluates the existing timeline and offers the result to the overlay controller. Equal presentation values are ignored, so WPF text changes occur only when current or next text changes. This path performs no SQLite query, filesystem read, LRC parsing, or independent clock update.

## Window and lifecycle

`LyricsOverlayWindow` is separate from `MainWindow`. It has no system chrome, does not appear on the taskbar, uses a transparent WPF surface with a subtle translucent backing and text shadow, and keeps the lyric text at normal opacity. One-line and two-line modes retain the primary/secondary sizes. All Lyrics is a contextual multi-line viewport around the current timeline line, not the complete document. Only nearby past/current/upcoming rows are rendered, with no scrollbar. WPF wrapping and normal Windows font fallback support Chinese, Japanese, Korean, punctuation, and musical symbols.

The visible overlay is permanently WPF `Topmost` during normal operation. This is a role invariant rather than a user option. There is no aggressive Z-order loop and no attempt to cover secure desktop, protected system UI, or exclusive fullscreen content. The modal Built-in Lyrics Editor temporarily suppresses overlay Topmost for the editor lifetime so the editor remains visually above it; normal Topmost resumes after actual editor close, but fatal/accepted application shutdown does not perform a late visual restore.

The window has `ShowActivated=false`, suppresses mouse activation, and is shown through a non-activating lifecycle path. Lyric updates do not activate or focus the window. With click-through off, invisible edge/corner hit regions resize the borderless window. With click-through on, the backing disappears and empty regions including edges pass through while lyric text remains interactive. See [OVERLAY_INTERACTION.md](OVERLAY_INTERACTION.md).

The Main Lyrics Window's **Show Desktop Lyrics** action, global shortcut, and tray menu show or hide one lazily created overlay instance. Repeated show operations reuse it. An ordinary overlay close request is intercepted as hide. If the persisted close-to-tray preference is enabled, closing the Main Lyrics Window hides it while the application continues; otherwise it keeps the existing exit behavior. Tray Exit performs real application shutdown and closes the overlay.

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
    "clickThrough": false
  }
}
```

The coherent settings reader accepts either section independently. Overlay coordinates must both be finite JSON numbers. Missing or malformed overlay data uses the default without discarding a valid library path. Position and preference saves merge into the `overlay` object, preserve unknown nested and top-level settings, write a unique same-directory temporary file, flush it, and replace the settings file. Topmost is not a persisted preference; legacy `overlay.topmost` values are ignored by the current invariant and may be removed by later settings saves.

Saved geometry is validated before show/recovery and the overlay remains a normal floating window; maximized/minimized bounds are never persisted as user geometry. Runtime movement is application-managed rather than native caption dragging, so Windows drag-to-maximize/Snap placement does not control the overlay.

During a manual drag, the cursor selects the target monitor and the complete overlay rectangle is clamped inside that monitor's usable work area, including taskbar exclusions. Crossing into another monitor transfers the constraint to that monitor. Mixed-DPI movement keeps screen/work-area calculations coherent across physical pixels and WPF device-independent geometry. If the overlay is larger than the destination work area, it is reduced only as much as necessary to fit. Manual edge/corner resize is constrained to the work area of the monitor where resizing began. Lock Position disables movement without disabling resize. If a monitor disappears, show/recovery and display-change handling return the overlay to a valid visible work area rather than leaving inaccessible geometry.

## Current limitations and future break design

The renderer consumes the existing timeline result for One Line, Two Lines, and All Lyrics; the All Lyrics contextual viewport follows the exact current occurrence, places nearby past/upcoming lines around it, and gives it the strongest presentation emphasis with distance falloff. Milestone 9 adds lock, partial click-through, persistent independent geometry/content preferences, timing quick actions, global shortcuts, and tray lifecycle around that presentation; see [OVERLAY_INTERACTION.md](OVERLAY_INTERACTION.md). Milestone 10 allows opening and reloading the active timed or untimed LRC externally, so the overlay reflects valid external edits immediately; the overlay itself does not own editing. Milestone 11 exposes **Open Built-in Editor** in the overlay context menu, but that menu only invokes the shared editor command and never owns editor state. The Main Lyrics Window shares ordinary lyrics commands, while overlay-only options remain limited to overlay presentation/interaction such as content mode, click-through, and lock. Skins, progress fill, karaoke layouts/animation, word/character timing, countdowns, preparation animation, and other advanced presentation remain outside the overlay implementation.

Breaks must eventually be represented explicitly. Timestamp gap length alone must never imply a break. The proposed canonical LRC marker is:

```lrc
[00:13.000]♪
```

In M7, `♪` is ordinary lyric text with no special behavior. A future renderer may, only during an explicit break, show a separate preparation indicator when the next lyric is **more than three seconds** away. When the next lyric is three seconds or less away, it should show the upcoming lyric directly with no separate prepare cue. Three seconds controls future prepare-cue presentation only; it does not determine whether a break exists. Break semantics, editing, and karaoke rendering remain unimplemented.

## Relationship to the Main Lyrics Window

The Desktop Lyrics Overlay is intentionally not the only lyrics-reading surface. The Main Lyrics Window is a conventional, non-Topmost focusable window for complete lyrics reading and always presents the full lyrics document; it does not expose the overlay's One/Two/All content-mode selector. The overlay remains the always-on-top ambient surface and keeps the existing `OneLine`, `TwoLines`, and bounded contextual `AllLyrics` modes.

Track metadata/playback controls are planned for the independent Media Controller, settings/diagnostics for the Settings window, and timing-authoring controls for the Built-in Lyrics Editor. These responsibilities should not accumulate in the overlay renderer.
