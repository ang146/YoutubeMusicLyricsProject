using System.Windows.Input;
using LyricsDisplayer.Core.Library;

namespace LyricsDisplayer;

public sealed record EditorCommandIdentity(string Id, EditorHotkeyScope Scope);

public sealed class EditorCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?> _canExecute;

    public EditorCommandIdentity Identity { get; }
    public event EventHandler? CanExecuteChanged;

    public EditorCommand(string id, Action<object?> execute, Predicate<object?>? canExecute = null,
        EditorHotkeyScope scope = EditorHotkeyScope.Editor)
    {
        Identity = new(id, scope);
        _execute = execute;
        _canExecute = canExecute ?? (_ => true);
    }

    public bool CanExecute(object? parameter) => _canExecute(parameter);
    public void Execute(object? parameter) => _execute(parameter);
    public void Invalidate() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public enum EditorSaveAction
{
    Save,
    OverwriteExternalChanges,
    ReloadExternalVersion
}
