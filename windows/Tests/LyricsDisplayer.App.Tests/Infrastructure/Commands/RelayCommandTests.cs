using System.Collections.Concurrent;
using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Infrastructure.Commands;
using LyricsDisplayer.Infrastructure.Errors;
using Microsoft.Extensions.Logging.Abstractions;

namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class RelayCommandTests
{
    [Test]
    public void RelayCommandExecutesAndRaisesOnlyExplicitInvalidation()
    {
        var handler = new RecordingExceptionHandler();
        var executed = 0;
        var command = new RelayCommand("test.execute", _ => executed++, _ => true,
            CommandScope.Application, NullLogger.Instance, handler);
        var invalidations = 0;
        command.CanExecuteChanged += (_, _) => invalidations++;

        Assert.That(command.CanExecute(null), Is.True);
        command.Execute(null);

        Assert.That(executed, Is.EqualTo(1));
        Assert.That(invalidations, Is.Zero);
        Assert.That(handler.Calls, Is.Empty);
        command.RaiseCanExecuteChanged();
        Assert.That(invalidations, Is.EqualTo(1));
        Assert.That(command.Identity, Is.EqualTo(new CommandIdentity("test.execute", CommandScope.Application)));
    }

    [Test]
    public void RelayCommandRoutesRecoverableExceptionsAndEscalatesFatalFailures()
    {
        var handler = new RecordingExceptionHandler();
        var failure = new IOException("disk unavailable");
        var command = new RelayCommand("test.recoverable", _ => throw failure, null,
            CommandScope.Editor, NullLogger.Instance, handler);

        Assert.DoesNotThrow(() => command.Execute(null));
        Assert.That(handler.Calls, Is.EqualTo(new[] { (failure, "test.recoverable") }));

        var fatal = new FatalApplicationException("unsafe to continue");
        handler.Disposition = ExceptionHandlingDisposition.Escalate;
        var fatalCommand = new RelayCommand("test.fatal", _ => throw fatal, null,
            CommandScope.Editor, NullLogger.Instance, handler);

        Assert.Throws<FatalApplicationException>(() => fatalCommand.Execute(null));
    }

    [Test]
    public async Task AsyncRelayCommandIsNonReentrantAndRefreshesCanExecuteAroundExecution()
    {
        var handler = new RecordingExceptionHandler();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executions = 0;
        var command = new AsyncRelayCommand("test.async", async () =>
        {
            executions++;
            started.SetResult();
            await release.Task;
        }, null, CommandScope.Application, NullLogger.Instance, handler);
        var invalidations = 0;
        var executionStates = new ConcurrentQueue<bool>();
        command.CanExecuteChanged += (_, _) => invalidations++;
        command.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(command.IsExecuting)) executionStates.Enqueue(command.IsExecuting);
        };

        var pending = command.ExecuteAsync();
        await started.Task;
        Assert.Multiple(() =>
        {
            Assert.That(command.IsExecuting, Is.True);
            Assert.That(command.CanExecute(null), Is.False);
            Assert.That(invalidations, Is.EqualTo(1));
        });

        await command.ExecuteAsync();
        Assert.That(executions, Is.EqualTo(1));
        release.SetResult();
        await pending;

        Assert.Multiple(() =>
        {
            Assert.That(command.IsExecuting, Is.False);
            Assert.That(command.CanExecute(null), Is.True);
            Assert.That(invalidations, Is.EqualTo(2));
            Assert.That(executionStates.ToArray(), Is.EqualTo(new[] { true, false }));
            Assert.That(handler.Calls, Is.Empty);
        });
    }

    [Test]
    public async Task AsyncRelayCommandIsNonReentrantAcrossConcurrentCallers()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executions = 0;
        var command = new AsyncRelayCommand("test.async.concurrent", async () =>
        {
            Interlocked.Increment(ref executions);
            started.TrySetResult();
            await release.Task;
        }, null, CommandScope.Application, NullLogger.Instance, new RecordingExceptionHandler());

        var callers = Enumerable.Range(0, 2).Select(_ => Task.Run(() => command.ExecuteAsync())).ToArray();
        await started.Task;
        Assert.That(executions, Is.EqualTo(1));
        release.SetResult();
        await Task.WhenAll(callers);
        Assert.That(executions, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AsyncRelayCommandHandlesSynchronousAndAsynchronousExceptions(bool throwBeforeAwait)
    {
        var handler = new RecordingExceptionHandler();
        var failure = new InvalidOperationException(throwBeforeAwait ? "before await" : "after await");
        var command = new AsyncRelayCommand("test.async.failure", async () =>
        {
            if (throwBeforeAwait) throw failure;
            await Task.Yield();
            throw failure;
        }, null, CommandScope.Editor, NullLogger.Instance, handler);

        await command.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(handler.Calls, Is.EqualTo(new[] { (failure, "test.async.failure") }));
            Assert.That(command.IsExecuting, Is.False);
            Assert.That(command.CanExecute(null), Is.True);
        });
    }

    private sealed class RecordingExceptionHandler : IExceptionHandler
    {
        public List<(Exception Exception, string Operation)> Calls { get; } = [];
        public ExceptionHandlingDisposition Disposition { get; set; } = ExceptionHandlingDisposition.Handled;

        public ExceptionHandlingDisposition Handle(Exception exception, string operationName)
        {
            Calls.Add((exception, operationName));
            return Disposition;
        }
    }
}
