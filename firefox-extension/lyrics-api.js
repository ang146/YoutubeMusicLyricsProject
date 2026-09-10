"use strict";

// Minimal response parser, based on sigma67/ytmusicapi main inspected 2026-09-09.
// See docs/YOUTUBE_MUSIC_LYRICS.md for upstream paths and client-version provenance.
const YtmLyrics = (() => {
  const MOBILE_CLIENT = Object.freeze({ clientName: "ANDROID_MUSIC", clientVersion: "7.21.50" });
  const unavailable = () => ({ available: false, timed: false, source: null, lines: [], attribution: null });

  function browseId(response) {
    const renderer = response?.contents?.singleColumnMusicWatchNextResultsRenderer
      ?.tabbedRenderer?.watchNextTabbedResultsRenderer;
    if (!Array.isArray(renderer?.tabs) || renderer.tabs.length < 2) {
      throw new Error("watch-response-shape");
    }
    for (const tab of renderer.tabs) {
      const value = tab.tabRenderer;
      if (!value || Object.hasOwn(value, "unselectable")) continue;
      const endpoint = value.endpoint?.browseEndpoint;
      const pageType = endpoint?.browseEndpointContextSupportedConfigs?.browseEndpointContextMusicConfig?.pageType;
      if (pageType === "MUSIC_PAGE_TYPE_TRACK_LYRICS") {
        if (typeof endpoint.browseId !== "string" || !endpoint.browseId.startsWith("MPLYt")) {
          throw new Error("lyrics-browse-id-shape");
        }
        return endpoint.browseId;
      }
    }
    // A valid watch tab list without a selectable lyrics page is upstream's no-lyrics case.
    if (!renderer.tabs.every(tab => tab?.tabRenderer)) throw new Error("watch-response-shape");
    return null;
  }

  function milliseconds(value) {
    if (typeof value === "string" && !/^\d+$/.test(value)) return null;
    if (typeof value !== "string" && typeof value !== "number") return null;
    const parsed = Number(value);
    return Number.isSafeInteger(parsed) && parsed >= 0 ? parsed : null;
  }

  function parse(response) {
    const data = response?.contents?.elementRenderer?.newElement?.type?.componentType
      ?.model?.timedLyricsModel?.lyricsData;

    if (data !== undefined) {
      if (!Array.isArray(data?.timedLyricsData)) {
        throw new Error("timed-response-shape");
      }

      const attribution =
        typeof data.sourceMessage === "string"
          ? data.sourceMessage
          : null;

      const lines = [];
      let skippedLines = 0;
      let untimedLines = 0;

      for (const raw of data.timedLyricsData) {
        if (typeof raw?.lyricLine !== "string") {
          skippedLines++;
          continue;
        }

        const startMs = milliseconds(
          raw?.cueRange?.startTimeMilliseconds
        );

        const endMs = milliseconds(
          raw?.cueRange?.endTimeMilliseconds
        );

        // Valid text, but no timing information.
        if (startMs === null && endMs === null) {
          untimedLines++;
          continue;
        }

        // Timing exists but is malformed/incomplete.
        if (
          startMs === null ||
          endMs === null ||
          endMs < startMs
        ) {
          skippedLines++;
          continue;
        }

        lines.push({
          startMs,
          endMs,
          text: raw.lyricLine
        });
      }

      // At least one valid timed line exists.
      if (lines.length > 0) {
        lines.sort((a, b) => a.startMs - b.startMs);

        return {
          result: {
            available: true,
            timed: true,
            source: "youtubeMusic",
            lines,
            attribution
          },
          skippedLines
        };
      }

      // YouTube supplied actual lyric text, but without cueRange/timestamps.
      if (untimedLines > 0) {
        return {
          result: {
            available: true,
            timed: false,
            source: "youtubeMusic",
            lines: [],
            attribution
          },
          skippedLines
        };
      }

      // Response contained entries, but none were understandable.
      if (data.timedLyricsData.length > 0) {
        throw new Error("no-usable-timed-lines");
      }

      // Empty lyrics dataset.
      return {
        result: {
          available: true,
          timed: false,
          source: "youtubeMusic",
          lines: [],
          attribution
        },
        skippedLines: 0
      };
    }

    const shelf =
      response?.contents?.sectionListRenderer?.contents?.[0]
        ?.musicDescriptionShelfRenderer;

    const runs = shelf?.description?.runs;

    if (
      !Array.isArray(runs) ||
      !runs.every(run => typeof run?.text === "string")
    ) {
      throw new Error("lyrics-response-shape");
    }

    const hasText = runs.some(
      run => run.text.trim().length > 0
    );

    const attribution = Array.isArray(shelf.footer?.runs)
      ? shelf.footer.runs
          .map(run => typeof run.text === "string" ? run.text : "")
          .join("")
      : Array.isArray(shelf.runs)
        ? shelf.runs
            .map(run => run.text ?? "")
            .join("")
        : null;

    return {
      result: hasText
        ? {
            available: true,
            timed: false,
            source: "youtubeMusic",
            lines: [],
            attribution
          }
        : unavailable(),
      skippedLines: 0
    };
  }

  return { MOBILE_CLIENT, browseId, parse, unavailable };
})();

if (typeof module !== "undefined") module.exports = YtmLyrics;
