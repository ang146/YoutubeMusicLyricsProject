using System.IO;
using System.Windows;
using LyricsDisplayer.Core.Logging;

namespace LyricsDisplayer;

public partial class App : Application
{
    public SessionFileLogger Logger { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Logger = new SessionFileLogger(Path.Combine(localAppData, "LyricsDisplayer", "Logs", "App"));
        Logger.Write("Information", "Application", "LyricsDisplayer.App started.");
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Logger.Write("Information", "Application", "LyricsDisplayer.App shut down.");
        Logger.Dispose();
        base.OnExit(e);
    }
}
