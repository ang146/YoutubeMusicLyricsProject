using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Infrastructure.Errors;
using Microsoft.Extensions.Logging;

namespace LyricsDisplayer.Infrastructure.Commands;

/// <summary>Application command with explicit invalidation and centralized logging/error handling.</summary>
public sealed class RelayCommand : IRelayCommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?> _canExecute;
    private readonly ILogger _logger;
    private readonly IExceptionHandler _exceptionHandler;

    public CommandIdentity Identity { get; }
    public event EventHandler? CanExecuteChanged;

    public RelayCommand(string name, Action<object?> execute, Predicate<object?>? canExecute,
        EditorHotkeyScope scope, ILogger logger, IExceptionHandler exceptionHandler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Identity = new(name, scope);
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute ?? (_ => true);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _exceptionHandler = exceptionHandler ?? throw new ArgumentNullException(nameof(exceptionHandler));
    }

    public bool CanExecute(object? parameter) => _canExecute(parameter);

    public void Execute(object? parameter)
    {
        _logger.LogDebug("Command {CommandName} started.", Identity.Name);
        try
        {
            _execute(parameter);
            _logger.LogDebug("Command {CommandName} completed.", Identity.Name);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Command {CommandName} failed.", Identity.Name);
            if (_exceptionHandler.Handle(exception, Identity.Name) == ExceptionHandlingDisposition.Escalate)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>Non-reentrant asynchronous application command.</summary>
public sealed class AsyncRelayCommand : IAsyncRelayCommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool> _canExecute;
    private readonly ILogger _logger;
    private readonly IExceptionHandler _exceptionHandler;
    private int _isExecuting;

    public CommandIdentity Identity { get; }
    public bool IsExecuting => Volatile.Read(ref _isExecuting) != 0;
    public event EventHandler? CanExecuteChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public AsyncRelayCommand(string name, Func<Task> execute, Func<bool>? canExecute,
        EditorHotkeyScope scope, ILogger logger, IExceptionHandler exceptionHandler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Identity = new(name, scope);
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute ?? (() => true);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _exceptionHandler = exceptionHandler ?? throw new ArgumentNullException(nameof(exceptionHandler));
    }

    public bool CanExecute(object? parameter) => !IsExecuting && _canExecute();

    // ICommand requires a void boundary; all work and exception handling remain awaitable in ExecuteAsync.
    public async void Execute(object? parameter) => await ExecuteAsync(parameter);

    public async Task ExecuteAsync(object? parameter = null)
    {
        if (!CanExecute(parameter) || Interlocked.CompareExchange(ref _isExecuting, 1, 0) != 0) return;
        OnPropertyChanged(nameof(IsExecuting));
        RaiseCanExecuteChanged();
        _logger.LogDebug("Command {CommandName} started.", Identity.Name);
        try
        {
            // Invocation itself is inside this try, so synchronous pre-await failures are handled too.
            await _execute();
            _logger.LogDebug("Command {CommandName} completed.", Identity.Name);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Command {CommandName} failed.", Identity.Name);
            if (_exceptionHandler.Handle(exception, Identity.Name) == ExceptionHandlingDisposition.Escalate)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
        }
        finally
        {
            Interlocked.Exchange(ref _isExecuting, 0);
            OnPropertyChanged(nameof(IsExecuting));
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new(propertyName));
}

public sealed class ApplicationCommandFactory(
    ILoggerFactory loggerFactory,
    IExceptionHandler exceptionHandler) : ICommandFactory
{
    public IRelayCommand Create(string name, Action<object?> execute,
        Predicate<object?>? canExecute = null, EditorHotkeyScope scope = EditorHotkeyScope.Editor) =>
        new RelayCommand(name, execute, canExecute, scope,
            loggerFactory.CreateLogger($"Command.{name}"), exceptionHandler);

    public IRelayCommand Create(string name, Action execute,
        Func<bool>? canExecute = null, EditorHotkeyScope scope = EditorHotkeyScope.Editor) =>
        Create(name, _ => execute(), canExecute is null ? null : _ => canExecute(), scope);

    public IAsyncRelayCommand CreateAsync(string name, Func<Task> execute,
        Func<bool>? canExecute = null, EditorHotkeyScope scope = EditorHotkeyScope.Editor) =>
        new AsyncRelayCommand(name, execute, canExecute, scope,
            loggerFactory.CreateLogger($"Command.{name}"), exceptionHandler);
}
