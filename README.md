# Lyrics Displayer

Milestone 1 proves this local transport path using synthetic playback data:

```text
Firefox extension -> Firefox Native Messaging -> LyricsDisplayer.NativeHost
                  -> Windows Named Pipe -> LyricsDisplayer.App
```

The Firefox extension does not inspect YouTube Music in this milestone. It generates a changing track, playback position, and timed lyric lines every 500 ms. The Windows application is a deliberately simple diagnostic receiver and must be started manually.

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

The extension immediately opens the registered Native Messaging host and starts its synthetic source session. Firefox's Browser Console also shows extension lifecycle diagnostics.

## Start and verify the app

Start Lyrics Displayer manually:

```powershell
dotnet run --project .\windows\LyricsDisplayer.App\LyricsDisplayer.App.csproj
```

The diagnostic window should change from **Waiting for NativeHost** to **Connected**. It displays the source session and sequence, track metadata, millisecond playback values, timed lyric lines, and the compact raw protocol JSON. Playback position changes about twice per second and the synthetic track changes every 30 seconds.

## Verify delayed startup and reconnect

To verify retry behavior:

1. Close Lyrics Displayer.
2. Reload the temporary extension in `about:debugging`, leaving the app closed.
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

## Milestone 1 limitations

This milestone intentionally has no real YouTube Music detection, lyrics provider or storage, playback interpolation, desktop overlay, settings, auto-launch, installer, or updater. See `docs\ARCHITECTURE.md` for the longer-term direction and `docs\PROTOCOL.md` for protocol version 1.
