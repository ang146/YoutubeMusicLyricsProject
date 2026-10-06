using LyricsDisplayer.Infrastructure.Commands;

namespace LyricsDisplayer;

public sealed record LyricsSurfaceActionDefinition(string Header, IRelayCommand Command);

/// <summary>Commands and live state common to every lyrics-oriented application surface.</summary>
public interface ILyricsSurfaceActionsViewModel
{
    LyricsSurfaceActionDefinition OpenLrcExternally { get; }
    LyricsSurfaceActionDefinition OpenBuiltInEditor { get; }
    LyricsSurfaceActionDefinition AdjustCurrentLineMinus500 { get; }
    LyricsSurfaceActionDefinition AdjustCurrentLineMinus100 { get; }
    LyricsSurfaceActionDefinition AdjustCurrentLinePlus100 { get; }
    LyricsSurfaceActionDefinition AdjustCurrentLinePlus500 { get; }
    LyricsSurfaceActionDefinition ShiftAllMinus500 { get; }
    LyricsSurfaceActionDefinition ShiftAllMinus100 { get; }
    LyricsSurfaceActionDefinition ShiftAllPlus100 { get; }
    LyricsSurfaceActionDefinition ShiftAllPlus500 { get; }
    bool CanOpenExternalLyrics { get; }
    string ExternalLyricsStatus { get; }
    bool CanAdjustCurrentLine { get; }
    bool CanShiftAll { get; }
}
