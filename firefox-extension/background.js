"use strict";

const NATIVE_HOST_NAME = "com.lyricsdisplayer.nativehost";
const RECONNECT_INTERVAL_MS = 5000;
const YOUTUBE_MUSIC_ORIGIN = "https://music.youtube.com";

// Diagnostics emitted before any tab owns playback use this background-session
// envelope. Playback snapshots always use an ownership-session envelope.
const extensionSession = createProtocolSession();
const contentInstances = new Map();
const ignoredTabs = new Set();
const invalidStateTabs = new Set();
const OBSERVATION_REASONS = new Set([
  "periodic", "play", "pause", "seeked", "ratechange", "trackchange"
]);

let owner = null;
let nativePort = null;
let reconnectTimer = null;

function createProtocolSession() {
  return {
    id: crypto.randomUUID(),
    sequence: 0
  };
}

function currentProtocolSession() {
  return owner?.protocolSession ?? extensionSession;
}

function createEnvelope(session, messageType, payload) {
  session.sequence += 1;
  return {
    protocolVersion: 1,
    messageType,
    source: "youtubeMusic",
    sourceSessionId: session.id,
    sequence: session.sequence,
    sentAtUtc: new Date().toISOString(),
    payload
  };
}

function postEnvelope(session, messageType, payload) {
  const envelope = createEnvelope(session, messageType, payload);
  if (!nativePort) {
    return false;
  }

  try {
    nativePort.postMessage(envelope);
    return true;
  } catch (error) {
    console.error("Lyrics Displayer Native Messaging send failed", error);
    return false;
  }
}

function diagnostic(level, category, message, session = currentProtocolSession()) {
  console.log(`[${level}] [${category}] ${message}`);
  postEnvelope(session, "diagnosticLog", { level, category, message });
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
      if (nativePort !== port) {
        return;
      }

      const detail = port.error?.message ?? browser.runtime.lastError?.message ??
        "Native Messaging port disconnected.";
      console.warn("Lyrics Displayer NativeHost disconnected", detail);
      nativePort = null;
      scheduleNativeReconnect();
    });
    diagnostic("Information", "NativeMessaging", "Native Messaging connection opened.");
  } catch (error) {
    console.error("Could not start Lyrics Displayer NativeHost", error);
    nativePort = null;
    scheduleNativeReconnect();
  }
}

function scheduleNativeReconnect() {
  if (reconnectTimer !== null) {
    return;
  }

  reconnectTimer = setTimeout(() => {
    reconnectTimer = null;
    connectNativeHost();
  }, RECONNECT_INTERVAL_MS);
}

function isYouTubeMusicUrl(url) {
  try {
    const parsed = new URL(url);
    return parsed.origin === YOUTUBE_MUSIC_ORIGIN;
  } catch {
    return false;
  }
}

function isObservedStateValid(state) {
  return Boolean(
    state &&
    state.track &&
    typeof state.track.sourceTrackId === "string" && state.track.sourceTrackId.length > 0 &&
    typeof state.track.title === "string" && state.track.title.length > 0 &&
    typeof state.track.artist === "string" && state.track.artist.length > 0 &&
    (state.track.album === null || typeof state.track.album === "string") &&
    Number.isSafeInteger(state.track.durationMs) && state.track.durationMs > 0 &&
    state.playback &&
    Number.isSafeInteger(state.playback.positionMs) && state.playback.positionMs >= 0 &&
    typeof state.playback.playing === "boolean" &&
    Number.isFinite(state.playback.playbackRate) && state.playback.playbackRate > 0
  );
}

function handleContentReady(tabId, message) {
  const previousInstance = contentInstances.get(tabId);
  contentInstances.set(tabId, message.instanceId);

  if (previousInstance === undefined) {
    diagnostic("Information", "PlaybackSource", `YouTube Music tab ${tabId} detected.`);
  } else if (previousInstance !== message.instanceId) {
    diagnostic("Information", "PlaybackSource",
      `YouTube Music content script reconnected in tab ${tabId}; ownership was preserved.`);
  }
}

function handleObservedState(tabId, state, reason) {
  if (!isObservedStateValid(state)) {
    if (!invalidStateTabs.has(tabId)) {
      invalidStateTabs.add(tabId);
      diagnostic("Warning", "PlaybackSource", `Ignored invalid observed state from tab ${tabId}.`);
    }
    return;
  }

  invalidStateTabs.delete(tabId);

  if (owner === null) {
    if (!state.playback.playing) {
      return;
    }

    diagnostic("Information", "Ownership", `YouTube Music tab ${tabId} attempted to claim ownership.`);
    owner = {
      tabId,
      protocolSession: createProtocolSession(),
      lastTrackId: state.track.sourceTrackId,
      lastPlaying: state.playback.playing,
      lastPlaybackRate: state.playback.playbackRate
    };
    ignoredTabs.clear();
    diagnostic("Information", "Ownership",
      `Ownership granted to YouTube Music tab ${tabId}.`, owner.protocolSession);
    sendSnapshot(owner.protocolSession, state);
    return;
  }

  if (owner.tabId !== tabId) {
    if (state.playback.playing && !ignoredTabs.has(tabId)) {
      ignoredTabs.add(tabId);
      diagnostic("Information", "Ownership",
        `Ignoring playing tab ${tabId}; tab ${owner.tabId} already owns the source.`);
    }
    return;
  }

  if (state.track.sourceTrackId !== owner.lastTrackId) {
    const previousTrackId = owner.lastTrackId;
    owner.lastTrackId = state.track.sourceTrackId;
    diagnostic("Information", "PlaybackSource",
      `Owner tab ${tabId} changed track from ${previousTrackId} to ${state.track.sourceTrackId}.`,
      owner.protocolSession);
  }

  if (state.playback.playing !== owner.lastPlaying) {
    diagnostic("Information", "PlaybackSource",
      state.playback.playing
        ? `Playback started or resumed in owner tab ${tabId}.`
        : `Playback paused in owner tab ${tabId}.`,
      owner.protocolSession);
  }

  if (state.playback.playbackRate !== owner.lastPlaybackRate) {
    diagnostic("Information", "PlaybackSource",
      `Playback rate changed from ${owner.lastPlaybackRate} to ${state.playback.playbackRate} ` +
      `in owner tab ${tabId}.`, owner.protocolSession);
  }

  if (reason === "seeked") {
    diagnostic("Information", "PlaybackSource",
      `Seek completed in owner tab ${tabId}; authoritative position is ` +
      `${state.playback.positionMs} ms.`, owner.protocolSession);
  }

  owner.lastPlaying = state.playback.playing;
  owner.lastPlaybackRate = state.playback.playbackRate;

  sendSnapshot(owner.protocolSession, state);
}

function sendSnapshot(session, state) {
  postEnvelope(session, "playbackSnapshot", {
    track: state.track,
    playback: state.playback,
    lyrics: {
      available: false,
      timed: false,
      source: null,
      lines: []
    }
  });
}

function releaseOwner(reason) {
  if (owner === null) {
    return;
  }

  const released = owner;
  diagnostic("Information", "Ownership",
    `Released ownership from YouTube Music tab ${released.tabId}: ${reason}.`,
    released.protocolSession);
  owner = null;
  ignoredTabs.clear();
  invalidStateTabs.delete(released.tabId);
}

browser.runtime.onMessage.addListener((message, sender) => {
  const tabId = sender.tab?.id;
  if (!Number.isInteger(tabId) || !isYouTubeMusicUrl(sender.url ?? sender.tab?.url ?? "")) {
    return undefined;
  }

  switch (message?.type) {
    case "ytmContentReady":
      if (typeof message.instanceId === "string" && message.instanceId.length > 0) {
        handleContentReady(tabId, message);
      }
      break;

    case "ytmObservedState":
      handleObservedState(tabId, message.state,
        OBSERVATION_REASONS.has(message.reason) ? message.reason : "periodic");
      break;

    case "ytmDiagnostic":
      if (typeof message.message === "string" && message.message.length > 0) {
        const level = ["Debug", "Information", "Warning", "Error"].includes(message.level)
          ? message.level
          : "Warning";
        diagnostic(level, "PlaybackSource", `Tab ${tabId}: ${message.message}`);
      }
      break;

    default:
      break;
  }

  return undefined;
});

browser.tabs.onRemoved.addListener(tabId => {
  contentInstances.delete(tabId);
  ignoredTabs.delete(tabId);
  invalidStateTabs.delete(tabId);
  if (owner?.tabId === tabId) {
    releaseOwner("the owning tab was closed");
  }
});

browser.tabs.onUpdated.addListener((tabId, changeInfo) => {
  if (typeof changeInfo.url !== "string" || isYouTubeMusicUrl(changeInfo.url)) {
    return;
  }

  contentInstances.delete(tabId);
  ignoredTabs.delete(tabId);
  invalidStateTabs.delete(tabId);
  if (owner?.tabId === tabId) {
    releaseOwner("the owning tab navigated away from YouTube Music");
  }
});

function shutdown() {
  diagnostic("Information", "PlaybackSource", "YouTube Music background source session stopped.");
  if (nativePort) {
    nativePort.disconnect();
    nativePort = null;
  }
}

connectNativeHost();
addEventListener("unload", shutdown);
