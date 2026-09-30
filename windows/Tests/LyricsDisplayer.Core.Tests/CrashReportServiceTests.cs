using System.Text;
using LyricsDisplayer.Core.Logging;

namespace LyricsDisplayer.Core.Tests;

[TestFixture]
public sealed class CrashReportServiceTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "LyricsDisplayerCrashTests", Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Test]
    public void ReportIncludesRuntimeContextExceptionDetailsAndCreatesUniqueFiles()
    {
        var writer = new TestWriter();
        var service = CreateService(writer);
        var exception = CaptureNestedException();
        exception.Data["operation"] = 42;
        exception.Data["token"] = "must not be copied";
        var first = service.Prepare(exception, "test fatal source", isTerminating: false);
        var second = service.Prepare(new InvalidOperationException("second incident"), "second source");

        Assert.That(Directory.Exists(_directory), Is.False, "The crash folder is created lazily when a report is written.");
        var firstWrite = service.Write(first);
        var secondWrite = service.Write(second);
        var content = File.ReadAllText(firstWrite.WrittenPath!, Encoding.UTF8);

        Assert.Multiple(() =>
        {
            Assert.That(firstWrite.Succeeded, Is.True);
            Assert.That(secondWrite.Succeeded, Is.True);
            Assert.That(firstWrite.WrittenPath, Does.StartWith(_directory));
            Assert.That(Path.GetFileName(firstWrite.WrittenPath), Does.StartWith("crash-"));
            Assert.That(firstWrite.WrittenPath, Is.Not.EqualTo(secondWrite.WrittenPath));
            Assert.That(content, Does.Contain(first.CrashReportId));
            Assert.That(content, Does.Contain("test fatal source"));
            Assert.That(content, Does.Contain("Application version: test-version"));
            Assert.That(content, Does.Contain($"Process ID: {Environment.ProcessId}"));
            Assert.That(content, Does.Contain("Process architecture:"));
            Assert.That(content, Does.Contain(".NET runtime:"));
            Assert.That(content, Does.Contain("OS:"));
            Assert.That(content, Does.Contain("Is terminating: False"));
            Assert.That(content, Does.Contain(typeof(InvalidOperationException).FullName));
            Assert.That(content, Does.Contain("Outer failure"));
            Assert.That(content, Does.Contain(typeof(ArgumentException).FullName));
            Assert.That(content, Does.Contain("Inner failure"));
            Assert.That(content, Does.Contain("HResult: 0x"));
            Assert.That(content, Does.Contain("Target site:"));
            Assert.That(content, Does.Contain("Stack trace:"));
            Assert.That(content, Does.Contain("CaptureNestedException"));
            Assert.That(content, Does.Contain("operation: <System.Int32>"));
            Assert.That(content, Does.Not.Contain("must not be copied"));
        });
    }

    [Test]
    public void AggregateExceptionPreservesAllNestedExceptions()
    {
        var aggregate = new AggregateException("Aggregate failure",
            new InvalidOperationException("first nested failure", new ArgumentException("deep failure")),
            new IOException("second nested failure"));

        var details = CrashReportService.FormatExceptionDetails(aggregate);

        Assert.Multiple(() =>
        {
            Assert.That(details, Does.Contain("Aggregate failure"));
            Assert.That(details, Does.Contain("first nested failure"));
            Assert.That(details, Does.Contain("deep failure"));
            Assert.That(details, Does.Contain("second nested failure"));
            Assert.That(details, Does.Contain(typeof(IOException).FullName));
            Assert.That(details, Does.Contain("Inner exception [1]"));
            Assert.That(details, Does.Contain("Inner exception [2]"));
        });
    }

    [Test]
    public void PrimaryReportFailureAttemptsFallbackAndReturnsWrittenFallbackPath()
    {
        var writer = new TestWriter(path => Path.GetFileName(path).StartsWith("crash-", StringComparison.Ordinal)
            ? new IOException("primary writer failed") : null);
        var service = CreateService(writer);
        var report = service.Prepare(new InvalidOperationException("Original failure"), "test source");

        var result = service.Write(report);

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.IsFallback, Is.True);
            Assert.That(result.PrimaryWriteFailure?.Message, Is.EqualTo("primary writer failed"));
            Assert.That(Path.GetFileName(result.WrittenPath), Does.StartWith("fatal-fallback-"));
            Assert.That(File.ReadAllText(result.WrittenPath!), Does.Contain("Original failure"));
        });
    }

    [Test]
    public void FatalCoordinatorLogsDetailsAndStillWritesWhenLoggerFails()
    {
        var service = CreateService();
        var exception = CaptureNestedException();
        var coordinator = new FatalExceptionCoordinator(service,
            (_, _, _) => throw new IOException("logger unavailable"));

        var result = coordinator.ReportFatal(exception, "test fatal source", isTerminating: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.WriteResult?.Succeeded, Is.True);
            Assert.That(File.ReadAllText(result.WriteResult!.WrittenPath!), Does.Contain("logger unavailable"));
            Assert.That(File.ReadAllText(result.WriteResult.WrittenPath!), Does.Contain("Outer failure"));
            Assert.That(result.WriteResult.WrittenPath, Does.Contain("crash-"));
        });
    }

    [Test]
    public void FatalLogContainsExceptionStackInnerDetailsAndReportLocation()
    {
        var service = CreateService();
        var logEntries = new List<(string Level, string Category, string Message)>();
        var coordinator = new FatalExceptionCoordinator(service,
            (level, category, message) => logEntries.Add((level, category, message)));

        var result = coordinator.ReportFatal(CaptureNestedException(), "dispatcher test source");
        var fatal = logEntries.Single();

        Assert.Multiple(() =>
        {
            Assert.That(fatal.Level, Is.EqualTo("Fatal"));
            Assert.That(fatal.Category, Is.EqualTo("Application"));
            Assert.That(fatal.Message, Does.Contain("Fatal unhandled exception"));
            Assert.That(fatal.Message, Does.Contain(result.Report!.CrashReportId));
            Assert.That(fatal.Message, Does.Contain("dispatcher test source"));
            Assert.That(fatal.Message, Does.Contain(typeof(InvalidOperationException).FullName));
            Assert.That(fatal.Message, Does.Contain("Outer failure"));
            Assert.That(fatal.Message, Does.Contain("Inner failure"));
            Assert.That(fatal.Message, Does.Contain("Stack trace:"));
            Assert.That(fatal.Message, Does.Contain(result.Report.Path));
        });
    }

    [Test]
    public void FatalLogUsesReadableMultilineOutputWithoutChangingOrdinaryLogEscaping()
    {
        var logDirectory = Path.Combine(_directory, "App");
        using var logger = new SessionFileLogger(logDirectory,
            new FixedClock(new DateTimeOffset(2026, 9, 30, 5, 38, 12, TimeSpan.FromHours(8))));
        logger.Write("Information", "Ordinary", "ordinary first line\r\nordinary second line");
        var coordinator = new FatalExceptionCoordinator(CreateService(), logger.Write, logger.WriteMultiline);

        coordinator.HandleDispatcherFatal(CaptureNestedException(), "dispatcher multiline test",
            _ => { }, () => { });

        var lines = File.ReadAllLines(logger.CurrentPath);
        var fatalStart = Array.FindIndex(lines, line => line.Contains("[Fatal] [Application]", StringComparison.Ordinal));
        var fatalText = string.Join(Environment.NewLine, lines.Skip(fatalStart));
        Assert.Multiple(() =>
        {
            Assert.That(lines[0], Does.Contain("ordinary first line\\r\\nordinary second line"));
            Assert.That(lines.Count(line => line.Contains("[Information] [Ordinary]", StringComparison.Ordinal)), Is.EqualTo(1));
            Assert.That(fatalStart, Is.GreaterThan(0));
            Assert.That(lines[fatalStart], Does.Contain("Fatal unhandled exception."));
            Assert.That(fatalText, Does.Contain(Environment.NewLine + "    Crash Report ID:"));
            Assert.That(fatalText, Does.Contain(Environment.NewLine + "    Exception:"));
            Assert.That(fatalText.Split(Environment.NewLine)
                .Any(line => line.TrimStart().StartsWith("at ", StringComparison.Ordinal)), Is.True);
            Assert.That(fatalText, Does.Contain(Environment.NewLine + "    Crash report path:"));
            Assert.That(fatalText, Does.Not.Contain("\\r\\n"));
            Assert.That(fatalText, Does.Not.Contain("\\n"));
        });
    }

    [Test]
    public void RepeatedDispatcherFatalRequestsOneDialogOneReportAndOneShutdown()
    {
        var writer = new TestWriter();
        var coordinator = new FatalExceptionCoordinator(CreateService(writer), (_, _, _) => { });
        var exception = new InvalidOperationException("same fatal incident");
        var dialogRequests = 0;
        var shutdownRequests = 0;

        var primary = coordinator.HandleDispatcherFatal(exception, "dispatcher first entry",
            _ => dialogRequests++, () => shutdownRequests++);
        var duplicate = coordinator.HandleDispatcherFatal(exception, "dispatcher re-entry",
            _ => dialogRequests++, () => shutdownRequests++);

        Assert.Multiple(() =>
        {
            Assert.That(primary.IsPrimaryIncident, Is.True);
            Assert.That(duplicate.IsPrimaryIncident, Is.False);
            Assert.That(duplicate.WasAlreadyHandling, Is.True);
            Assert.That(writer.Paths.Count(path => Path.GetFileName(path).StartsWith("crash-", StringComparison.Ordinal)), Is.EqualTo(1));
            Assert.That(dialogRequests, Is.EqualTo(1));
            Assert.That(shutdownRequests, Is.EqualTo(1));
        });
    }

    [Test]
    public void SecondaryFatalDuringShutdownDoesNotNotifyWriteOrShutdownAgain()
    {
        var writer = new TestWriter();
        var coordinator = new FatalExceptionCoordinator(CreateService(writer), (_, _, _) => { });
        var dialogRequests = 0;
        var shutdownRequests = 0;

        coordinator.HandleDispatcherFatal(new InvalidOperationException("primary"), "dispatcher",
            _ => dialogRequests++, () =>
            {
                shutdownRequests++;
                coordinator.HandleDispatcherFatal(new ObjectDisposedException("secondary shutdown"), "shutdown",
                    _ => dialogRequests++, () => shutdownRequests++);
            });

        Assert.Multiple(() =>
        {
            Assert.That(writer.Paths.Count(path => Path.GetFileName(path).StartsWith("crash-", StringComparison.Ordinal)), Is.EqualTo(1));
            Assert.That(dialogRequests, Is.EqualTo(1));
            Assert.That(shutdownRequests, Is.EqualTo(1));
        });
    }

    [Test]
    public void AppDomainFatalAfterDispatcherDoesNotRequestAnotherDialogOrShutdown()
    {
        var writer = new TestWriter();
        var coordinator = new FatalExceptionCoordinator(CreateService(writer), (_, _, _) => { });
        var dialogRequests = 0;
        var shutdownRequests = 0;

        coordinator.HandleDispatcherFatal(new InvalidOperationException("primary"), "dispatcher",
            _ => dialogRequests++, () => shutdownRequests++);
        var appDomainResult = coordinator.ReportFatal(new InvalidOperationException("secondary callback"),
            "AppDomain.CurrentDomain.UnhandledException", isTerminating: true);

        Assert.Multiple(() =>
        {
            Assert.That(appDomainResult.IsPrimaryIncident, Is.False);
            Assert.That(writer.Paths.Count(path => Path.GetFileName(path).StartsWith("crash-", StringComparison.Ordinal)), Is.EqualTo(1));
            Assert.That(dialogRequests, Is.EqualTo(1));
            Assert.That(shutdownRequests, Is.EqualTo(1));
        });
    }

    [Test]
    public void DialogFailureStillRequestsShutdownOnceAndDoesNotRetryDialog()
    {
        var writer = new TestWriter();
        var coordinator = new FatalExceptionCoordinator(CreateService(writer), (_, _, _) => { });
        var dialogRequests = 0;
        var shutdownRequests = 0;

        coordinator.HandleDispatcherFatal(new InvalidOperationException("dialog failure"), "dispatcher",
            _ => { dialogRequests++; throw new InvalidOperationException("UI unavailable"); },
            () => shutdownRequests++);
        coordinator.HandleDispatcherFatal(new InvalidOperationException("same incident"), "dispatcher re-entry",
            _ => dialogRequests++, () => shutdownRequests++);

        Assert.Multiple(() =>
        {
            Assert.That(dialogRequests, Is.EqualTo(1));
            Assert.That(shutdownRequests, Is.EqualTo(1));
            Assert.That(writer.Paths.Count(path => Path.GetFileName(path).StartsWith("crash-", StringComparison.Ordinal)), Is.EqualTo(1));
        });
    }

    [Test]
    public void ReentrantFatalDoesNotRecursivelyProcessOrDuplicateFullReport()
    {
        var writer = new TestWriter();
        var service = CreateService(writer);
        var exception = new InvalidOperationException("reentrant failure");
        FatalExceptionCoordinator? coordinator = null;
        var triggered = 0;
        coordinator = new FatalExceptionCoordinator(service, (_, _, _) =>
        {
            if (Interlocked.Exchange(ref triggered, 1) == 0)
                coordinator!.ReportFatal(exception, "nested handler");
        });

        var first = coordinator.ReportFatal(exception, "first handler");
        var writesAfterFirst = writer.Paths.Count;
        var duplicate = coordinator.ReportFatal(exception, "duplicate handler");

        Assert.Multiple(() =>
        {
            Assert.That(first.WriteResult?.Succeeded, Is.True);
            Assert.That(writer.Paths.Count, Is.EqualTo(2), "One full report and one minimal concurrent fallback are expected.");
            Assert.That(writer.Paths.Count(path => Path.GetFileName(path).StartsWith("crash-", StringComparison.Ordinal)), Is.EqualTo(1));
            Assert.That(duplicate.WasAlreadyHandling, Is.True);
            Assert.That(duplicate.Report, Is.Null);
            Assert.That(writer.Paths.Count, Is.EqualTo(writesAfterFirst), "A later duplicate after completion is suppressed.");
        });
    }

    [Test]
    public void UnobservedTaskExceptionIsLoggedAsErrorWithoutCrashReport()
    {
        var writer = new TestWriter();
        var service = CreateService(writer);
        var logEntries = new List<(string Level, string Category, string Message)>();
        var coordinator = new FatalExceptionCoordinator(service,
            (level, category, message) => logEntries.Add((level, category, message)));

        coordinator.ReportUnobservedTaskException(new InvalidOperationException("task failed"));

        Assert.Multiple(() =>
        {
            Assert.That(logEntries.Single().Level, Is.EqualTo("Error"));
            Assert.That(logEntries.Single().Category, Is.EqualTo("TaskScheduler"));
            Assert.That(logEntries.Single().Message, Does.Contain("not classified as fatal"));
            Assert.That(logEntries.Single().Message, Does.Contain("task failed"));
            Assert.That(writer.Paths, Is.Empty);
        });
    }

    [Test]
    public void TotalWriterFailureDoesNotEscapeFatalCoordinator()
    {
        var writer = new TestWriter(_ => new IOException("disk unavailable"));
        var logs = new List<string>();
        var coordinator = new FatalExceptionCoordinator(CreateService(writer),
            (_, _, message) => logs.Add(message));

        var result = coordinator.ReportFatal(new InvalidOperationException("must still terminate"), "test source");

        Assert.Multiple(() =>
        {
            Assert.That(result.Report, Is.Not.Null);
            Assert.That(result.WriteResult?.Succeeded, Is.False);
            Assert.That(result.WriteResult?.PrimaryWriteFailure, Is.TypeOf<IOException>());
            Assert.That(result.WriteResult?.FallbackWriteFailure, Is.TypeOf<IOException>());
            Assert.That(result.WriteResult?.TemporaryFallbackWriteFailure, Is.TypeOf<IOException>());
            Assert.That(logs, Has.Some.Contains("Crash report path"));
        });
    }

    private CrashReportService CreateService(ICrashReportFileWriter? writer = null) =>
        new(_directory, new FixedClock(new DateTimeOffset(2026, 9, 30, 5, 38, 12, TimeSpan.FromHours(8))),
            writer, "test-version");

    private static Exception CaptureNestedException()
    {
        try
        {
            try { throw new ArgumentException("Inner failure"); }
            catch (Exception inner) { throw new InvalidOperationException("Outer failure", inner); }
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; } = now;
    }

    private sealed class TestWriter(Func<string, Exception?>? failure = null) : ICrashReportFileWriter
    {
        public List<string> Paths { get; } = [];

        public void WriteNew(string path, string content)
        {
            Paths.Add(path);
            if (failure?.Invoke(path) is { } exception) throw exception;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }
}
