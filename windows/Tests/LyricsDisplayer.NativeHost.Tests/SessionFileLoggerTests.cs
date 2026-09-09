using LyricsDisplayer.Core.Logging;

namespace LyricsDisplayer.NativeHost.Tests;

[TestFixture]
public sealed class SessionFileLoggerTests
{
    private string _directory = null!;
    private FakeClock _clock = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "LyricsDisplayerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _clock = new FakeClock(new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.FromHours(8)));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    [Test]
    public void StartupWithNoCurrentLogCreatesCurrentLog()
    {
        using var logger = new SessionFileLogger(_directory, _clock);
        Assert.That(File.Exists(CurrentPath), Is.True);
    }

    [Test]
    public void StartupWithEmptyCurrentLogReusesIt()
    {
        File.WriteAllText(CurrentPath, string.Empty);
        using var logger = new SessionFileLogger(_directory, _clock);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(CurrentPath), Is.True);
            Assert.That(Directory.GetFiles(_directory), Has.Length.EqualTo(1));
        });
    }

    [Test]
    public void StartupWithNonEmptyCurrentLogRotatesUsingItsEntryDate()
    {
        File.WriteAllText(CurrentPath, "2026-09-08T23:00:00.000+08:00 [Information] [Test] prior\n");
        using var logger = new SessionFileLogger(_directory, _clock);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(_directory, "2026-09-08.logs")), Is.True);
            Assert.That(File.Exists(CurrentPath), Is.True);
            Assert.That(new FileInfo(CurrentPath).Length, Is.Zero);
        });
    }

    [Test]
    public void DuplicateRotationNamesIncrementWithoutOverwrite()
    {
        File.WriteAllText(Path.Combine(_directory, "2026-09-08.logs"), "one");
        File.WriteAllText(Path.Combine(_directory, "2026-09-08-(1).logs"), "two");
        File.WriteAllText(CurrentPath, "2026-09-08T23:00:00.000+08:00 [Information] [Test] prior\n");

        using var logger = new SessionFileLogger(_directory, _clock);

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(Path.Combine(_directory, "2026-09-08.logs")), Is.EqualTo("one"));
            Assert.That(File.ReadAllText(Path.Combine(_directory, "2026-09-08-(1).logs")), Is.EqualTo("two"));
            Assert.That(File.Exists(Path.Combine(_directory, "2026-09-08-(2).logs")), Is.True);
        });
    }

    [Test]
    public void FirstDuplicateRotationUsesSuffixOne()
    {
        File.WriteAllText(Path.Combine(_directory, "2026-09-08.logs"), "existing");
        File.WriteAllText(CurrentPath, "2026-09-08T23:00:00.000+08:00 [Information] [Test] prior\n");

        using var logger = new SessionFileLogger(_directory, _clock);

        Assert.That(File.Exists(Path.Combine(_directory, "2026-09-08-(1).logs")), Is.True);
    }

    [Test]
    public void LocalDateChangeRotatesAndKeepsCurrentActive()
    {
        using var logger = new SessionFileLogger(_directory, _clock);
        logger.Write("Information", "Test", "before midnight");
        _clock.Now = new DateTimeOffset(2026, 9, 10, 0, 0, 1, TimeSpan.FromHours(8));

        logger.Write("Information", "Test", "after midnight");

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(Path.Combine(_directory, "2026-09-09.logs")), Does.Contain("before midnight"));
            Assert.That(File.ReadAllText(CurrentPath), Does.Contain("after midnight"));
            Assert.That(File.ReadAllText(CurrentPath), Does.Not.Contain("before midnight"));
        });
    }

    [Test]
    public void RetentionDeletesOnlyRecognisedRotatedLogsOlderThanThirtyDays()
    {
        var old = Path.Combine(_directory, "2026-08-09.logs");
        var oldDuplicate = Path.Combine(_directory, "2026-08-09-(2).logs");
        var boundary = Path.Combine(_directory, "2026-08-10.logs");
        var recent = Path.Combine(_directory, "2026-09-01.logs");
        var unrelated = Path.Combine(_directory, "notes.logs");
        var almostMatching = Path.Combine(_directory, "2026-08-09-backup.logs");
        foreach (var path in new[] { old, oldDuplicate, boundary, recent, unrelated, almostMatching })
        {
            File.WriteAllText(path, "data");
        }
        File.WriteAllText(CurrentPath, string.Empty);

        using var logger = new SessionFileLogger(_directory, _clock);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(old), Is.False);
            Assert.That(File.Exists(oldDuplicate), Is.False);
            Assert.That(File.Exists(boundary), Is.True);
            Assert.That(File.Exists(recent), Is.True);
            Assert.That(File.Exists(CurrentPath), Is.True);
            Assert.That(File.Exists(unrelated), Is.True);
            Assert.That(File.Exists(almostMatching), Is.True);
        });
    }

    [Test]
    public void LogEntriesUseIsoTimestampWithLocalOffsetAndSingleLines()
    {
        using var logger = new SessionFileLogger(_directory, _clock);
        logger.Write("Information", "Test", "line one\nline two");
        var content = File.ReadAllText(CurrentPath);
        Assert.Multiple(() =>
        {
            Assert.That(content, Does.StartWith("2026-09-09T12:00:00.000+08:00 [Information] [Test]"));
            Assert.That(content, Does.Contain("line one\\nline two"));
        });
    }

    private string CurrentPath => Path.Combine(_directory, "current.logs");

    private sealed class FakeClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;
    }
}
