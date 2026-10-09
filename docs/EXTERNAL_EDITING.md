# External Lyrics Editing

Milestone 10 supports editing the currently active local LRC in a separate application, whether its content is timed or untimed. The portable LRC remains user-owned and authoritative; this is not a built-in editor or library-wide synchronisation service. Available untimed provider lyrics are stored as ordinary UTF-8 text lines in the same `.lrc` asset, with no synthetic timestamps.

## Open the active LRC

The Main Lyrics Window and overlay/shared lyrics actions may expose **Open LRC Externally** when the active track has a usable authoritative local LRC and its path resolves safely inside the configured library. Timing is not required: a valid untimed file is editable too. The app asks Windows to open that file with its default associated application; it does not select or launch a particular editor and never creates an empty file for this command. Shell and file failures are logged and surfaced through the ordinary application status/diagnostic path.

## Active-file watcher

Only the active record's LRC is watched. `ActiveLrcFileWatcher` watches the containing directory filtered to the target filename, plus its parent filtered to the track directory so it can recover if the track directory is replaced. Changed, created, deleted, renamed, and watcher-error notifications are coalesced with a 300 ms debounce. A stable read is established by obtaining two identical SHA-256 fingerprints, with up to six attempts 125 ms apart; retrying is bounded and never polls during normal playback. Missing status is reported only after the bounded absent checks. Atomic temp-file/rename saves and temporary file locks are therefore treated as transient until the stable-read result is known.

An observed fingerprint is compared with the last reported content identity. Duplicate notifications and app-owned editor/timing saves that produce already-loaded content do not cause duplicate state transitions or writes. Reload is read-only: it validates the on-disk sidecar association and LRC through the existing library/parser path, then rebuilds the local document and timeline at the existing `PlaybackClock` position. Effective metadata is retained. New parser/source-occurrence data replaces old data so later editor operations target the current document. The Main Lyrics Window and overlay receive the new presentation immediately.

## Invalid, missing, and recovered files

Fatal diagnostics for malformed timestamp-like syntax reject the entire external document; parsed valid lines are never accepted as a partial replacement. Clean untimed lyrics, blank content, and unknown metadata remain syntactically valid and distinct from malformed LRC syntax. A clean untimed document has no timeline and keeps the unsynchronised-lyrics renderer status, while remaining available as the authoritative local file. The parser's established mixed-document rule is preserved: one or more valid timestamped lyric entries make the document timed; untimed text lines are retained in the source file/document but do not enter the timeline. No timing is inferred for them. Saving valid timestamps to an untimed file therefore transitions it directly to normal timed presentation on reload, at the current playback position, without restart or provider re-fetch. The app retains the last-known-good runtime lyrics and timeline where available, marks local timing edits unavailable until a valid reload, records a low-volume diagnostic with line number and reason where available, and continues watching for a correction. It never repairs, restores, or overwrites the user's malformed file. A later valid save is loaded automatically, becomes the new last-known-good state, and clears the invalid status. If the active authoritative LRC remains missing after debounce and bounded recovery checks, the overlay displays a local-file-missing status instead of stale lyric context; no replacement file is created and provider lyrics are not restored over the local association. When the file reappears, it is reloaded automatically and normal lyrics presentation resumes. User-facing surfaces show an appropriate concise invalid/missing status, while detailed diagnostics belong under Settings > Debug and logs.

## Track changes and lifecycle

Each watcher is bound to the active `LocalTrackId`, path, and generation. Switching tracks disposes the old watcher and creates one for the new active local LRC; queued callbacks from an earlier generation are discarded on the dispatcher. The watcher belongs to app/current-track lifetime, not overlay or Main Lyrics Window visibility, so it continues while either UI is hidden to the tray. Explicit application shutdown disposes both filesystem watchers and timers and cancels pending stable-read work.

On activation and when the overlay is shown, the active file is fingerprint-checked as a low-cost fallback for missed filesystem notifications. Watcher errors are logged and monitoring is re-established with bounded exponential backoff; this does not introduce high-frequency polling.

## UNC/SMB and limitations

Paths are resolved through the configured library root and normal filesystem APIs, including absolute UNC paths. Filesystem notifications can be delayed, duplicated, or unavailable on SMB/NAS implementations; activation/overlay-show checks and watcher recovery reduce, but cannot eliminate, that platform limitation. The app does not promise perfect real-time network-share notifications and does not monitor inactive tracks.

## Built-in editor boundary

Milestone 11 reuses the same authoritative local LRC/sidecar and safe-save principles but owns a separate modal editor buffer. Opening the built-in editor is a shared application command that may be invoked from the Main Lyrics Window or overlay context menu, and later from other surfaces such as the Media Controller. The active-file watcher remains an application/current-playback concern; it must not silently overwrite a dirty built-in editor buffer. Built-in-editor conflict/dirty-state rules are documented in [BUILT_IN_EDITOR.md](BUILT_IN_EDITOR.md).
