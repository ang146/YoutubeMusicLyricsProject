# Overlay Interaction and Basic Preferences

Milestone 9 makes the Desktop Lyrics Overlay practical as the always-on-top ambient lyrics surface. The overlay presents state from the playback/timeline services and does not own lyrics lookup, the playback clock, timeline selection, LRC parsing, SQLite, or timing-edit safety. M12 now separates the ordinary full-document Main Lyrics Window, the Settings/Debug surface, the Built-in Lyrics Editor, and the future Media Controller so the overlay does not accumulate unrelated application controls.

## Renderer model

Renderer configuration is divided into independent dimensions:

```text
Lyrics state → Content mode → Layout style → Appearance → Optional animation → WPF visuals
```

`LyricsContentMode` describes how much content to display:

- `OneLine`: current lyric, or the first upcoming lyric before playback reaches the first line.
- `TwoLines`: current lyric as primary, next lyric as secondary. Before the first lyric the primary is blank and the first upcoming line is secondary.
- `AllLyrics`: a contextual multi-line viewport around the exact M6 timeline current occurrence. Current is mandatory when the timeline has a current line. The strict semantic priority is `Current, Upcoming +1, Previous -1, Upcoming +2, Previous -2, ...`. Measurement decides only how many entries fit: the viewport may select only a prefix of that priority sequence, and a lower-priority line must never replace a higher-priority line merely because it is shorter. If the first context candidate does not fit, selection stops there; it does not scan ahead. Selected occurrences are sorted into normal document order only after fitting. Thus two rows prefer Current + Next, while three rows normally show Previous + Current + Next. If there is no current line, available upcoming rows fill the viewport without fabricated past rows. At the song end, nonexistent upcoming occurrences are omitted from the sequence so available past rows can fill capacity. The whole document is never placed in the visual tree and there is no scrollbar or scroll-position state.

Confirmed no-lyrics and untimed statuses remain `暫無可用歌詞` and `此歌曲暫無同步歌詞` in every content mode. All Lyrics shows these statuses in its normal text presentation rather than an empty scrolling list. Seek and line transitions use the already-evaluated `LyricsTimelinePosition`; All Lyrics does not evaluate timing independently.

The current layout is `CenterStacked`. All Lyrics places the current row near the center and combines semantic role (`Past`, `Current`, `Upcoming`) with distance from Current. Current is strongest and modestly larger than the contextual base size; Upcoming remains readable and has greater emphasis than Past at equal distance. Past remains legible but is visually lighter. Both contextual roles decrease in size and opacity with distance, with lower bounds to preserve readability. These are presentation defaults and future user appearance preferences may override their visual values. The current occurrence comes from the same timeline index used by the rest of the application; stable timestamp ordering preserves identity when text or timestamps are duplicated. Before the first lyric, the viewport contains upcoming lines only; near the end, it shows available past lines without blank placeholders. Height or width/wrapping changes and seeks immediately rebuild the complete subset; shrinking removes entries from the low-priority end, and growing appends candidates in priority order. A future `KaraokeAlternating` layout may combine with `TwoLines` to place current and upcoming slots on alternating sides. Karaoke is a layout/rendering concern, not a content mode, and is not implemented.

Future appearance preferences may include font family, size and weight; role-based current/upcoming/past colors; alignment; opacity; outline/shadow; line spacing; and background opacity. Future karaoke appearance may include sung/unsung colors and progressive fill. No appearance editor or karaoke animation is implemented. Explicit break rendering, preparation cues, horizontal panning, word timing, and character timing remain deferred.

## Movement, resizing, and screen recovery

`Lock Position` prevents movement only. It does not prevent resizing, hide the overlay, pause lyrics, or change topmost/click-through. When unlocked, a left-button drag on the lyric surface captures the mouse and application code uses the cursor position and the original grab offset to update the overlay; it does not call `Window.DragMove`, return `HTCAPTION`, or enter the native Windows caption move loop. This intentionally disables drag-to-top maximize, left/right Snap placement, Snap Assist, and Snap Layout movement for the overlay. On every move, the cursor selects the target monitor. The whole overlay rectangle is clamped inside that monitor's usable work area, not raw monitor bounds, so taskbars remain unobstructed. Crossing a monitor boundary switches to the new monitor immediately and may correct the position to fit the whole window there.

With click-through disabled, invisible `WM_NCHITTEST` zones at each edge and corner provide borderless width/height resizing through the existing native sizing hit tests; lyric-text hit regions take priority over edge resizing. During the native resize operation, the monitor under the cursor at resize start (or the window monitor as fallback) remains the active monitor, avoiding accidental monitor transfer during resize. Requested edges are constrained to that monitor's work area, including its taskbar boundary. Resizing remains available while position is locked because the setting locks position only. There is no visible resize grip, bar, border, or other resize-only control.

The overlay is always intended to remain in `WindowState.Normal`. Overlay-specific `SC_MAXIMIZE` and `SC_MINIMIZE` commands are ignored, and any unexpected non-normal state is normalized. Before a hidden singleton is shown after an abnormal state, it reapplies the saved normal width/height and resolves the saved position through the existing visibility-recovery logic. Geometry completion is persisted only while the window is normal and no abnormal-state recovery is pending, so maximized monitor bounds cannot replace the user's floating geometry. Hide/show and application-level visibility toggles remain supported; they are not minimized-window states.

Overlay geometry (`Left`, `Top`, `Width`, and `Height`) is persistent state independent of `OneLine`, `TwoLines`, and `AllLyrics`; changing modes never auto-sizes the window. The interactive minimum is 420 × 120 WPF device-independent pixels where the active work area permits it. If a destination work area is smaller, movement reduces only the dimensions necessary to fit; those smaller positive dimensions are persisted and restored instead of being rejected by the ordinary interactive minimum. No arbitrary upper dimension is imposed. Because WPF dimensions are device-independent units while monitor work areas and cursor/window positioning use physical screen pixels, the process is Per-Monitor-V2 DPI aware and explicitly applies the target monitor's scale when converting dimensions and grab offsets. `AllLyrics` estimates wrapped text height at the current width and fits only a prefix of the semantic priority sequence to the current height. Measurement decides visible count; semantic priority decides line identity. A tall or wrapped candidate ends the prefix—shorter later candidates cannot replace it. Width or height changes rebuild the subset from the current timeline state while retaining window dimensions. Resize completion saves position and both dimensions together. Startup/show recovery clamps saved geometry into the nearest/current work area, shrinking only where required, and retains fully fitting custom geometry unchanged.

## Partial click-through and hit testing

Click-through is selected per screen point through a focused `WM_NCHITTEST` hook:

| Click-through | Hit location | Result |
| --- | --- | --- |
| off | lyric text | normal WPF mouse interaction; application-managed left-drag moves when unlocked |
| off | empty edge/corner resize zone | native `HTLEFT`/`HTRIGHT`/`HTTOP`/`HTBOTTOM`/corner hit test |
| off | other overlay area | normal WPF mouse interaction |
| on | visible lyric text | WPF receives the input; right-click menu and unlocked application-managed left-drag remain available |
| on | empty/transparent overlay area, including edges | returns `HTTRANSPARENT` for the window underneath |

The overlay does not set whole-window `WS_EX_TRANSPARENT`. Click-through off keeps the semi-transparent interaction backing visible and enables invisible edge/corner resizing. With click-through on, the backing becomes fully transparent and resizing is disabled; layered-window alpha hit testing and `WM_NCHITTEST` pass empty pixels through across application threads while visible lyric text remains interactive. Lock and click-through remain independent: click-through never changes the saved lock setting, and lock disables dragging over lyrics while leaving right-click available. Lock does not disable resizing when click-through is off.

The Main Lyrics Window and the global shortcut remain independent recovery paths for click-through; Settings provides the dedicated configuration/diagnostic surface. The shortcut is **Ctrl+Alt+Shift+T**. Click-through data that is missing or malformed defaults off. Mouse activation is suppressed so normal overlay interaction does not take keyboard focus.

## Overlay context menu

The overlay and Main Lyrics Window build separate WPF context menus from `LyricsContextMenuBase`. They bind to separate concrete surface-action ViewModels that share their common implementation and application services. Shared actions are **Open LRC Externally**, **Open Built-in Editor**, four **Current Line** adjustments (`-0.5s`, `-0.1s`, `+0.1s`, `+0.5s`), and four **All Lyrics** adjustments with the same deltas. Current Line edits the exact current occurrence; All Lyrics edits every timestamp in the active authoritative LRC. Both persist immediately. There is no Reset or runtime offset action.

The target shared/menu direction is:

```text
Open Lyrics Window (Overlay only)
Open LRC Externally
Open Built-in Editor
Current Line > -0.5s / -0.1s / +0.1s / +0.5s
All Lyrics > -0.5s / -0.1s / +0.1s / +0.5s
Settings (Main Lyrics Window only)

Lyrics Display > One Line / Two Lines / All Lyrics (Overlay only)
Overlay > Lock Position / Click Through (Overlay only)
Hide Desktop Lyrics (Overlay only)
```

The menu instances remain surface-specific: **Open Lyrics Window**, display mode, lock, click-through, and Hide Desktop Lyrics belong only to the overlay; **Settings** belongs to the Main Lyrics Window. There is no **Always on Top** menu item: Topmost is an invariant of a visible Desktop Lyrics Overlay.

**Open LRC Externally** is enabled for any safely resolved, usable authoritative local LRC, whether timed or untimed. The shared action model owns this command's availability and status; Main Lyrics Window and overlay menus bind to that same command. Active-file watching and reload behavior are documented in [EXTERNAL_EDITING.md](EXTERNAL_EDITING.md).

Milestone 11 adds **Open Built-in Editor** as another shared application command entry point. Its existing editor-opening eligibility remains authoritative in one command, and both lyrics surfaces bind to that command. The overlay menu does not own editor logic or editor-session state. A future Media Controller may expose the same action.

The timing actions use `ICurrentLyricsTimingService`, which captures current playback identity/occurrence, applies the shared timestamp safety rules, conditionally writes through `LyricsLibrary.SaveEditorAssets`, and reloads current lyrics presentation. Command availability follows the timing, current-file, and editor services through focused change events; it does not depend on MainWindow copying boolean state into the action ViewModels. These are not Editor row-selection commands and do not change playback-driven editor selection.

The menu is available when click-through is on only if the pointer is over lyric text. The explicit command that opens the Main Lyrics Window may activate that ordinary window; automatic lyric updates remain non-activating.

## Topmost and hotkeys

A visible Desktop Lyrics Overlay is permanently Topmost during normal operation. This is a window-role invariant, not a user preference, and there is no Z-order polling loop. Legacy persisted `overlay.topmost` values are ignored and may be removed on a later settings save.

When the modal Built-in Lyrics Editor is open, the editor must remain visually above the Desktop Lyrics Overlay. Opening the editor therefore activates an explicit modal suppression state that temporarily lowers the overlay out of the Topmost band without making the editor permanently Topmost. Suppression remains active through cancelled close attempts and ends only after the editor actually closes. Fatal or accepted application shutdown does not perform a late Topmost restoration that could interfere with shutdown UI.

The fixed global shortcuts are:

| Shortcut | Action |
| --- | --- |
| `Ctrl+Alt+Shift+L` | Toggle overlay visibility |
| `Ctrl+Alt+Shift+T` | Toggle click-through |

`GlobalHotkeyService` is created for the application lifetime. It uses Win32 `RegisterHotKey` and `WM_HOTKEY`, not a keyboard hook. Hiding the Main Lyrics Window does not unregister shortcuts. Successful registrations are removed during explicit exit. Registration conflicts are logged and should surface through normal diagnostics/Settings Debug UI without stopping the app; the service does not busy-retry. Hotkey actions do not activate the ordinary Main Lyrics Window.

These two M9 shortcuts are currently fixed. A later custom-hotkey feature should map stable command IDs to user-selected bindings and keep Global/Application/Editor scopes separate; it must not duplicate action logic inside key handlers.

## Tray lifecycle

The single system tray icon offers:

- Open Main Window / Lyrics Window
- Show / Hide Desktop Lyrics
- Exit Lyrics Displayer

The close-to-tray preference controls Main Lyrics Window close behaviour. The persisted development-era key may still use the older Control Panel name, but the user-facing role is Main Lyrics Window. When enabled, the close button hides that ordinary window while the WPF application, Named Pipe server, playback tracking, overlay, and global shortcuts continue running. Tray Open restores/activates the ordinary main window. Tray Exit takes an explicit shutdown path that bypasses close-to-tray interception, closes the overlay, unregisters hotkeys, disposes the tray icon, and exits the application. The tray icon is disposed only on real shutdown.

## Persistence and compatibility

Overlay preferences and position share `%LOCALAPPDATA%\LyricsDisplayer\settings.json` with other machine-local application settings:

```json
{
  "overlay": {
    "left": 500,
    "top": 800,
    "locked": false,
    "clickThrough": false,
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

Missing values default to unlocked, click-through off, two lines, 900 × 220 geometry, and close-to-tray off. Overlay Topmost is no longer persisted because it is always enabled outside explicit editor-modal suppression. Existing settings without `overlay.height` use the 220 DIP default. Malformed dimensions fall back independently; dimensions below 420 × 120 DIP or non-finite values use their safe defaults. No arbitrary maximum is imposed.

## Future Media Controller

The future Media Controller is an independent window/surface that may be shown or hidden separately from the Desktop Lyrics Overlay and optionally dock beside it on the left or right. Potential contents are current title/artist, elapsed time, duration, Previous/Play-Pause/Next transport buttons, a seekable progress bar when supported, and explicit application actions such as **Open Built-in Editor**. Previous and Next mean transport buttons only, not previous/next track metadata. Opening the editor must invoke the same shared `OpenBuiltInEditorCommand` used by the Main Lyrics Window and overlay context menu; the Media Controller does not own editor state. None of the media-control functionality is implemented now.

Display state should reuse the existing playback model (title, artist, duration, playback position, playing/paused). Commands belong to a future provider-independent `MediaControlService`. Do not equate Windows `GetCurrentSession()` with the YouTube Music source Lyrics Displayer tracks. Future matching should enumerate `GlobalSystemMediaTransportControlsSessionManager.GetSessions()` and use multiple signals (source application identity, title, artist, duration, position, and playback state); no single weak signal is authoritative. Enable controls only for exactly one sufficiently strong match. Disable them for no strong match or multiple ambiguous matches.

Do not try to make YouTube Music Windows' current/priority media session; find and control the right session directly. Firefox's behavior with multiple media-producing tabs must be tested empirically rather than assumed to be one session per tab or one session per browser. If the tracked source is not uniquely controllable, leave generic controls disabled. Seek is desired but capability-aware: send a seek request through `MediaControlService` to the matched session only if accepted. Continue using Lyrics Displayer's `PlaybackClock` for smooth elapsed-time display. If seek is unsupported or rejected, progress can remain displayed while seeking is disabled or safely reverted. No Windows media API, media command, or Firefox extension command is part of M9.

## Current limitations

All Lyrics is a contextual viewport estimated using wrapped text dimensions; WPF fallback fonts can differ slightly from the estimator. The fixed hotkeys are not user-configurable. The renderer still uses one centered stacked layout and static role/distance emphasis. Tray close-to-hide is optional and off by default. Milestone 11 now provides the built-in lyrics editor, but appearance editing, karaoke layout/animation, semantic-break rendering/preparation cues, media controls, provider search, and a general plugin framework remain later work.

## Target desktop-surface relationship

The M12 UI restructuring gives the ordinary Main Lyrics Window and the Desktop Lyrics Overlay different jobs. The Main Lyrics Window is a normal focusable window that always presents the full lyrics document and does not expose a One/Two/All selector. The overlay remains the compact ambient surface with `OneLine`, `TwoLines`, and contextual `AllLyrics` modes and is permanently Topmost whenever visible outside editor suppression.

Both lyrics surfaces should converge on the same application-command-backed right-click menu for shared actions. Surface-specific items such as overlay content mode, click-through, and lock remain overlay-only. Settings/diagnostics now live in a dedicated Settings window with independent pages. Rich playback transport remains future Media Controller work, while timing authoring now lives in the Built-in Lyrics Editor.
