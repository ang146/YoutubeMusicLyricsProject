"use strict";

// Requests run only after the background has authorised this owning tab.
// Configuration and credentials remain within this content script and Firefox.
(() => {
  let active = null;
  const ORIGIN = "https://music.youtube.com";
  const ERROR_CODES = new Set([
    "browser-context-unavailable", "browser-fetch-unavailable", "watch-response-shape", "lyrics-browse-id-shape",
    "timed-response-shape", "no-usable-timed-lines", "lyrics-response-shape"
  ]);

  async function request(endpoint, body, mobile, signal) {
    const config = window.wrappedJSObject?.ytcfg?.data_;
    const webClient = config?.INNERTUBE_CONTEXT?.client;

    if (typeof webClient?.clientVersion !== "string") {
      throw new Error("browser-context-unavailable");
    }

    const client = {
      clientName: mobile
        ? YtmLyrics.MOBILE_CLIENT.clientName
        : "WEB_REMIX",

      clientVersion: mobile
        ? YtmLyrics.MOBILE_CLIENT.clientVersion
        : webClient.clientVersion,

      hl: typeof webClient.hl === "string" ? webClient.hl : "en",
      gl: typeof webClient.gl === "string" ? webClient.gl : "US"
    };

    const visitor = webClient.visitorData ?? config.VISITOR_DATA;

    if (typeof visitor === "string") {
      client.visitorData = visitor;
    }

    const headers = {
      "Content-Type": "application/json",
      "X-Origin": ORIGIN
    };

    if (typeof visitor === "string") {
      headers["X-Goog-Visitor-Id"] = visitor;
    }

    // Only use the logged-in browser session for normal WEB_REMIX requests.
    // ANDROID_MUSIC timed-lyrics requests are sent anonymously.
    if (!mobile) {
      if (config.SESSION_INDEX !== undefined) {
        headers["X-Goog-AuthUser"] = String(config.SESSION_INDEX);
      }

      const cookies = document.cookie
        .split(";")
        .map(item => item.trim());

      const cookie =
        cookies.find(item => item.startsWith("SAPISID=")) ??
        cookies.find(item => item.startsWith("__Secure-3PAPISID="));

      if (cookie) {
        const secret = cookie.slice(cookie.indexOf("=") + 1);
        const timestamp = Math.floor(Date.now() / 1000);

        const digest = await crypto.subtle.digest(
          "SHA-1",
          new TextEncoder().encode(
            `${timestamp} ${secret} ${ORIGIN}`
          )
        );

        const hash = Array.from(
          new Uint8Array(digest),
          value => value.toString(16).padStart(2, "0")
        ).join("");

        headers.Authorization =
          `SAPISIDHASH ${timestamp}_${hash}`;
      }
    }

    const url = new URL(
      `/youtubei/v1/${endpoint}`,
      ORIGIN
    );

    url.searchParams.set("prettyPrint", "false");

    if (typeof config.INNERTUBE_API_KEY === "string") {
      url.searchParams.set(
        "key",
        config.INNERTUBE_API_KEY
      );
    }

    if (
      typeof content === "undefined" ||
      typeof content.fetch !== "function"
    ) {
      throw new Error("browser-fetch-unavailable");
    }

    const response = await content.fetch(url.href, {
      method: "POST",

      // Important:
      // WEB_REMIX = logged-in browser session
      // ANDROID_MUSIC = anonymous
      credentials: mobile ? "omit" : "include",

      headers,
      signal,

      body: JSON.stringify({
        ...body,
        context: {
          client,
          user: {}
        }
      })
    });

    if (!response.ok) {
      console.warn(
        `[Lyrics debug] ${endpoint} failed:`,
        response.status,
        response.statusText
      );

      throw new Error(`http-${response.status}`);
    }

    const json = await response.json();

    if (json.error) {
      throw new Error("api-error");
    }

    return json;
  }

  async function retrieve(message) {
    active?.controller.abort();
    const operation = { id: message.requestId, controller: new AbortController() };
    active = operation;
    const timeout = setTimeout(() => operation.controller.abort(), 30000);
    try {
      const next = await request("next", {
        videoId: message.sourceTrackId, playlistId: `RDAMVM${message.sourceTrackId}`,
        enablePersistentPlaylistPanel: true, isAudioOnly: true, tunerSettingValue: "AUTOMIX_SETTING_NORMAL",
        watchEndpointMusicSupportedConfigs: {
          watchEndpointMusicConfig: { hasPersistentPlaylistPanel: true, musicVideoType: "MUSIC_VIDEO_TYPE_ATV" }
        }
      }, false, operation.controller.signal);
      const id = YtmLyrics.browseId(next);
      if (!id) return { result: YtmLyrics.unavailable(), browseIdObtained: false };
      const response = await request("browse", { browseId: id }, true, operation.controller.signal);
      return { ...YtmLyrics.parse(response), browseIdObtained: true };
    } catch (error) {
      // Do not forward arbitrary server/error text that could contain sensitive request details.
      const errorCode = operation.controller.signal.aborted ? "cancelled-or-timeout" :
        ERROR_CODES.has(error.message) || /^http-\d{3}$/.test(error.message) ? error.message : "lyrics-request-failed";
      return { errorCode };
    } finally {
      clearTimeout(timeout);
      if (active === operation) active = null;
    }
  }

  browser.runtime.onMessage.addListener((message, sender) => {
    if (sender.id !== browser.runtime.id) return undefined;
    if (message?.type === "ytmCancelLyrics") {
      if (active?.id === message.requestId) active.controller.abort();
      return undefined;
    }
    if (message?.type === "ytmFetchLyrics" && /^[A-Za-z0-9_-]{11}$/.test(message.sourceTrackId) &&
        typeof message.requestId === "string") return retrieve(message);
    return undefined;
  });
  addEventListener("pagehide", () => active?.controller.abort());
})();
