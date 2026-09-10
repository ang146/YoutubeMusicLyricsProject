const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
const parser = require('../lyrics-api.js');
const Coordinator = require('../lyrics-coordinator.js');

// Sanitised fixtures reproduce upstream paths, not copyrighted song text or session data.
const rawLine = (start, end, text = '測試 "line"\nnext') => ({ lyricLine: text,
  cueRange: { startTimeMilliseconds: start, endTimeMilliseconds: end, metadata: { id: '1' } } });
const timedResponse = lines => ({ contents: { elementRenderer: { newElement: { type: { componentType: {
  model: { timedLyricsModel: { lyricsData: { timedLyricsData: lines, sourceMessage: 'Test attribution' } } }
} } } } } });
const watchResponse = tabs => ({ contents: { singleColumnMusicWatchNextResultsRenderer: {
  tabbedRenderer: { watchNextTabbedResultsRenderer: { tabs } }
} } });
const lyricsTab = { tabRenderer: { endpoint: { browseEndpoint: { browseId: 'MPLYt_test',
  browseEndpointContextSupportedConfigs: { browseEndpointContextMusicConfig: { pageType: 'MUSIC_PAGE_TYPE_TRACK_LYRICS' } }
} } } };

test('browse ID uses semantic lyrics page type, independent of tab order or title', () => {
  assert.equal(parser.browseId(watchResponse([{ tabRenderer: {} }, { tabRenderer: {} }, lyricsTab])), 'MPLYt_test');
});
test('disabled lyrics tab is known unavailable; missing response shape is failure', () => {
  assert.equal(parser.browseId(watchResponse([{ tabRenderer: {} }, { tabRenderer: { unselectable: true } }])), null);
  assert.throws(() => parser.browseId({ error: {} }));
});
test('timed parser preserves explicit end times, text and attribution, orders lines', () => {
  const { result } = parser.parse(timedResponse([rawLine('10680', '12540'), rawLine('9200', '10630', '')]));
  assert.equal(result.available, true);
  assert.equal(result.timed, true);
  assert.deepEqual(result.lines, [ { startMs: 9200, endMs: 10630, text: '' },
    { startMs: 10680, endMs: 12540, text: '測試 "line"\nnext' } ]);
  assert.equal(result.attribution, 'Test attribution');
});
test('malformed individual cues are skipped; no times are invented', () => {
  const { result, skippedLines } = parser.parse(timedResponse([
    rawLine('1', '10'), rawLine(null, '10'), rawLine('2.5', '10'), rawLine('20', '10'), rawLine('2', undefined)
  ]));
  assert.equal(result.lines.length, 1);
  assert.equal(skippedLines, 4);
  assert.throws(() => parser.parse(timedResponse([rawLine('1', null)])));
  assert.throws(() => parser.parse(timedResponse([
    { lyricLine: 'Malformed', cueRange: { startTimeMilliseconds: 'invalid', endTimeMilliseconds: 'also-invalid' } }
  ])), /no-usable-timed-lines/);
});
test('timedLyricsData without cueRange is valid untimed lyrics', () => {
  const { result, skippedLines } = parser.parse(timedResponse([
    { lyricLine: '測試 歌詞' },
    { lyricLine: '第二行' }
  ]));
  assert.equal(result.available, true);
  assert.equal(result.timed, false);
  assert.deepEqual(result.lines, []);
  assert.equal(skippedLines, 0);
});
test('valid timed lines survive malformed neighbours', () => {
  const { result, skippedLines } = parser.parse(timedResponse([
    rawLine('1000', '2500', 'Valid'),
    { lyricLine: 'Malformed', cueRange: { startTimeMilliseconds: 'invalid', endTimeMilliseconds: '3000' } }
  ]));
  assert.deepEqual(result.lines, [{ startMs: 1000, endMs: 2500, text: 'Valid' }]);
  assert.equal(skippedLines, 1);
});
test('known no-lyrics result remains unavailable', () => {
  assert.deepEqual(parser.unavailable(), {
    available: false, timed: false, source: null, lines: [], attribution: null
  });
});
test('untimed text is available without fake lines; unknown response is a failure', () => {
  const parsed = parser.parse({ contents: { sectionListRenderer: { contents: [
    { musicDescriptionShelfRenderer: { description: { runs: [{ text: 'Untimed test' }] } } }
  ] } } });
  assert.equal(parsed.result.available, true);
  assert.equal(parsed.result.timed, false);
  assert.deepEqual(parsed.result.lines, []);
  assert.throws(() => parser.parse({ contents: {} }));
});

const flush = () => new Promise(resolve => setImmediate(resolve));
function harness() {
  const requests = [], published = [], cancelled = [], logs = [];
  const coordinator = new Coordinator({
    fetchLyrics: operation => new Promise(resolve => requests.push({ operation, resolve })),
    cancel: operation => cancelled.push(operation),
    publish: (operation, result) => published.push({ operation, result }),
    log: (...args) => logs.push(args)
  });
  return { coordinator, requests, published, cancelled, logs };
}
test('A to B race discards late A even when cancellation cannot stop it', async () => {
  const h = harness();
  h.coordinator.observe(1, 'session', 'a', 'instance'); await flush();
  h.coordinator.observe(1, 'session', 'b', 'instance'); await flush();
  h.requests[1].resolve({ result: parser.unavailable() }); await flush();
  h.requests[0].resolve({ result: parser.parse(timedResponse([rawLine(1, 2)])).result }); await flush();
  assert.equal(h.cancelled.length, 1);
  assert.equal(h.published.length, 1);
  assert.equal(h.published[0].operation.trackId, 'b');
});
test('same-track playback never refetches; return uses cache; reconnect replays', async () => {
  const h = harness();
  h.coordinator.observe(1, 'session', 'a', 'instance'); await flush();
  h.requests[0].resolve({ result: parser.unavailable() }); await flush();
  for (let i = 0; i < 50; i++) h.coordinator.observe(1, 'session', 'a', 'instance');
  assert.equal(h.requests.length, 1);
  assert.equal(h.published.length, 1);
  h.coordinator.observe(1, 'session', 'b', 'instance'); await flush();
  h.coordinator.observe(1, 'session', 'a', 'instance'); await flush();
  assert.equal(h.requests.length, 2);
  assert.equal(h.published.length, 2);
  h.coordinator.replay();
  assert.equal(h.published.length, 3);
});
test('failures stay unknown, are not cached and can retry on a later visit', async () => {
  const h = harness();
  h.coordinator.observe(1, 'session', 'a', 'instance'); await flush();
  h.requests[0].resolve({ errorCode: 'http-503' }); await flush();
  h.coordinator.observe(1, 'session', 'a', 'instance'); await flush();
  assert.equal(h.requests.length, 1);
  assert.equal(h.published.length, 0);
  assert.equal(h.coordinator.cache.size, 0);
  h.coordinator.clear(); h.coordinator.observe(1, 'new-session', 'a', 'instance'); await flush();
  assert.equal(h.requests.length, 2);
});
test('ownership release rejects in-flight results, including same track in a new session', async () => {
  const h = harness();
  h.coordinator.observe(1, 'old', 'a', 'instance'); await flush();
  h.coordinator.clear(); h.coordinator.observe(2, 'new', 'a', 'instance'); await flush();
  h.requests[0].resolve({ result: parser.unavailable() }); await flush();
  assert.equal(h.published.length, 0);
});
test('actual background routes lookups only for owner and keeps full lines out of playback', async () => {
  let receive;
  const envelopes = [], fetches = [];
  let id = 0;
  const context = vm.createContext({ console: { log() {}, debug() {}, warn() {}, error() {} }, URL,
    crypto: { randomUUID: () => `session-${++id}` }, setTimeout, clearTimeout, addEventListener() {},
    browser: { runtime: {
      connectNative: () => ({ postMessage: m => envelopes.push(m), onMessage: { addListener() {} }, onDisconnect: { addListener() {} } }),
      onMessage: { addListener: fn => receive = fn }
    }, tabs: { sendMessage: async (tabId, message) => {
      if (message.type === 'ytmFetchLyrics') fetches.push(tabId);
      return { result: parser.parse(timedResponse([rawLine(1, 2)])).result };
    }, onRemoved: { addListener() {} }, onUpdated: { addListener() {} } } }
  });
  for (const file of ['lyrics-coordinator.js', 'background.js'])
    vm.runInContext(fs.readFileSync(path.join(__dirname, '..', file), 'utf8'), context);
  const send = (tabId, trackId) => receive({ type: 'ytmObservedState', state: {
    track: { sourceTrackId: trackId, title: 'Test', artist: 'Test', album: null, durationMs: 1000 },
    playback: { playing: true, positionMs: 0, playbackRate: 1 }
  } }, { tab: { id: tabId }, url: 'https://music.youtube.com/' });
  send(1, 'aaaaaaaaaaa'); await flush();
  send(2, 'bbbbbbbbbbb'); send(1, 'aaaaaaaaaaa'); await flush();
  assert.deepEqual(fetches, [1]);
  assert.equal(envelopes.filter(e => e.messageType === 'lyricsSnapshot').length, 1);
  assert.ok(envelopes.filter(e => e.messageType === 'playbackSnapshot').every(e => e.payload.lyrics.lines.length === 0));
});

test('content flow authenticates WEB_REMIX but keeps mobile lyrics anonymous and requests zh_TW', async () => {
  let receive;
  const requests = [];
  const context = vm.createContext({ YtmLyrics: parser, URL, TextEncoder, Uint8Array, AbortController, setTimeout, clearTimeout,
    crypto: require('node:crypto').webcrypto, addEventListener() {}, document: { cookie: 'SAPISID=test-secret' },
    window: { wrappedJSObject: { ytcfg: { data_: { INNERTUBE_CONTEXT: { client: {
      clientVersion: 'web-test-version', hl: 'en', gl: 'US', visitorData: 'test-visitor'
    } }, INNERTUBE_API_KEY: 'test-key', SESSION_INDEX: 0 } } }, content: { fetch: async (url, init) => {
      requests.push({ url, ...init, body: JSON.parse(init.body) });
      return { ok: true, json: async () => requests.length === 1
        ? watchResponse([{ tabRenderer: {} }, lyricsTab]) : timedResponse([rawLine('10', '20')]) };
    } } }, browser: { runtime: { id: 'test-extension', onMessage: { addListener: fn => receive = fn } } }
  });
  // Firefox MV2 exposes page-origin fetch on the sandbox global, not window.content.
  context.content = context.window.content;
  context.window.content = {};
  vm.runInContext(fs.readFileSync(path.join(__dirname, '..', 'lyrics-content.js'), 'utf8'), context);
  const response = await receive({ type: 'ytmFetchLyrics', requestId: '1', sourceTrackId: 'aaaaaaaaaaa' }, { id: 'test-extension' });
  assert.equal(requests.length, 2);
  assert.equal(requests[0].body.videoId, 'aaaaaaaaaaa');
  assert.equal(requests[0].body.context.client.clientName, 'WEB_REMIX');
  assert.equal(requests[1].body.browseId, 'MPLYt_test');
  assert.equal(requests[1].body.context.client.clientName, 'ANDROID_MUSIC');
  assert.equal(requests[1].body.context.client.clientVersion, '7.21.50');
  assert.equal(requests[0].credentials, 'include');
  assert.match(requests[0].headers.Authorization, /^SAPISIDHASH /);
  assert.equal(requests[0].headers['X-Goog-AuthUser'], '0');
  assert.equal(requests[0].body.context.client.hl, 'en');
  assert.equal(requests[1].credentials, 'omit');
  assert.equal(requests[1].headers.Authorization, undefined);
  assert.equal(requests[1].headers['X-Goog-AuthUser'], undefined);
  assert.equal(requests[1].body.context.client.hl, parser.LYRICS_LANGUAGE);
  assert.equal(parser.LYRICS_LANGUAGE, 'zh_TW');
  assert.equal(requests[1].body.context.client.visitorData, 'test-visitor');
  assert.equal(requests[1].headers['X-Goog-Visitor-Id'], 'test-visitor');
  assert.equal(new URL(requests[1].url).searchParams.get('key'), 'test-key');
  assert.equal(response.result.timed, true);
  assert.ok(!JSON.stringify(response).includes('test-secret'));
  assert.ok(!JSON.stringify(response).includes('test-visitor'));
});
