# Overlay Interaction and Basic Preferences

Milestone 9 makes the existing lyrics window practical as the primary daily surface. The overlay presents the current state from the playback/timeline services. The Control Panel owns preferences, diagnostics, timing controls, and library management. The system tray provides lifecycle and recovery actions while the Control Panel is hidden. None of these UI surfaces owns lyrics lookup, the playback clock, timeline selection, LRC parsing, SQLite, or timing-edit safety.

## Renderer model

Renderer configuration is divided into independent dimensions:

```text
Lyrics state → Content mode → Layout style → Appearance → Optional animation → WPF visuals
```

`LyricsContentMode` describes how much content to display:

- `OneLine`: current lyric, or the first upcoming lyric before playback reaches the first line.
- `TwoLines`: current lyric as primary, next lyric as secondary. Before the first lyric the primary is blank and the first upcoming line is secondary.
- `AllLyrics`: a contextual multi-line viewport around the exact M6 timeline current occurrence. Nearby past lines appear above, upcoming lines below, and only lines that fit the overlay height are rendered. The whole document is never placed in the visual tree and there is no scrollbar or scroll-position state.

Confirmed no-lyrics and untimed statuses remain `暫無可用歌詞` and `此歌曲暫無同步歌詞` in every content mode. All Lyrics shows these statuses in its normal text presentation rather than an empty scrolling list. Seek and line transitions use the already-evaluated `LyricsTimelinePosition`; All Lyrics does not evaluate timing independently.

The current layout is `CenterStacked`. All Lyrics places the current row near the center and dims/sizes context according to distance from it. The current occurrence comes from the same timeline index used by the rest of the application; stable timestamp ordering preserves identity when text or timestamps are duplicated. Before the first lyric, the viewport contains upcoming lines only; near the end, it shows available past lines without blank placeholders. Height changes and seeks immediately rebuild the subset. `Past`, `Current`, `Upcoming`, and `Status` are semantic presentation roles; they are not domain colors. Current has scale/opacity 1.0; contextual emphasis decreases with distance to minimum scale 0.72 and opacity 0.52. These are presentation defaults and may become appearance preferences later. A future `KaraokeAlternating` layout may combine with `TwoLines` to place current and upcoming slots on alternating sides. Karaoke is a layout/rendering concern, not a content mode, and is not implemented.

Future appearance preferences may include font family, size and weight; role-based current/upcoming/past colors; alignment; opacity; outline/shadow; line spacing; and background opacity. Future karaoke appearance may include sung/unsung colors and progressive fill. No appearance editor or karaoke animation is implemented. Explicit break rendering, preparation cues, horizontal panning, word timing, and character timing remain deferred.

## Movement, resizing, and screen recovery

`Lock Position` prevents movement only. It does not prevent resizing, hide the overlay, pause lyrics, or change topmost/click-through. When unlocked, users can drag the lyric surface. With click-through disabled, invisible `WM_NCHITTEST` zones at each edge and corner provide ordinary borderless width/height resizing; lyric-text hit regions take priority over edge resizing. Resizing remains available while position is locked because the setting locks position only. There is no visible resize grip, bar, border, or other resize-only control.

Overlay geometry (`Left`, `Top`, `Width`, and `Height`) is persistent state independent of `OneLine`, `TwoLines`, and `AllLyrics`; changing modes never auto-sizes the window. Minimum size is 420 × 120 WPF device-independent pixels. No arbitrary upper dimension is imposed. `AllLyrics` estimates wrapped text height at the current width and selects nearby lines that fit the current height; width or height changes recalculate the subset while retaining window dimensions. Resize completion saves position and both dimensions together. After a resize, the current rectangle is retained if it remains meaningfully visible; otherwise the work-area resolver selects a visible fallback. A valid custom position is not reset just because another monitor is available.

## Partial click-through and hit testing

Click-through is selected per screen point through a focused `WM_NCHITTEST` hook:

| Click-through | Hit location | Result |
| --- | --- | --- |
| off | lyric text | normal WPF mouse interaction; left-drag moves when unlocked |
| off | empty edge/corner resize zone | native `HTLEFT`/`HTRIGHT`/`HTTOP`/`HTBOTTOM`/corner hit test |
| off | other overlay area | normal WPF mouse interaction |
| on | visible lyric text | WPF receives the input; right-click menu and unlocked left-drag remain available |
| on | empty/transparent overlay area, including edges | returns `HTTRANSPARENT` for the window underneath |

The overlay does not set whole-window `WS_EX_TRANSPARENT`. Click-through off keeps the semi-transparent interaction backing visible and enables invisible edge/corner resizing. With click-through on, the backing becomes fully transparent and resizing is disabled; layered-window alpha hit testing and `WM_NCHITTEST` pass empty pixels through across application threads while visible lyric text remains interactive. Lock and click-through remain independent: click-through never changes the saved lock setting, and lock disables dragging over lyrics while leaving right-click available. Lock does not disable resizing when click-through is off.

Control Panel and the global shortcut remain independent recovery paths for click-through. The shortcut is **Ctrl+Alt+Shift+T**. Click-through data that is missing or malformed defaults off. Mouse activation is suppressed so normal overlay interaction does not take keyboard focus.

## Overlay context menu

The current menu hierarchy is:

```text
Open Control Panel
Lyrics Display > One Line / Two Lines / All Lyrics
Adjust Timing >
  Current Line > -0.5s / -0.1s / +0.1s / +0.5s
  Global > -0.5s / -0.1s / Reset / +0.1s / +0.5s
Overlay > Lock Position / Click Through / Always on Top
Hide Desktop Lyrics
```

Content and overlay toggles reflect current state. Current Line actions are disabled unless the playback coordinator exposes a writable current local timed line. Timing actions invoke the existing M8 coordinator and storage paths, including their boundary checks and safe LRC write behavior. Global timing also uses existing `GlobalOffsetMs` behavior. Bake remains in the Control Panel because it rewrites the LRC and requires confirmation.

The menu is available when click-through is on only if the pointer is over lyric text. **Open Control Panel** shows/restores and activates the Control Panel as an explicit user action. Automatic lyric updates remain non-activating.

## Topmost and hotkeys

`Topmost` maps directly to WPF `Window.Topmost`, applies immediately, and defaults true for existing settings. There is no Z-order polling loop.

The fixed global shortcuts are:

| Shortcut | Action |
| --- | --- |
| `Ctrl+Alt+Shift+L` | Toggle overlay visibility |
| `Ctrl+Alt+Shift+T` | Toggle click-through |

`GlobalHotkeyService` is created for the application/Control Panel lifetime. It uses Win32 `RegisterHotKey` and `WM_HOTKEY`, not a keyboard hook. Hiding the Control Panel does not unregister shortcuts. Successful registrations are removed during explicit exit. Registration conflicts are logged and shown in the Control Panel without stopping the app; the service does not busy-retry. Hotkey actions do not activate the Control Panel.

## Tray lifecycle

The single system tray icon offers:

- Open Control Panel
- Show / Hide Desktop Lyrics
- Exit Lyrics Displayer

The Control Panel preference **Close Control Panel to system tray instead of exiting** defaults false for compatibility. When enabled, the close button hides the Control Panel while the WPF application, Named Pipe server, playback tracking, overlay, and global shortcuts continue running. Tray Open restores/activates the Control Panel. Tray Exit takes an explicit shutdown path that bypasses close-to-tray interception, closes the overlay, unregisters hotkeys, disposes the tray icon, and exits the application. The tray icon is disposed only on real shutdown.

## Persistence and compatibility

Overlay preferences and position share `%LOCALAPPDATA%\LyricsDisplayer\settings.json` with other machine-local application settings:

```json
{
  "overlay": {
    "left": 500,
    "top": 800,
    "locked": false,
    "clickThrough": false,
    "topmost": true,
    "contentMode": "twoLines",
    "width": 900,
    "height": 220
  },
  "application": {
    "closeControlPanelToTray": false
  }
}
```

The earlier M9 `displayMode` property is read as a compatibility alias. New saves use `contentMode` values `oneLine`, `twoLines`, or `allLyrics`. Position, width, preferences, and application lifecycle saves merge their own fields while preserving unrelated root/nested properties. The existing atomic replacement mechanism remains in use. Starting the app does not rewrite settings.

Missing values default to unlocked, click-through off, topmost on, two lines, 900 × 220 geometry, and close-to-tray off. Existing settings without `overlay.height` use the 220 DIP default. Malformed dimensions fall back independently; dimensions below 420 × 120 DIP or non-finite values use their safe defaults. No arbitrary maximum is imposed.

## Future media controls

A future context-menu section may be `Playback > Play / Pause / Previous Track / Next Track`. It is not implemented. Commands should go through a provider-independent `MediaControlService` and investigate Windows system media-session controls such as Global System Media Transport Controls Session APIs before simulated media keys. Firefox/YouTube Music DOM commands are not the intended normal control path. The OS active media session may not match the source session currently supplying lyrics, so matching may need a later policy.

## Current limitations

All Lyrics is a contextual viewport estimated using wrapped text dimensions; WPF fallback fonts can differ slightly from the estimator. The fixed hotkeys are not user-configurable. The renderer still uses one centered stacked layout and static role/distance emphasis. Tray close-to-hide is optional and off by default. No M10 external editing/watcher, appearance editor, karaoke layout/animation, break rendering, media controls, provider search, or general plugin framework is included.
