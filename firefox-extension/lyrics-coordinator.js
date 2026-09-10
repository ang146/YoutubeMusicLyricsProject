"use strict";

class LyricsCoordinator {
  constructor({ fetchLyrics, cancel, publish, log }) {
    this.fetchLyrics = fetchLyrics;
    this.cancel = cancel;
    this.publish = publish;
    this.log = log;
    this.cache = new Map();
    this.active = null;
    this.generation = 0;
  }

  clear() {
    const old = this.active;
    this.active = null;
    if (old?.pending) {
      this.cancel(old);
      this.log("Information", `Lyrics request cancelled for track ${old.trackId}.`);
    }
  }

  observe(tabId, sessionId, trackId, instanceId) {
    const old = this.active;
    if (old && old.tabId === tabId && old.sessionId === sessionId &&
        old.trackId === trackId && old.instanceId === instanceId) return;
    this.clear();
    const operation = { tabId, sessionId, trackId, instanceId, requestId: String(++this.generation), pending: true };
    this.active = operation;
    if (this.cache.has(trackId)) {
      operation.pending = false;
      operation.result = this.cache.get(trackId);
      this.log("Information", `Lyrics cache hit for track ${trackId}.`);
      this.publish(operation, operation.result);
      return;
    }
    this.log("Information", `Timed lyrics retrieval started for track ${trackId}.`);
    Promise.resolve().then(() => {
      if (this.active !== operation) return null;
      return this.fetchLyrics(operation);
    }).then(response => {
      if (this.active !== operation) {
        this.log("Information", `Stale lyrics result discarded for track ${trackId}.`);
        return;
      }
      operation.pending = false;
      if (!response?.result) {
        this.log("Warning", `Lyrics lookup failed for track ${trackId}; state remains unknown ` +
          `(${safeLyricsError(response?.errorCode)}).`);
        return;
      }
      if (response.browseIdObtained) this.log("Information", `Lyrics browse ID obtained for track ${trackId}.`);
      if (response.skippedLines) this.log("Warning", `Skipped ${response.skippedLines} malformed timed lines for ${trackId}.`);
      operation.result = response.result;
      this.cache.set(trackId, response.result);
      this.log("Information", `${response.result.timed ? "Timed lyrics retrieved" :
        response.result.available ? "Untimed lyrics returned" : "No lyrics available"} for track ${trackId}, ` +
        `lines=${response.result.lines.length}.`);
      this.publish(operation, response.result);
    }).catch(() => {
      if (this.active === operation) {
        operation.pending = false;
        this.log("Warning", `Lyrics lookup failed for track ${trackId}; content script unavailable.`);
      }
    });
  }

  replay() {
    if (this.active?.result) this.publish(this.active, this.active.result);
  }
}

function safeLyricsError(value) {
  return typeof value === "string" && /^[a-z-]{1,50}$|^http-\d{3}$/.test(value) ? value : "request-failed";
}

if (typeof module !== "undefined") module.exports = LyricsCoordinator;
