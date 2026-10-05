using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LyricsDisplayer;

/// <summary>Property notification mechanics for ViewModels and lightweight observable item objects.</summary>
public abstract class ObservableObjectBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

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
