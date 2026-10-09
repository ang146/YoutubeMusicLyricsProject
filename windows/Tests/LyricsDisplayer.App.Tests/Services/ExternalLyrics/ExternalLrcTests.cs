using System.Security.Cryptography;

namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class ExternalLrcTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "LyricsDisplayerExternalLrcTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Test]
    public async Task WatcherReportsChangedContentAndDeduplicatesIdenticalContent()
    {
        var path = Path.Combine(_root, "track.lrc");
        var original = "[00:01.000]Original\n";
        File.WriteAllText(path, original);
        var observations = new List<ActiveLrcFileObservation>();
        var changed = new TaskCompletionSource<ActiveLrcFileObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var originalFingerprint = Fingerprint(path);
        using var watcher = new ActiveLrcFileWatcher(NullLogger<ActiveLrcFileWatcher>.Instance, path, originalFingerprint, observation =>
        {
            lock (observations) observations.Add(observation);
            changed.TrySetResult(observation);
        }, debounce: TimeSpan.FromMilliseconds(10), retryDelay: TimeSpan.FromMilliseconds(5));

        watcher.CheckNow();
        await Task.Delay(80);
        Assert.That(observations, Is.Empty);

        File.WriteAllText(path, "[00:02.000]Changed\n");
        watcher.CheckNow();
        var update = await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(update.Fingerprint, Is.EqualTo(Fingerprint(path)));

        watcher.CheckNow();
        await Task.Delay(80);
        Assert.That(observations, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task WatcherReportsMissingAndDetectsRestoredFile()
    {
        var path = Path.Combine(_root, "track.lrc");
        File.WriteAllText(path, "[00:01.000]Original\n");
        var observations = new List<ActiveLrcFileObservation>();
        var missingObservation = new TaskCompletionSource<ActiveLrcFileObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var restoredObservation = new TaskCompletionSource<ActiveLrcFileObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new ActiveLrcFileWatcher(NullLogger<ActiveLrcFileWatcher>.Instance, path, Fingerprint(path), observation =>
        {
            lock (observations) observations.Add(observation);
            if (observation.Kind == ActiveLrcFileObservationKind.Missing) missingObservation.TrySetResult(observation);
            if (observation.Kind == ActiveLrcFileObservationKind.Content) restoredObservation.TrySetResult(observation);
        }, debounce: TimeSpan.FromMilliseconds(10), retryDelay: TimeSpan.FromMilliseconds(5));

        File.Delete(path);
        watcher.CheckNow();
        var missing = await missingObservation.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(missing.Kind, Is.EqualTo(ActiveLrcFileObservationKind.Missing));

        File.WriteAllText(path, "[00:03.000]Restored\n");
        watcher.CheckNow();
        var restored = await restoredObservation.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(restored.Kind, Is.EqualTo(ActiveLrcFileObservationKind.Content));
        Assert.That(restored.Fingerprint, Is.EqualTo(Fingerprint(path)));
    }

    [Test]
    public async Task WatcherRetriesTransientLockAndToleratesAtomicReplacement()
    {
        var path = Path.Combine(_root, "track.lrc");
        File.WriteAllText(path, "[00:01.000]Original\n");
        var initialFingerprint = Fingerprint(path);
        var lockedContent = new TaskCompletionSource<ActiveLrcFileObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var replacementContent = new TaskCompletionSource<ActiveLrcFileObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var missing = new TaskCompletionSource<ActiveLrcFileObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var expectedLockedFingerprint = string.Empty;
        var expectedReplacementFingerprint = string.Empty;
        using var watcher = new ActiveLrcFileWatcher(NullLogger<ActiveLrcFileWatcher>.Instance, path, initialFingerprint, observation =>
        {
            if (observation.Kind == ActiveLrcFileObservationKind.Missing) missing.TrySetResult(observation);
            if (observation.Kind == ActiveLrcFileObservationKind.Content)
            {
                if (observation.Fingerprint == expectedLockedFingerprint) lockedContent.TrySetResult(observation);
                if (observation.Fingerprint == expectedReplacementFingerprint) replacementContent.TrySetResult(observation);
            }
        }, debounce: TimeSpan.FromMilliseconds(10), retryDelay: TimeSpan.FromMilliseconds(40));

        File.WriteAllText(path, "[00:02.000]Unlocked after retry\n");
        expectedLockedFingerprint = Fingerprint(path);
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            watcher.CheckNow();
            await Task.Delay(100);
        }
        var afterLock = await lockedContent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(afterLock.Kind, Is.EqualTo(ActiveLrcFileObservationKind.Content));

        File.Delete(path);
        watcher.CheckNow();
        await Task.Delay(60);
        var temporary = Path.Combine(_root, "track.tmp");
        File.WriteAllText(temporary, "[00:03.000]Atomic replacement\n");
        expectedReplacementFingerprint = Fingerprint(temporary);
        File.Move(temporary, path);

        var afterReplace = await replacementContent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(afterReplace.Fingerprint, Is.EqualTo(Fingerprint(path)));
        Assert.That(missing.Task.IsCompleted, Is.False,
            "A replacement that returns during bounded retries must not produce a permanent-missing observation.");
    }

    [Test]
    public void ExternalOpenerUsesInjectedLauncherAndHandlesMissingFilesSafely()
    {
        var path = Path.Combine(_root, "track.lrc");
        File.WriteAllText(path, "[00:01.000]Line\n");
        string? launched = null;
        var opener = new ExternalLrcOpener(NullLogger<ExternalLrcOpener>.Instance, value => launched = value);

        Assert.That(opener.TryOpen(path, out var error), Is.True);
        Assert.That(launched, Is.EqualTo(path));
        Assert.That(error, Is.Null);

        File.Delete(path);
        Assert.That(opener.TryOpen(path, out error), Is.False);
        Assert.That(error, Is.Not.Null);

        File.WriteAllText(path, "[00:01.000]Line\n");
        var failingOpener = new ExternalLrcOpener(NullLogger<ExternalLrcOpener>.Instance,
            _ => throw new InvalidOperationException("No associated application."));
        Assert.That(failingOpener.TryOpen(path, out error), Is.False);
        Assert.That(error, Does.Contain(nameof(InvalidOperationException)));
    }

    private static string Fingerprint(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
