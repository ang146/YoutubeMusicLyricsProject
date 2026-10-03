using System.Threading;

namespace LyricsDisplayer;

public enum ApplicationShutdownReason
{
    None,
    FatalException
}

public interface IApplicationLifetimeState
{
    ApplicationShutdownReason ShutdownReason { get; }
    bool IsShuttingDown { get; }
    bool IsFatalShutdown { get; }
}

/// <summary>Thread-safe, process-lifetime shutdown state shared by application components.</summary>
public sealed class ApplicationLifetimeState : IApplicationLifetimeState
{
    private int _shutdownReason;

    public ApplicationShutdownReason ShutdownReason =>
        (ApplicationShutdownReason)Volatile.Read(ref _shutdownReason);

    public bool IsShuttingDown => ShutdownReason != ApplicationShutdownReason.None;

    public bool IsFatalShutdown => ShutdownReason == ApplicationShutdownReason.FatalException;

    /// <summary>Promotes the process to fatal shutdown once and never resets it.</summary>
    public bool BeginFatalShutdown()
    {
        while (true)
        {
            var current = Volatile.Read(ref _shutdownReason);
            if (current == (int)ApplicationShutdownReason.FatalException) return false;

            if (Interlocked.CompareExchange(ref _shutdownReason,
                    (int)ApplicationShutdownReason.FatalException, current) == current)
                return true;
        }
    }
}
