using System.ComponentModel;
using System.Windows.Input;

namespace LyricsDisplayer.Infrastructure.Commands;

public sealed record CommandIdentity(string Name, CommandScope Scope);

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
        Predicate<object?>? canExecute = null, CommandScope scope = CommandScope.Editor);

    IRelayCommand Create(string name, Action execute,
        Func<bool>? canExecute = null, CommandScope scope = CommandScope.Editor);

    IAsyncRelayCommand CreateAsync(string name, Func<Task> execute,
        Func<bool>? canExecute = null, CommandScope scope = CommandScope.Editor);
}
