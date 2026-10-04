using System.Runtime.CompilerServices;

namespace LyricsDisplayer;

public abstract class EditableViewModelBase : ViewModelBase
{
    private bool _isDirty;

    public bool IsDirty => _isDirty;

    protected void MarkDirty() => SetProperty(ref _isDirty, true, nameof(IsDirty));

    protected void MarkClean() => SetProperty(ref _isDirty, false, nameof(IsDirty));

    protected bool SetEditableProperty<T>(ref T field, T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName)) return false;

        MarkDirty();
        return true;
    }
}
