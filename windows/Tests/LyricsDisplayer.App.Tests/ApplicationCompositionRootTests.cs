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
        }
        finally
        {
            if (Directory.Exists(applicationData)) Directory.Delete(applicationData, recursive: true);
        }
    }
}
