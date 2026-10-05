using LyricsDisplayer.Infrastructure.DependencyInjection;

namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class ApplicationCompositionRootTests
{
    [Test]
    public void ProductionRegistrationsResolveTheNonVisualApplicationGraph()
    {
        var applicationData = Path.Combine(Path.GetTempPath(), $"LyricsDisplayer.CompositionTests.{Guid.NewGuid():N}");
        try
        {
            using var compositionRoot = new ApplicationCompositionRoot(applicationData);
            Assert.DoesNotThrow(compositionRoot.VerifyServiceGraph);
            var logPath = Path.Combine(applicationData, "Logs", "App", "current.logs");
            var output = File.ReadAllText(logPath);
            var category = $"{typeof(ApplicationCompositionRoot).FullName}.UnlistedLoggerCategory";
            Assert.That(output, Does.Contain($"[{category}]"));
            Assert.That(output, Does.Contain("Open-generic typed logger composition check."));
        }
        finally
        {
            if (Directory.Exists(applicationData)) Directory.Delete(applicationData, recursive: true);
        }
    }
}
