using System.IO;
using System.Windows;
using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Core.Logging;
using LyricsDisplayer.Core.Settings;

namespace LyricsDisplayer;

public partial class App : System.Windows.Application
{
    public SessionFileLogger Logger { get; private set; } = null!;
    public LyricsLibrary LyricsLibrary { get; private set; } = null!;
    public ApplicationSettingsStore SettingsStore { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Logger = new SessionFileLogger(Path.Combine(localAppData, "LyricsDisplayer", "Logs", "App"));
        Logger.Write("Information", "Application", "LyricsDisplayer.App started.");
        var applicationData = Path.Combine(localAppData, "LyricsDisplayer");
        SettingsStore = new ApplicationSettingsStore(Path.Combine(applicationData, "settings.json"));
        LyricsLibrary = new LyricsLibrary(LibraryPaths.Resolve(applicationData), Logger.Write);
        LyricsLibrary.Initialise();
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Logger.Write("Information", "Application", "LyricsDisplayer.App shut down.");
        LyricsLibrary.Dispose();
        Logger.Dispose();
        base.OnExit(e);
    }
}
