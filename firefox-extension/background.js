"use strict";

const NATIVE_HOST_NAME = "com.lyricsdisplayer.nativehost";
const SNAPSHOT_INTERVAL_MS = 500;
const RECONNECT_INTERVAL_MS = 5000;
const TRACK_LENGTH_MS = 30000;
const SOURCE_SESSION_ID = crypto.randomUUID();

let sequence = 0;
let nativePort = null;
let reconnectTimer = null;
let trackNumber = 1;
let positionMs = 0;
let playing = true;
let lastTickMs = performance.now();

function createEnvelope(messageType, payload) {
  sequence += 1;
  return {
    protocolVersion: 1,
    messageType,
    source: "youtubeMusic",
    sourceSessionId: SOURCE_SESSION_ID,
    sequence,
    sentAtUtc: new Date().toISOString(),
    payload
  };
}

function post(message) {
  if (!nativePort) {
    return false;
  }

  try {
    nativePort.postMessage(message);
    return true;
  } catch (error) {
    console.error("Lyrics Displayer Native Messaging send failed", error);
    return false;
  }
}

function diagnostic(level, category, message) {
  const envelope = createEnvelope("diagnosticLog", { level, category, message });
  console.log(`[${level}] [${category}] ${message}`);
  post(envelope);
}

function connectNativeHost() {
  if (nativePort) {
    return;
  }

  console.log("Connecting to Lyrics Displayer NativeHost");
  try {
    const port = browser.runtime.connectNative(NATIVE_HOST_NAME);
    nativePort = port;
    port.onMessage.addListener(message => {
      console.debug("Unexpected NativeHost response", message);
    });
    port.onDisconnect.addListener(() => {
      const detail = browser.runtime.lastError?.message ?? "Native Messaging port disconnected.";
      console.warn("Lyrics Displayer NativeHost disconnected", detail);
      nativePort = null;
      if (reconnectTimer === null) {
        reconnectTimer = setTimeout(() => {
          reconnectTimer = null;
          connectNativeHost();
        }, RECONNECT_INTERVAL_MS);
      }
    });
    diagnostic("Information", "NativeMessaging", "Native Messaging connection opened.");
    diagnostic("Information", "PlaybackSource", "Synthetic playback source session started.");
  } catch (error) {
    console.error("Could not start Lyrics Displayer NativeHost", error);
    nativePort = null;
    if (reconnectTimer === null) {
      reconnectTimer = setTimeout(() => {
        reconnectTimer = null;
        connectNativeHost();
      }, RECONNECT_INTERVAL_MS);
    }
  }
}

function buildLyrics(currentTrack) {
  const lines = [];
  const lyricText = [
    `Synthetic track ${currentTrack} is travelling through Firefox.`,
    "The native host carries this line across the pipe.",
    "The WPF diagnostic window shows the changing state.",
    `A new synthetic song follows after ${TRACK_LENGTH_MS / 1000} seconds.`,
    "Milestone one uses no YouTube Music page data."
  ];

  for (let index = 0; index < lyricText.length; index += 1) {
    const startMs = index * 6000;
    lines.push({
      startMs,
      endMs: startMs + 6000,
      text: lyricText[index]
    });
  }

  return lines;
}

function sendSnapshot() {
  const now = performance.now();
  if (playing) {
    positionMs += Math.round(now - lastTickMs);
  }
  lastTickMs = now;

  if (positionMs >= TRACK_LENGTH_MS) {
    trackNumber += 1;
    positionMs = 0;
    diagnostic("Information", "PlaybackSource", `Synthetic track changed to track ${trackNumber}.`);
  }

  post(createEnvelope("playbackSnapshot", {
    track: {
      sourceTrackId: `synthetic-track-${trackNumber}`,
      title: `Synthetic Song ${trackNumber}`,
      artist: `Synthetic Artist ${(trackNumber % 4) + 1}`,
      album: `Transport Test Collection ${(trackNumber % 3) + 1}`,
      durationMs: TRACK_LENGTH_MS
    },
    playback: {
      positionMs,
      playing,
      playbackRate: 1.0
    },
    lyrics: {
      available: true,
      timed: true,
      source: "youtubeMusic",
      lines: buildLyrics(trackNumber)
    }
  }));
}

function shutdown() {
  diagnostic("Information", "PlaybackSource", "Synthetic playback source session stopped.");
  if (nativePort) {
    nativePort.disconnect();
    nativePort = null;
  }
}

connectNativeHost();
setInterval(sendSnapshot, SNAPSHOT_INTERVAL_MS);
addEventListener("unload", shutdown);

