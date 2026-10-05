using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LyricsDisplayer;

public abstract class ViewModelBase : INotifyPropertyChanged
{
    protected ILogger Logger { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    // Child presentation rows with no logging responsibility can use the null logger.
    protected ViewModelBase() : this(NullLogger.Instance) { }

    protected ViewModelBase(ILogger logger) =>
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));

    protected bool SetProperty<T>(ref T field, T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new(propertyName));
}
