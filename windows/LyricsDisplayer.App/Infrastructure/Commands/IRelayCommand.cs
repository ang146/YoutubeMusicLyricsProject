using System.ComponentModel;
using System.Windows.Input;
using LyricsDisplayer.Core.Library;

namespace LyricsDisplayer.Infrastructure.Commands;

public sealed record CommandIdentity(string Name, EditorHotkeyScope Scope);

public interface IRelayCommand : ICommand
{
    CommandIdentity Identity { get; }
    void RaiseCanExecuteChanged();
}

public interface IAsyncRelayCommand : IRelayCommand, INotifyPropertyChanged
{
    bool IsExecuting { get; }
    Task ExecuteAsync(object? parameter = null);
}

public interface ICommandFactory
{
    IRelayCommand Create(string name, Action<object?> execute,
        Predicate<object?>? canExecute = null, EditorHotkeyScope scope = EditorHotkeyScope.Editor);

    IRelayCommand Create(string name, Action execute,
        Func<bool>? canExecute = null, EditorHotkeyScope scope = EditorHotkeyScope.Editor);

    IAsyncRelayCommand CreateAsync(string name, Func<Task> execute,
        Func<bool>? canExecute = null, EditorHotkeyScope scope = EditorHotkeyScope.Editor);
}
