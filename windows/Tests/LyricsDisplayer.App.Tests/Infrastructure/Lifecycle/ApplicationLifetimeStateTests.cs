using LyricsDisplayer.Core.Logging;

namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class ApplicationLifetimeStateTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp() =>
        _directory = Path.Combine(Path.GetTempPath(), "LyricsDisplayerLifetimeTests", Guid.NewGuid().ToString("N"));

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Test]
    public void LifetimeStartsRunningAndFatalReasonIsProcessLifetime()
    {
        var lifetime = new ApplicationLifetimeState();

        Assert.Multiple(() =>
        {
            Assert.That(lifetime.ShutdownReason, Is.EqualTo(ApplicationShutdownReason.None));
            Assert.That(lifetime.IsShuttingDown, Is.False);
            Assert.That(lifetime.IsFatalShutdown, Is.False);
        });

        Assert.That(lifetime.BeginFatalShutdown(), Is.True);
        Assert.That(lifetime.BeginFatalShutdown(), Is.False);

        Assert.Multiple(() =>
        {
            Assert.That(lifetime.ShutdownReason, Is.EqualTo(ApplicationShutdownReason.FatalException));
            Assert.That(lifetime.IsShuttingDown, Is.True);
            Assert.That(lifetime.IsFatalShutdown, Is.True);
        });
    }

    [Test]
    public void FatalEditorCloseSkipsCommitPromptAndSaveEntirely()
    {
        var lifetime = new ApplicationLifetimeState();
        lifetime.BeginFatalShutdown();
        var commitCalls = 0;
        var dirtyReads = 0;
        var promptCalls = 0;
        var saveCalls = 0;

        var canClose = EditorCloseGuard.CanClose(lifetime,
            () => commitCalls++,
            () => { dirtyReads++; return true; },
            () => { promptCalls++; return DirtyEditorCloseChoice.Cancel; },
            () => saveCalls++);

        Assert.Multiple(() =>
        {
            Assert.That(canClose, Is.True);
            Assert.That(commitCalls, Is.Zero);
            Assert.That(dirtyReads, Is.Zero);
            Assert.That(promptCalls, Is.Zero);
            Assert.That(saveCalls, Is.Zero);
        });
    }

    [Test]
    public void NormalDirtyCloseCancelStillPreventsCloseWithoutSaving()
    {
        var lifetime = new ApplicationLifetimeState();
        var dirty = true;
        var commitCalls = 0;
        var promptCalls = 0;
        var saveCalls = 0;

        var canClose = EditorCloseGuard.CanClose(lifetime,
            () => commitCalls++,
            () => dirty,
            () => { promptCalls++; return DirtyEditorCloseChoice.Cancel; },
            () => saveCalls++);

        Assert.Multiple(() =>
        {
            Assert.That(canClose, Is.False);
            Assert.That(lifetime.ShutdownReason, Is.EqualTo(ApplicationShutdownReason.None));
            Assert.That(commitCalls, Is.EqualTo(1));
            Assert.That(promptCalls, Is.EqualTo(1));
            Assert.That(saveCalls, Is.Zero);
            Assert.That(dirty, Is.True);
        });
    }

    [Test]
    public void NormalDirtyCloseSaveAndDiscardKeepTheirExistingChoices()
    {
        var saveLifetime = new ApplicationLifetimeState();
        var dirty = true;
        var saveCalls = 0;
        var savedCanClose = EditorCloseGuard.CanClose(saveLifetime, () => { }, () => dirty,
            () => DirtyEditorCloseChoice.Save,
            () => { saveCalls++; dirty = false; });

        var discardLifetime = new ApplicationLifetimeState();
        var discardSaveCalls = 0;
        var discardedCanClose = EditorCloseGuard.CanClose(discardLifetime, () => { }, () => true,
            () => DirtyEditorCloseChoice.Discard,
            () => discardSaveCalls++);

        Assert.Multiple(() =>
        {
            Assert.That(savedCanClose, Is.True);
            Assert.That(saveCalls, Is.EqualTo(1));
            Assert.That(discardedCanClose, Is.True);
            Assert.That(discardSaveCalls, Is.Zero);
        });
    }

    [Test]
    public void PrimaryFatalSetsSharedStateBeforeDiagnosticsDialogAndShutdown()
    {
        var lifetime = new ApplicationLifetimeState();
        var order = new List<string>();
        var coordinator = new FatalExceptionCoordinator(
            new CrashReportService(Path.Combine(_directory, "Crash"), applicationVersion: "test"),
            (level, _, _) =>
            {
                Assert.That(lifetime.IsFatalShutdown, Is.True);
                Assert.That(level, Is.EqualTo("Fatal"));
                order.Add("fatal-log");
            },
            onPrimaryFatalAccepted: () =>
            {
                lifetime.BeginFatalShutdown();
                order.Add("fatal-state");
            });

        var result = coordinator.HandleDispatcherFatal(new InvalidOperationException("fatal test"), "dispatcher",
            _ =>
            {
                Assert.That(lifetime.ShutdownReason, Is.EqualTo(ApplicationShutdownReason.FatalException));
                order.Add("dialog");
            },
            () =>
            {
                Assert.That(lifetime.IsFatalShutdown, Is.True);
                order.Add("shutdown");
            });
        var duplicate = coordinator.HandleDispatcherFatal(new ObjectDisposedException("secondary"), "secondary",
            _ => order.Add("duplicate-dialog"), () => order.Add("duplicate-shutdown"));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsPrimaryIncident, Is.True);
            Assert.That(duplicate.IsPrimaryIncident, Is.False);
            Assert.That(lifetime.IsFatalShutdown, Is.True);
            Assert.That(order, Is.EqualTo(new[] { "fatal-state", "fatal-log", "dialog", "shutdown" }));
        });
    }
}
