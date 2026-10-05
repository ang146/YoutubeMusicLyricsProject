using LyricsDisplayer.Core.Logging;
using Microsoft.Extensions.Logging;

namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class SessionFileLoggerProviderTests
{
    [Test]
    public void PreservesMicrosoftLoggerCategoryInExistingSessionLog()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"LyricsDisplayer.LoggerTests.{Guid.NewGuid():N}");
        try
        {
            using var fileLogger = new SessionFileLogger(directory);
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Debug);
                builder.AddProvider(new SessionFileLoggerProvider(fileLogger));
            });
            loggerFactory.CreateLogger<SampleCategory>().LogInformation("category-preservation-check");

            Assert.That(File.ReadAllText(fileLogger.CurrentPath),
                Does.Contain($"[{typeof(SampleCategory).FullName!.Replace('+', '.')}]"));
            Assert.That(File.ReadAllText(fileLogger.CurrentPath), Does.Contain("category-preservation-check"));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void ViewModelBaseLoggingPreservesConcreteTypedLoggerCategory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"LyricsDisplayer.LoggerTests.{Guid.NewGuid():N}");
        try
        {
            using var fileLogger = new SessionFileLogger(directory);
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Debug);
                builder.AddProvider(new SessionFileLoggerProvider(fileLogger));
            });
            new LoggedProbeViewModel(loggerFactory.CreateLogger<LoggedProbeViewModel>()).LogProbeEvent();

            var output = File.ReadAllText(fileLogger.CurrentPath);
            var category = typeof(LoggedProbeViewModel).FullName!.Replace('+', '.');
            Assert.That(output, Does.Contain($"[{category}]"));
            Assert.That(output, Does.Contain("view-model-base-category-check"));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class SampleCategory { }

    private sealed class LoggedProbeViewModel(ILogger<LoggedProbeViewModel> logger) : ViewModelBase(logger)
    {
        public void LogProbeEvent() => Logger.LogInformation("view-model-base-category-check");
    }
}
