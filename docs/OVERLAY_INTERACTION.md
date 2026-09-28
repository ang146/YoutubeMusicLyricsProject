# Overlay Interaction and Basic Preferences

Milestone 9 adds desktop interaction policy around the Milestone 7 lyrics overlay. The overlay remains a presentation endpoint over `PlaybackClock` and `LyricsTimeline`; it does not own playback, lyrics lookup, local-library, SQLite, or timing-adjustment logic.

## Interaction state

`OverlayInteractionState` is the runtime model. It contains independent `Locked`, `ClickThrough`, `Topmost`, `DisplayMode`, and `Width` values. The controller owns this state, applies it to the existing window immediately, and persists a preference change once.

Dragging follows this table:

| Locked | Click-through | Dragging |
| --- | --- | --- |
| off | off | enabled |
| on | off | disabled; the context menu remains available |
| off | on | disabled because mouse input passes through |
| on | on | disabled |

Click-through never changes the saved lock value. Turning click-through off therefore restores the interaction behavior implied by the existing lock value.

## Click-through and recovery

Click-through adds `WS_EX_TRANSPARENT` to the overlay window in place. The small `NativeOverlayClickThrough` adapter owns the `GetWindowLongPtr`/`SetWindowLongPtr` interop and preserves unrelated extended styles. The non-activating style remains in place when click-through is disabled.

Because a click-through window cannot receive its context menu, two independent recovery paths are always available:

- clear **Click through** in the Control Panel;
- press **Ctrl+Alt+Shift+T**.

Malformed or missing click-through data defaults to `false`, leaving the overlay mouse-interactive.

## Presentation and size

Two-line mode preserves the Milestone 7 mapping: current lyric in the primary line and next lyric in the secondary line. Before the first timestamp, the primary line is blank and the first upcoming lyric is secondary.

One-line mode shows current text when it exists. Before the first timestamp it shows the first upcoming lyric. It never alternates between current and next during an active line. No-lyrics and untimed status text use the same primary-line messages in both modes.

Width is adjustable from 420 to 1800 device-independent pixels and applies immediately. The window uses a compact height in one-line mode and the existing height in two-line mode; WPF wrapping handles long text. A size or mode change retains the current position when it remains meaningfully visible and otherwise uses the existing visible-work-area fallback. It does not recreate playback or timeline state.

`Topmost` maps directly to the WPF window property and is updated in place. The compatibility default is `true`; there is no Z-order polling loop.

## User surfaces

The main window is titled **Lyrics Displayer Control Panel** and remains the diagnostics, timing, and preference surface. Its Overlay Preferences section controls lock, click-through, topmost, display mode, and width. The existing **Show Desktop Lyrics** control remains the recovery path after hiding the overlay.

When mouse interaction is enabled, the overlay context menu provides:

- Open Control Panel
- Lock position
- Click through
- Always on top
- One line / Two lines
- Hide

Checked items mirror current state. **Open Control Panel** restores a minimized window and activates it because it follows an explicit user action. Rendering and automatic lyric updates remain non-activating.

## Global shortcuts

The fixed initial shortcuts are:

| Shortcut | Action |
| --- | --- |
| `Ctrl+Alt+Shift+L` | Toggle overlay visibility |
| `Ctrl+Alt+Shift+T` | Toggle click-through |

`GlobalHotkeyService` belongs to the Control Panel/application lifetime rather than the overlay-window lifetime. It registers each shortcut once with Win32 `RegisterHotKey`, receives `WM_HOTKEY` through the Control Panel handle, and unregisters successful registrations during shutdown. It does not use a keyboard hook.

If another application owns a shortcut, registration failure is logged and shown in the Control Panel without stopping the application. There is no busy retry. These toggle actions do not activate the Control Panel or deliberately move keyboard focus.

## Persistence and compatibility

Preferences share `%LOCALAPPDATA%\LyricsDisplayer\settings.json` with existing application settings:

```json
{
  "overlay": {
    "left": 500,
    "top": 800,
    "locked": false,
    "clickThrough": false,
    "topmost": true,
    "displayMode": "twoLines",
    "width": 900
  }
}
```

Position and preference writes merge properties into the existing `overlay` object. They preserve each other, unrelated top-level settings, and unknown nested overlay properties. Writes continue to use the existing same-directory atomic replacement mechanism. Settings are not rewritten merely because the application starts.

Missing and malformed values are validated independently. Safe defaults are unlocked, click-through off, topmost on, two lines, and 900-pixel width. A bad preference does not discard a valid position or another valid preference.

## Current limitations

Milestone 9 does not add configurable shortcut editing, a system tray, font/theme editing, karaoke rendering, break inference, preparation cues, horizontal panning, external LRC editing, or filesystem watching. Control Panel close behavior remains the existing application shutdown behavior. Timing adjustment stays exclusively in the Control Panel and is unaffected by overlay preferences.
