# Lyrics Displayer

Milestone 3 uses this local transport path to show real YouTube Music playback state:

```text
Firefox extension -> Firefox Native Messaging -> LyricsDisplayer.NativeHost
                  -> Windows Named Pipe -> LyricsDisplayer.App
```

The Firefox extension observes YouTube Music's HTML media element, current video ID, and player-bar metadata. It sends the owning tab's latest authoritative state approximately every 500 ms, plus coalesced immediate snapshots for play, pause, completed seeks, playback-rate changes, and track metadata changes. The Windows application interpolates a local playback position between accepted snapshots and must be started manually.

## Prerequisites

- Windows 10 or 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Firefox
- PowerShell 5.1 or later

Run all commands below from the repository root.

## Build and test

Build the complete solution:

```powershell
dotnet build .\windows\LyricsDisplayer.slnx
```

Run all NUnit tests:

```powershell
dotnet test .\windows\LyricsDisplayer.slnx
```

The three test projects are under `windows\Tests` and do not require Firefox, Native Messaging registration, the real `%LOCALAPPDATA%`, or real five-second delays.

## Register the development NativeHost

After a Debug build, register the host for the current Windows user:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install-native-host.ps1
```

For a Release build, pass `-Configuration Release`. The script resolves the repository location, verifies the built executable, writes the generated host manifest beneath `%LOCALAPPDATA%\LyricsDisplayer\NativeHost`, and creates the Firefox registration at:

```text
HKCU\Software\Mozilla\NativeMessagingHosts\com.lyricsdisplayer.nativehost
```

The fixed development extension ID is `lyrics-displayer@example.com`.

## Load the Firefox extension temporarily

1. Open `about:debugging` in Firefox.
2. Select **This Firefox**.
3. Select **Load Temporary Add-on**.
4. Choose `firefox-extension\manifest.json` from this repository.

The extension opens the registered Native Messaging host and installs a content script on `https://music.youtube.com/`. If YouTube Music was already open when the temporary extension was loaded, reload that tab once if Firefox does not inject the content script immediately. Firefox's Browser Console shows extension lifecycle and extraction diagnostics.

## Start and verify the app

Start Lyrics Displayer manually:

```powershell
dotnet run --project .\windows\LyricsDisplayer.App\LyricsDisplayer.App.csproj
```

Then open `https://music.youtube.com/` in Firefox and play a song. The diagnostic window should change from **Waiting for NativeHost** to **Connected** and display the real video ID, title, artist, album when available, duration, play/pause state, playback rate, and compact raw protocol JSON. **Snapshot Position** shows the last authoritative browser value, while **Local Playback Position** refreshes approximately every 33 ms from a monotonic local clock. Lyrics intentionally show unavailable with no lines.

Pause, resume, seek in both directions, and change playback rate where practical. The local position should freeze while paused, jump promptly to completed seeks, advance at the selected rate, and continue to receive periodic browser corrections. If an active Named Pipe connection is lost, the local position freezes until a newly accepted snapshot rebases it after reconnection.

Track changes made through Next, Previous, selecting another song, playlist progression, or autoplay should update without reloading Firefox. The source session ID remains stable while the same tab owns playback and sequence numbers continue increasing.

## Verify tab ownership

YouTube Music tabs use first-in-first-served ownership:

1. Start playback in tab A and confirm its track appears in Lyrics Displayer.
2. Start playback in tab B. Tab B is logged and ignored; it must not replace tab A.
3. Pause tab A. It remains the owner, and tab B still cannot steal ownership.
4. Resume or change tracks in tab A and confirm its existing source session continues.
5. Close tab A or navigate it away from YouTube Music.
6. Start or resume playback in an eligible YouTube Music tab. It receives ownership with a new source session ID.

## Verify delayed startup and reconnect

To verify retry behavior:

1. Close Lyrics Displayer.
2. Reload the temporary extension in `about:debugging`, leave the app closed, and play a song in YouTube Music.
3. Inspect the NativeHost log and wait through several approximately five-second retries.
4. Start the app with the command above. The existing NativeHost connects on a later retry; Firefox does not need to be restarted.
5. Close the WPF app while the extension remains loaded. The host logs the lost pipe and resumes retrying.
6. Start the WPF app again. Snapshots resume on a later retry.
7. Remove or reload the temporary extension to end its Native Messaging session. The host observes EOF/disconnection, cancels reconnect work, and exits.

## Persistent logs

Each component has an active `current.logs` file:

```text
%LOCALAPPDATA%\LyricsDisplayer\Logs\FirefoxExtension\current.logs
%LOCALAPPDATA%\LyricsDisplayer\Logs\NativeHost\current.logs
%LOCALAPPDATA%\LyricsDisplayer\Logs\App\current.logs
```

Non-empty active logs rotate on a new component session and on the first write after local midnight. Rotated files use `YYYY-MM-DD.logs`, then numbered suffixes when needed, and recognised rotated logs older than 30 days are removed.

## Unregister the NativeHost

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\uninstall-native-host.ps1
```

The uninstall script removes only the current-user Firefox registration and the generated development manifest. It does not remove builds or logs.

## Milestone 3 limitations

Milestone 3 intentionally has no YouTube Music lyrics retrieval, lyrics provider or storage, sophisticated drift smoothing, desktop overlay, settings, auto-launch, installer, or updater. Snapshot correction is a straightforward authoritative rebase. Album extraction accepts only an explicit album link or Media Session album value; when neither is available, the protocol value is `null`. See `docs\ARCHITECTURE.md` for the longer-term direction and `docs\PROTOCOL.md` for protocol version 1.
