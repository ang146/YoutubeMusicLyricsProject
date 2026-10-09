namespace LyricsDisplayer.Infrastructure.Commands;

/// <summary>Stable technical identities used when constructing App commands.</summary>
internal static class ApplicationCommandIds
{
    internal static class LyricsSurface
    {
        public const string OpenLrcExternally = "application.open-lrc-externally";
        public const string OpenBuiltInEditor = "application.open-built-in-editor";
        public const string CurrentLineMinus500 = "application.lyrics.current-line.minus-500";
        public const string CurrentLineMinus100 = "application.lyrics.current-line.minus-100";
        public const string CurrentLinePlus100 = "application.lyrics.current-line.plus-100";
        public const string CurrentLinePlus500 = "application.lyrics.current-line.plus-500";
        public const string AllLyricsMinus500 = "application.lyrics.all.minus-500";
        public const string AllLyricsMinus100 = "application.lyrics.all.minus-100";
        public const string AllLyricsPlus100 = "application.lyrics.all.plus-100";
        public const string AllLyricsPlus500 = "application.lyrics.all.plus-500";
    }

    internal static class MainWindow
    {
        public const string OpenSettings = "application.open-settings";
    }

    internal static class Overlay
    {
        public const string OpenLyricsWindow = "application.overlay.open-lyrics-window";
        public const string SetOneLine = "application.overlay.display.one-line";
        public const string SetTwoLines = "application.overlay.display.two-lines";
        public const string SetAllLyrics = "application.overlay.display.all-lyrics";
        public const string ToggleLocked = "application.overlay.toggle-lock";
        public const string ToggleClickThrough = "application.overlay.toggle-click-through";
        public const string Hide = "application.overlay.hide";
    }

    internal static class Editor
    {
        public const string Save = "editor.save";
        public const string Undo = "editor.undo";
        public const string Redo = "editor.redo";
        public const string InsertRowAbove = "editor.row.insert-above";
        public const string InsertRowBelow = "editor.row.insert-below";
        public const string AppendRow = "editor.row.append";
        public const string DeleteRow = "editor.row.delete";
        public const string ClearCell = "editor.cell.clear";
        public const string SetTimestampFromPlayback = "editor.timestamp.from-playback";
        public const string ShiftAllTimestamps = "editor.timing.shift-all";
        public const string ShiftSelectedLineTiming = "editor.timing.shift-selected";
        public const string CommitMetadata = "editor.metadata.commit";
        public const string ClearTitleOverride = "editor.metadata.clear-title";
        public const string ClearArtistOverride = "editor.metadata.clear-artist";
        public const string ReloadExternal = "editor.external.reload";
    }

    internal static class Debug
    {
        public const string OpenLogsFolder = "debug.open-logs-folder";
        public const string OpenCrashReportsFolder = "debug.open-crash-reports-folder";
    }
}
