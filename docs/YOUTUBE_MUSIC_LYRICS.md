# YouTube Music lyrics adapter

The runtime is entirely JavaScript inside Firefox. `ytmusicapi` is a reference, not a dependency. Upstream `main` and its current documentation were inspected on 2026-09-09/10 before implementing requests:

- [get_watch_playlist](https://github.com/sigma67/ytmusicapi/blob/main/ytmusicapi/mixins/watch.py)
- [get_tab_browse_ids](https://github.com/sigma67/ytmusicapi/blob/main/ytmusicapi/parsers/watch.py)
- [get_lyrics](https://github.com/sigma67/ytmusicapi/blob/main/ytmusicapi/mixins/browsing.py)
- [as_mobile](https://github.com/sigma67/ytmusicapi/blob/main/ytmusicapi/ytmusic.py)
- [TIMESTAMPED_LYRICS navigation](https://github.com/sigma67/ytmusicapi/blob/main/ytmusicapi/navigation.py)
- [LyricLine.from_raw](https://github.com/sigma67/ytmusicapi/blob/main/ytmusicapi/models/lyrics.py)
- [Browser authorization helper](https://github.com/sigma67/ytmusicapi/blob/main/ytmusicapi/helpers.py)
- [Language initialization](https://github.com/sigma67/ytmusicapi/blob/main/ytmusicapi/ytmusic.py)
- [Supported languages FAQ](https://github.com/sigma67/ytmusicapi/blob/main/docs/source/faq.rst#which-values-can-i-use-for-languages)
- [Current get_lyrics documentation](https://ytmusicapi.readthedocs.io/en/stable/reference/api/ytmusicapi.mixins.html)

## Request and parsing flow

The owning background session requests a lookup from its content script. A `next` POST to `https://music.youtube.com/youtubei/v1/next` includes the current video ID, `RDAMVM` playlist ID, audio-only flag, persistent playlist panel options and the page's current `WEB_REMIX` version. The watch response's tabs are inspected by semantic `MUSIC_PAGE_TYPE_TRACK_LYRICS` page type, not translated titles or a fixed tab index. Unselectable tabs are skipped. The resulting browse ID normally starts with `MPLYt`.

The `browse` POST uses that ID and a separate client object with `ANDROID_MUSIC`, version `7.21.50`. This is the value in upstream `as_mobile` at inspection time, despite its older-looking version number. It is centralised in `lyrics-api.js`; no global page client configuration is modified. If upstream changes, recheck the reference and test a live track before changing this constant.

YouTube Music may put entries containing `lyricLine` but no `cueRange` inside `timedLyricsData`. Those entries establish `available=true`, `timed=false`, and no synthetic timeline lines. A present `cueRange` that is incomplete or unparseable is malformed instead: malformed neighbours are skipped when another valid timed cue exists, while an entirely unusable timed response remains an unknown lookup failure. The property name alone does not prove that timestamps exist.

Timed data is under `contents.elementRenderer.newElement.type.componentType.model.timedLyricsModel.lyricsData.timedLyricsData`. Each timed cue's `lyricLine` is preserved, and `cueRange.startTimeMilliseconds` / `endTimeMilliseconds` become integer milliseconds. Explicit end times are retained. Malformed cues are skipped with a count warning, lines are sorted by start time, and an entirely unusable response is a failure. `sourceMessage` is retained as optional attribution.

The untimed alternative is `contents.sectionListRenderer.contents[0].musicDescriptionShelfRenderer.description.runs`. Text establishes availability only; no text timing is inferred. A valid watch response without an enabled lyrics page establishes no lyrics. Unexpected response shapes, HTTP failures and timeouts remain unknown, are logged using fixed error codes, and are not cached as no lyrics.

## Browser session boundary

Both calls use Firefox MV2 `content.fetch` so they run from the page origin; see [Mozilla's content-script fetch documentation](https://developer.mozilla.org/en-US/docs/Mozilla/Add-ons/WebExtensions/Content_scripts#xhr_and_fetch). Their authentication policies intentionally differ and are named constants in `lyrics-api.js`:

- `WEB_REMIX /next` uses `credentials: include` and may add the browser session's `X-Goog-AuthUser` and short-lived `SAPISIDHASH` authorization.
- `ANDROID_MUSIC /browse` uses `credentials: omit` and never adds either authentication header. Authenticated mobile browse requests were observed returning HTTP 400 during signed-in manual testing, whereas the anonymous request succeeds. Do not merge these policies.

The content script reads only necessary page configuration fields: client version, locale, visitor data, session index and API key. Browser authentication is constructed only for the web request. No second login is created, cookies are not exported, and request headers/configuration are neither sent to NativeHost nor logged.

## Lyrics language preference

The mobile lyrics request sets `context.client.hl` from the central `LYRICS_LANGUAGE` constant, currently `zh_TW`. Current upstream `ytmusicapi` lists `zh_TW` as Chinese (Taiwan) and assigns its configured language to `context.client.hl`. This preference is lyrics-only and should become user-configurable in a later settings milestone; playback metadata continues using the page locale.

The locale is a request preference, not a response guarantee. Valid original-language or other-script lyrics remain accepted when YouTube Music ignores the preference or has no Traditional Chinese variant. The extension performs no Simplified-to-Traditional conversion or script detection.

The lookup has a 30-second timeout. Track/ownership changes request cancellation; generation plus session/track validation still protects against late completion. Cache entries live only for the extension lifetime. Failed lookups can retry on a later track visit or owner content-script reload, with no periodic network retry flood.

## Verification

Run the .NET solution tests and `node --test firefox-extension/tests/lyrics.test.cjs`. Static fixtures use invented text and no session data. Tests cover exact timing preservation, malformed timing, `timedLyricsData` without `cueRange`, legacy untimed/no-lyrics distinctions, explicit request authentication policies, the `zh_TW` mobile context, cache reuse, cancellation races, owner-only routing, stale App results and reconnect retention.

Signed-in end-to-end manual verification completed on 2026-09-10. It covered real timed, untimed and unavailable lyrics; timing sanity; rapid A → B and A → B → C transitions; cache reuse; pause/resume and seek; App and NativeHost reconnects; multi-tab ownership; playlists; podcasts; several track types; and browser/extension restart. Full lyrics remained separate from the 500 ms playback snapshots.

No local lyrics files, provider fallbacks, LRC parsing, timeline selection or karaoke rendering are implemented.

### Live API verification on 2026-09-10

The adapter was exercised in Firefox 155.0.1 using an isolated guest profile (optional cookies rejected, no Google login). The actual content-script request/parser files were used, and only status/count/timestamp summaries were reported:

| Video ID | Observed result |
| --- | --- |
| `hpSrLjc5SMs` (Oasis — Wonderwall, upstream's lyrics test fixture) | Lyrics browse ID found; available=true, timed=true; 40 lines; first explicit range 0–22150 ms |
| `dQw4w9WgXcQ` | No selectable lyrics browse ID; available=false, timed=false, zero lines in this guest session |
| `kJQP7kiw5Fk` | No selectable lyrics browse ID; available=false, timed=false, zero lines in this guest session |

The live check caught a Firefox realm distinction: page-origin fetch is `content.fetch` on the content-script sandbox, not `window.content.fetch`. The implementation and regression harness were corrected accordingly. Timed availability for a particular video can differ from its audio-track counterpart or by session/region; these observations are not universal availability claims.

### Traditional Chinese locale check on 2026-09-10

An anonymous `ANDROID_MUSIC /browse` request for the YouTube Music audio track `l6a5D6yxqEU` was made with `hl=zh_TW`. It returned 38 timed lines; a count-only script check found 37 selected Traditional markers and no corresponding Simplified markers. No lyric text, API key, visitor value or credential was printed or saved. A `zh_CN` control returned the same provider-curated Traditional text for this track, so the check confirms that `zh_TW` is accepted and can return Traditional Chinese, but does not prove that changing `hl` performs script conversion for every catalog item. No local conversion or fallback was required.

English timed lyrics were already verified with `hpSrLjc5SMs`; setting the lyrics preference does not translate or alter the original-language lyric text. Other original-language tracks remain accepted without language/script validation.
