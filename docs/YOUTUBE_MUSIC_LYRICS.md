# YouTube Music lyrics adapter

The runtime is entirely JavaScript inside Firefox. `ytmusicapi` is a reference, not a dependency. Upstream `main` and its current documentation were inspected on 2026-09-09/10 before implementing requests:

- [get_watch_playlist](https://github.com/sigma67/ytmusicapi/blob/main/ytmusicapi/mixins/watch.py)
- [get_tab_browse_ids](https://github.com/sigma67/ytmusicapi/blob/main/ytmusicapi/parsers/watch.py)
- [get_lyrics](https://github.com/sigma67/ytmusicapi/blob/main/ytmusicapi/mixins/browsing.py)
- [as_mobile](https://github.com/sigma67/ytmusicapi/blob/main/ytmusicapi/ytmusic.py)
- [TIMESTAMPED_LYRICS navigation](https://github.com/sigma67/ytmusicapi/blob/main/ytmusicapi/navigation.py)
- [LyricLine.from_raw](https://github.com/sigma67/ytmusicapi/blob/main/ytmusicapi/models/lyrics.py)
- [Browser authorization helper](https://github.com/sigma67/ytmusicapi/blob/main/ytmusicapi/helpers.py)
- [Current get_lyrics documentation](https://ytmusicapi.readthedocs.io/en/stable/reference/api/ytmusicapi.mixins.html)

## Request and parsing flow

The owning background session requests a lookup from its content script. A `next` POST to `https://music.youtube.com/youtubei/v1/next` includes the current video ID, `RDAMVM` playlist ID, audio-only flag, persistent playlist panel options and the page's current `WEB_REMIX` version. The watch response's tabs are inspected by semantic `MUSIC_PAGE_TYPE_TRACK_LYRICS` page type, not translated titles or a fixed tab index. Unselectable tabs are skipped. The resulting browse ID normally starts with `MPLYt`.

The `browse` POST uses that ID and a separate client object with `ANDROID_MUSIC`, version `7.21.50`. This is the value in upstream `as_mobile` at inspection time, despite its older-looking version number. It is centralised in `lyrics-api.js`; no global page client configuration is modified. If upstream changes, recheck the reference and test a live track before changing this constant.

Timed data is under `contents.elementRenderer.newElement.type.componentType.model.timedLyricsModel.lyricsData.timedLyricsData`. Each cue's `lyricLine` is preserved, and `cueRange.startTimeMilliseconds` / `endTimeMilliseconds` become integer milliseconds. Explicit end times are retained. Malformed cues are skipped with a count warning, lines are sorted by start time, and an entirely unusable response is a failure. `sourceMessage` is retained as optional attribution.

The untimed alternative is `contents.sectionListRenderer.contents[0].musicDescriptionShelfRenderer.description.runs`. Text establishes availability only; no text timing is inferred. A valid watch response without an enabled lyrics page establishes no lyrics. Unexpected response shapes, HTTP failures and timeouts remain unknown, are logged using fixed error codes, and are not cached as no lyrics.

## Browser session boundary

Requests use Firefox MV2 `content.fetch` with `credentials: include` so they run with the page's origin and cookie context; see [Mozilla's content-script fetch documentation](https://developer.mozilla.org/en-US/docs/Mozilla/Add-ons/WebExtensions/Content_scripts#xhr_and_fetch). The content script reads only necessary page configuration fields: client version, locale, visitor data, session index and API key. If a browser-visible SAPISID/3PAPISID cookie exists, it computes the same short-lived authorization hash as upstream inside the content script. No second login is created, cookies are not exported, and request headers/configuration are neither sent to NativeHost nor logged.

The lookup has a 30-second timeout. Track/ownership changes request cancellation; generation plus session/track validation still protects against late completion. Cache entries live only for the extension lifetime. Failed lookups can retry on a later track visit or owner content-script reload, with no periodic network retry flood.

## Verification

Run the .NET solution tests and `node --test firefox-extension/tests/lyrics.test.cjs`. Static fixtures use invented text and no session data. Tests cover timing preservation, malformed cues, untimed/no-lyrics distinctions, request construction, browser-only credentials, cache reuse, cancellation races, owner-only routing, stale App results and reconnect retention.

Live signed-in verification is still required before calling Milestone 4 complete:

1. Play a track known to have timed lyrics in mobile YouTube Music. Confirm timed=true, a nonzero line count, plausible start/end values and matching text in the App.
2. Skip A → B before A completes. Confirm immediate clearing and that A never replaces B. Return to A and check the cache-hit log.
3. Try untimed and no-lyrics tracks. Confirm distinct status and empty timed lines.
4. Pause, seek and change rate while lyrics are loaded. Confirm smooth playback continues with no additional lookup.
5. Play a non-owner tab and confirm it cannot affect lyrics.
6. Restart the App while Firefox stays open, then separately reconnect Native Messaging. Confirm retained/cached lyrics return without waiting for a track change.

No local lyrics files, provider fallbacks, LRC parsing, timeline selection or karaoke rendering are implemented.

### Live API verification on 2026-09-10

The adapter was exercised in Firefox 155.0.1 using an isolated guest profile (optional cookies rejected, no Google login). The actual content-script request/parser files were used, and only status/count/timestamp summaries were reported:

| Video ID | Observed result |
| --- | --- |
| `hpSrLjc5SMs` (Oasis — Wonderwall, upstream's lyrics test fixture) | Lyrics browse ID found; available=true, timed=true; 40 lines; first explicit range 0–22150 ms |
| `dQw4w9WgXcQ` | No selectable lyrics browse ID; available=false, timed=false, zero lines in this guest session |
| `kJQP7kiw5Fk` | No selectable lyrics browse ID; available=false, timed=false, zero lines in this guest session |

The live check caught a Firefox realm distinction: page-origin fetch is `content.fetch` on the content-script sandbox, not `window.content.fetch`. The implementation and regression harness were corrected accordingly. Timed availability for a particular video can differ from its audio-track counterpart or by session/region; these observations are not universal availability claims. Signed-in visual playback, cache, race and multi-tab scenarios above remain separate from this API smoke check.
