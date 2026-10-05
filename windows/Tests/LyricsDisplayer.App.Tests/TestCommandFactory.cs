using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Infrastructure.Commands;
using LyricsDisplayer.Infrastructure.Errors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LyricsDisplayer.App.Tests;

/// <summary>Creates real commands with inert logging/error reporting for isolated ViewModel tests.</summary>
internal sealed class TestCommandFactory : ICommandFactory
{
    public static TestCommandFactory Instance { get; } = new();

    private static readonly IExceptionHandler ExceptionHandler = new TestExceptionHandler();
    private static readonly ILoggerFactory LoggerFactory = NullLoggerFactory.Instance;

    public IRelayCommand Create(string name, Action<object?> execute,
        Predicate<object?>? canExecute = null, EditorHotkeyScope scope = EditorHotkeyScope.Editor) =>
        new RelayCommand(name, execute, canExecute, scope,
            LoggerFactory.CreateLogger($"TestCommand.{name}"), ExceptionHandler);

    public IRelayCommand Create(string name, Action execute,
        Func<bool>? canExecute = null, EditorHotkeyScope scope = EditorHotkeyScope.Editor) =>
        Create(name, _ => execute(), canExecute is null ? null : _ => canExecute(), scope);

    public IAsyncRelayCommand CreateAsync(string name, Func<Task> execute,
        Func<bool>? canExecute = null, EditorHotkeyScope scope = EditorHotkeyScope.Editor) =>
        new AsyncRelayCommand(name, execute, canExecute, scope,
            LoggerFactory.CreateLogger($"TestCommand.{name}"), ExceptionHandler);

    private sealed class TestExceptionHandler : IExceptionHandler
    {
        public ExceptionHandlingDisposition Handle(Exception exception, string operationName) =>
            ExceptionHandlingDisposition.Handled;
    }
}
