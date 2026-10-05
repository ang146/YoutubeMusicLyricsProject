using Microsoft.Extensions.Logging;

namespace LyricsDisplayer;

public abstract class ViewModelBase : ObservableObjectBase
{
    protected ILogger Logger { get; }

    protected ViewModelBase(ILogger logger) =>
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
}
