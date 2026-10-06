using LyricsDisplayer.Core.Library;
using Microsoft.Extensions.Logging;

namespace LyricsDisplayer;

/// <summary>Persists safe timing corrections directly into the active track's authoritative local LRC.</summary>
public sealed class CurrentLyricsTimingService : ICurrentLyricsTimingService
{
    private readonly PlaybackStateCoordinator _playback;
    private readonly LyricsLibrary _library;
    private readonly ILyricsTimingAdjustmentService _adjustments;
    private readonly ILogger<CurrentLyricsTimingService> _logger;

    public CurrentLyricsTimingService(PlaybackStateCoordinator playback, LyricsLibrary library,
        ILyricsTimingAdjustmentService adjustments, ILogger<CurrentLyricsTimingService> logger)
    {
        _playback = playback ?? throw new ArgumentNullException(nameof(playback));
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _adjustments = adjustments ?? throw new ArgumentNullException(nameof(adjustments));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _playback.LyricsActionAvailabilityChanged += OnAvailabilityChanged;
    }

    public bool CanAdjustCurrentLine => _playback.CanAdjustCurrentLineTiming;

    public bool CanShiftAll => !_playback.IsEditorPreviewActive && _playback.IsCurrentLocalLrcUsable &&
        _playback.CurrentLocalLyrics is { IsTimed: true, IsLrcWritable: true, LrcContentHash: not null } &&
        _playback.ActiveLocalLyricsRecord is not null;

    public event Action? AvailabilityChanged;

    public TimingAdjustmentResult AdjustCurrentLine(long deltaMs)
    {
        var target = _playback.CaptureCurrentLineTimingTarget();
        if (target is null)
            return new(TimingAdjustmentStatus.NoCurrentLine,
                Error: "There is no writable current local lyric to edit.");

        var asset = LoadMatchingAsset(target.Document);
        if (asset is null) return Changed();
        if (target.LineIndex < 0 || target.LineIndex >= target.Document.TimestampOccurrences.Count)
            return new(TimingAdjustmentStatus.NoCurrentLine, Error: "The current lyric occurrence is unavailable.");

        var occurrence = target.Document.TimestampOccurrences[target.LineIndex];
        var editorDocument = EditorDocumentCodec.Load(asset.LrcContent, asset.LrcHash);
        if (!TryFindOccurrence(editorDocument, occurrence, out var rowId, out var timestampId))
            return Changed("The current lyric occurrence no longer matches the authoritative LRC.");

        var adjusted = _adjustments.ShiftOccurrence(editorDocument, rowId, timestampId, deltaMs);
        if (!adjusted.Succeeded || adjusted.Document is null)
            return FromRuleFailure(adjusted.Error);

        return SaveAndReload(asset, adjusted.Document, "current lyric occurrence");
    }

    public TimingAdjustmentResult ShiftAll(long deltaMs)
    {
        var current = _playback.CurrentLocalLyrics;
        if (!CanShiftAll || current is null || _playback.ActiveLocalLyricsRecord is not { } record)
            return new(TimingAdjustmentStatus.NoLocalLyrics,
                Error: "There is no writable timed local LRC to shift.");

        var assetResult = _library.LoadForEditing(record);
        if (assetResult.Status != EditorAssetStatus.Ready || assetResult.Asset is not { } asset)
        {
            if (assetResult.Status == EditorAssetStatus.Missing)
                return Changed("The authoritative local LRC is no longer available.");
            return new(TimingAdjustmentStatus.StorageFailure,
                Error: assetResult.Error ?? "The authoritative local LRC is unavailable.");
        }
        if (!MatchesCurrentAsset(current, asset)) return Changed();

        var document = EditorDocumentCodec.Load(asset.LrcContent, asset.LrcHash);
        var adjusted = _adjustments.ShiftAll(document, deltaMs);
        if (!adjusted.Succeeded || adjusted.Document is null)
            return FromRuleFailure(adjusted.Error);

        return SaveAndReload(asset, adjusted.Document, "all lyrics");
    }

    private EditorAssetSnapshot? LoadMatchingAsset(LocalLyricsDocument expected)
    {
        if (!MatchesActiveDocument(expected) || _playback.ActiveLocalLyricsRecord is not { } record)
            return null;
        var loaded = _library.LoadForEditing(record);
        if (loaded.Status != EditorAssetStatus.Ready || loaded.Asset is not { } asset ||
            !MatchesCurrentAsset(expected, asset))
            return null;
        return asset;
    }

    private bool MatchesActiveDocument(LocalLyricsDocument expected) =>
        _playback.IsCurrentLocalLrcUsable && _playback.CurrentLocalLyrics is { } current &&
        string.Equals(current.Record.LocalTrackId, expected.Record.LocalTrackId, StringComparison.Ordinal) &&
        string.Equals(current.Record.LyricsRelativePath, expected.Record.LyricsRelativePath,
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(current.LrcContentHash, expected.LrcContentHash, StringComparison.Ordinal);

    private bool MatchesCurrentAsset(LocalLyricsDocument expected, EditorAssetSnapshot asset) =>
        string.Equals(asset.Record.LocalTrackId, expected.Record.LocalTrackId, StringComparison.Ordinal) &&
        string.Equals(asset.Record.LyricsRelativePath, expected.Record.LyricsRelativePath,
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(asset.LrcHash, expected.LrcContentHash, StringComparison.Ordinal);

    private TimingAdjustmentResult SaveAndReload(EditorAssetSnapshot asset, EditorDocument adjusted,
        string description)
    {
        // Recheck playback identity immediately before the conditional, hash-checked save.
        if (_playback.ActiveLocalLyricsRecord?.LocalTrackId != asset.Record.LocalTrackId ||
            _playback.CurrentLocalLyrics is not { } current || !MatchesCurrentAsset(current, asset))
            return Changed();

        var content = EditorDocumentCodec.Serialize(adjusted);
        var saved = _library.SaveEditorAssets(asset, content, asset.Sidecar.UserMetadata);
        if (!saved.Succeeded || saved.Asset is not { } savedAsset)
        {
            var conflict = saved.Status is EditorAssetStatus.Conflict or EditorAssetStatus.Missing;
            _logger.LogWarning("Could not persist {Adjustment} timing edit for LocalTrackId={LocalTrackId}: {Error}",
                description, asset.Record.LocalTrackId, saved.Error ?? saved.Status.ToString());
            return new(conflict ? TimingAdjustmentStatus.FileChanged : TimingAdjustmentStatus.StorageFailure,
                Error: saved.Error ?? "The timing edit could not be saved.");
        }

        var reload = _playback.ReloadExternalLocalLyrics(asset.Record.LocalTrackId, savedAsset.LrcHash);
        if (reload is not (ExternalLocalLyricsUpdate.Reloaded or ExternalLocalLyricsUpdate.Unchanged))
            return Changed("The LRC was saved, but the active lyrics could not be refreshed safely.");

        _logger.LogInformation("Persisted {Adjustment} timing edit for LocalTrackId={LocalTrackId}.",
            description, asset.Record.LocalTrackId);
        return new(TimingAdjustmentStatus.Succeeded, _playback.CurrentLocalLyrics);
    }

    private static bool TryFindOccurrence(EditorDocument document, LrcTimestampOccurrence occurrence,
        out Guid rowId, out Guid timestampId)
    {
        rowId = default;
        timestampId = default;
        var absoluteOffset = 0;
        foreach (var line in document.PhysicalLines)
        {
            if (line.LyricRow is { } row)
            {
                var tokenOffset = absoluteOffset + row.LeadingWhitespace.Length;
                foreach (var timestamp in row.Timestamps)
                {
                    var tokenLength = timestamp.Value.Length + 2;
                    if (tokenOffset == occurrence.CharacterIndex && tokenLength == occurrence.Length &&
                        EditorDocumentCodec.TryParseTimestamp(timestamp.Value, out var startMs) &&
                        startMs == occurrence.StartMs)
                    {
                        rowId = row.Id;
                        timestampId = timestamp.Id;
                        return true;
                    }
                    tokenOffset += tokenLength;
                }
            }
            absoluteOffset += line.RawText.Length + line.LineEnding.Length;
        }
        return false;
    }

    private static TimingAdjustmentResult FromRuleFailure(string? error)
    {
        var status = error switch
        {
            LrcTimestampRewriter.NegativeTimestampError => TimingAdjustmentStatus.NegativeTimestamp,
            LrcTimestampRewriter.TimestampOverflowError => TimingAdjustmentStatus.TimestampOverflow,
            var message when message?.StartsWith("Cannot move this lyric past the", StringComparison.Ordinal) == true =>
                TimingAdjustmentStatus.TimestampOrderViolation,
            _ => TimingAdjustmentStatus.NoCurrentLine
        };
        return new(status, Error: error ?? "The timestamp adjustment was rejected.");
    }

    private static TimingAdjustmentResult Changed(string? error = null) =>
        new(TimingAdjustmentStatus.FileChanged,
            Error: error ?? "The current local LRC changed before the timing edit could be saved.");

    private void OnAvailabilityChanged() => AvailabilityChanged?.Invoke();
}
