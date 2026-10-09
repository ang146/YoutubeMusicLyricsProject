using LyricsDisplayer.Core.Library;

namespace LyricsDisplayer;

public interface ICurrentLyricsTimingService
{
    bool CanAdjustCurrentLine { get; }
    bool CanShiftAll { get; }
    event Action? AvailabilityChanged;
    TimingAdjustmentResult AdjustCurrentLine(long deltaMs);
    TimingAdjustmentResult ShiftAll(long deltaMs);
}
