"use strict";

const SNAPSHOT_INTERVAL_MS = 500;
const IMMEDIATE_OBSERVATION_DELAY_MS = 50;
const METADATA_CONFIRMATION_DELAY_MS = 100;
const CONTENT_INSTANCE_ID = crypto.randomUUID();
const VIDEO_ID_PATTERN = /^[A-Za-z0-9_-]{11}$/;

// YouTube Music-specific DOM knowledge is intentionally centralised here. The
// semantic custom-element/class selectors are tried in order, with Media Session
// metadata as a fallback when Firefox exposes it.
const SELECTORS = Object.freeze({
  media: ["video", "audio"],
  title: [
    "ytmusic-player-bar yt-formatted-string.title",
    "ytmusic-player-bar .title"
  ],
  titleLink: [
    "ytmusic-player-bar yt-formatted-string.title a[href*='watch?v=']",
    "ytmusic-player-bar .title a[href*='watch?v=']"
  ],
  byline: [
    "ytmusic-player-bar yt-formatted-string.byline",
    "ytmusic-player-bar .byline"
  ],
  artistLink: [
    "ytmusic-player-bar .byline a[href*='/channel/']",
    "ytmusic-player-bar .byline a[href*='/browse/UC']",
    "ytmusic-player-bar .byline a"
  ],
  albumLink: [
    "ytmusic-player-bar .byline a[href*='/browse/MPRE']",
    "ytmusic-player-bar .byline a[href*='/browse/OLAK']"
  ]
});

let candidateIdentity = null;
let candidateObservations = 0;
let lastFailure = null;
let lastMissingAlbumTrackId = null;
let boundMediaElement = null;
let immediateObservationTimer = null;
let metadataConfirmationTimer = null;
let pendingImmediateReason = null;

const MEDIA_EVENT_REASONS = Object.freeze({
  play: "play",
  pause: "pause",
  seeked: "seeked",
  ratechange: "ratechange",
  loadedmetadata: "trackchange",
  durationchange: "trackchange"
});

const REASON_PRIORITY = Object.freeze({
  play: 1,
  pause: 1,
  ratechange: 2,
  seeked: 3,
  trackchange: 4
});

function normaliseText(value) {
  return typeof value === "string" ? value.replace(/\s+/g, " ").trim() : "";
}

function firstElement(selectors) {
  for (const selector of selectors) {
    const element = document.querySelector(selector);
    if (element) {
      return element;
    }
  }

  return null;
}

function firstText(selectors) {
  return normaliseText(firstElement(selectors)?.textContent);
}

function findMediaElement() {
  const elements = SELECTORS.media.flatMap(selector => Array.from(document.querySelectorAll(selector)));
  return elements.find(element => Number.isFinite(element.duration) && element.duration > 0) ??
    elements[0] ?? null;
}

function handleMediaEvent(event) {
  scheduleImmediateObservation(MEDIA_EVENT_REASONS[event.type]);
}

function bindMediaEvents(media) {
  if (media === boundMediaElement) {
    return;
  }

  if (boundMediaElement) {
    for (const eventName of Object.keys(MEDIA_EVENT_REASONS)) {
      boundMediaElement.removeEventListener(eventName, handleMediaEvent);
    }
  }

  boundMediaElement = media;
  if (!boundMediaElement) {
    return;
  }

  for (const eventName of Object.keys(MEDIA_EVENT_REASONS)) {
    boundMediaElement.addEventListener(eventName, handleMediaEvent);
  }
}

function scheduleImmediateObservation(reason) {
  if (!reason) {
    return;
  }

  if (!pendingImmediateReason || REASON_PRIORITY[reason] > REASON_PRIORITY[pendingImmediateReason]) {
    pendingImmediateReason = reason;
  }

  if (immediateObservationTimer !== null) {
    return;
  }

  immediateObservationTimer = setTimeout(() => {
    immediateObservationTimer = null;
    const observationReason = pendingImmediateReason;
    pendingImmediateReason = null;
    observePlayback(observationReason);
  }, IMMEDIATE_OBSERVATION_DELAY_MS);
}

function scheduleMetadataConfirmation(reason) {
  if (metadataConfirmationTimer !== null) {
    clearTimeout(metadataConfirmationTimer);
  }

  metadataConfirmationTimer = setTimeout(() => {
    metadataConfirmationTimer = null;
    observePlayback(reason);
  }, METADATA_CONFIRMATION_DELAY_MS);
}

function parseVideoId(url) {
  try {
    const value = new URL(url, location.href).searchParams.get("v") ?? "";
    return VIDEO_ID_PATTERN.test(value) ? value : "";
  } catch {
    return "";
  }
}

function extractVideoId() {
  const titleLink = firstElement(SELECTORS.titleLink);
  const linkedVideoId = titleLink ? parseVideoId(titleLink.href) : "";
  return linkedVideoId || parseVideoId(location.href);
}

function extractArtistFromByline() {
  const byline = firstText(SELECTORS.byline);
  if (!byline) {
    return "";
  }

  const parts = byline.split(/\s*[\u2022\u00b7]\s*/).map(normaliseText).filter(Boolean);
  return parts[0] ?? "";
}

function extractState() {
  const media = findMediaElement();
  if (!media) {
    return { failure: "No HTML media element is currently available." };
  }

  const durationSeconds = media.duration;
  if (!Number.isFinite(durationSeconds) || durationSeconds <= 0) {
    return { failure: "The current media duration is not available yet." };
  }

  const mediaMetadata = navigator.mediaSession?.metadata;
  const bylineArtist = extractArtistFromByline();
  const sourceTrackId = extractVideoId();
  const title = firstText(SELECTORS.title) || normaliseText(mediaMetadata?.title);
  const artist = firstText(SELECTORS.artistLink) || normaliseText(mediaMetadata?.artist) || bylineArtist;
  const extractedAlbum = firstText(SELECTORS.albumLink) || normaliseText(mediaMetadata?.album) || null;

  if (!sourceTrackId) {
    return { failure: "The current YouTube Music video ID is not available yet." };
  }

  if (!title || !artist) {
    return { failure: "The current track title or artist is not available yet." };
  }

  const durationMs = Math.max(1, Math.round(durationSeconds * 1000));
  const positionSeconds = Number.isFinite(media.currentTime) ? media.currentTime : 0;
  const positionMs = Math.round(Math.max(0, Math.min(positionSeconds, durationSeconds)) * 1000);
  const playbackRate = Number.isFinite(media.playbackRate) && media.playbackRate > 0
    ? media.playbackRate
    : 1.0;

  return {
    albumUnavailable: !extractedAlbum,
    state: {
      track: {
        sourceTrackId,
        title,
        artist,
        album: extractedAlbum,
        durationMs
      },
      playback: {
        positionMs,
        playing: !media.paused && !media.ended,
        playbackRate
      }
    }
  };
}

function sendToBackground(message) {
  try {
    const sending = browser.runtime.sendMessage(message);
    sending.catch(error => {
      console.debug("Lyrics Displayer background message was not delivered", error);
    });
  } catch (error) {
    console.debug("Lyrics Displayer background message failed", error);
  }
}

function reportFailure(message) {
  if (message === lastFailure) {
    return;
  }

  lastFailure = message;
  console.warn(`Lyrics Displayer: ${message}`);
  sendToBackground({ type: "ytmDiagnostic", level: "Warning", message });
}

function observePlayback(reason = "periodic") {
  bindMediaEvents(findMediaElement());
  const observation = extractState();
  if (!observation.state) {
    candidateIdentity = null;
    candidateObservations = 0;
    reportFailure(observation.failure);
    return;
  }

  if (lastFailure !== null) {
    console.info("Lyrics Displayer: YouTube Music metadata extraction recovered.");
    sendToBackground({
      type: "ytmDiagnostic",
      level: "Information",
      message: "YouTube Music metadata extraction recovered."
    });
    lastFailure = null;
  }

  const track = observation.state.track;
  const identity = `${track.sourceTrackId}\u0000${track.title}\u0000${track.artist}\u0000${track.album}\u0000${track.durationMs}`;
  if (identity !== candidateIdentity) {
    candidateIdentity = identity;
    candidateObservations = 1;
    if (reason !== "periodic") {
      scheduleMetadataConfirmation(reason);
    }
    return;
  }

  candidateObservations += 1;
  if (candidateObservations < 2) {
    return;
  }

  if (observation.albumUnavailable && lastMissingAlbumTrackId !== track.sourceTrackId) {
    lastMissingAlbumTrackId = track.sourceTrackId;
    sendToBackground({
      type: "ytmDiagnostic",
      level: "Warning",
      message: `Reliable album metadata is unavailable for video ${track.sourceTrackId}; sending null.`
    });
  } else if (!observation.albumUnavailable && lastMissingAlbumTrackId === track.sourceTrackId) {
    lastMissingAlbumTrackId = null;
  }

  sendToBackground({ type: "ytmObservedState", state: observation.state, reason });
}

console.info("Lyrics Displayer content script started for YouTube Music.");
sendToBackground({ type: "ytmContentReady", instanceId: CONTENT_INSTANCE_ID });
observePlayback();
setInterval(observePlayback, SNAPSHOT_INTERVAL_MS);
