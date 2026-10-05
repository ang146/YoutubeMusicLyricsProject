using System.IO;
using System.Windows;
using System.Windows.Threading;
using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Core.Logging;
using LyricsDisplayer.Core.Settings;
using WpfMessageBox = System.Windows.MessageBox;

namespace LyricsDisplayer;

public partial class App : System.Windows.Application
{
    private readonly CrashReportService _crashReports;
    private readonly FatalExceptionCoordinator _fatalExceptionCoordinator;
    private readonly ApplicationLifetimeState _lifetimeState;

    static App()
    {
        System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);
    }

    public App()
    {
        _lifetimeState = new ApplicationLifetimeState();
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _crashReports = new CrashReportService(Path.Combine(localAppData, "LyricsDisplayer", "Logs", "Crash"));
        _fatalExceptionCoordinator = new FatalExceptionCoordinator(_crashReports, WriteLog, WriteFatalLog,
            () => { _lifetimeState.BeginFatalShutdown(); });
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    public SessionFileLogger Logger { get; private set; } = null!;
    public CrashReportService CrashReports => _crashReports;
    public LyricsLibrary LyricsLibrary { get; private set; } = null!;
    public ApplicationSettingsStore SettingsStore { get; private set; } = null!;
    public IApplicationLifetimeState Lifetime => _lifetimeState;

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
        if (!Lifetime.IsFatalShutdown) Logger?.Write("Information", "Application", "LyricsDisplayer.App shut down.");
        LyricsLibrary?.Dispose();
        Logger?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        try
        {
            _fatalExceptionCoordinator.HandleDispatcherFatal(e.Exception,
                "Application.DispatcherUnhandledException", ShowFatalErrorDialog, () => Shutdown(-1));
        }
        catch (Exception reportingFailure)
        {
            try
            {
                _crashReports.WriteMinimalFallback(e.Exception, "Application.DispatcherUnhandledException",
                    $"Fatal UI exception handler failed: {reportingFailure.GetType().FullName}: {reportingFailure.Message}");
            }
            catch (Exception) { }
        }
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception ??
            new Exception($"AppDomain.UnhandledException received a non-Exception object of type " +
                $"'{e.ExceptionObject?.GetType().FullName ?? "null"}'.");
        try
        {
            _fatalExceptionCoordinator.ReportFatal(exception, "AppDomain.CurrentDomain.UnhandledException",
                e.IsTerminating);
        }
        catch (Exception) { }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        try { _fatalExceptionCoordinator.ReportUnobservedTaskException(e.Exception); }
        finally
        {
            try { e.SetObserved(); }
            catch (Exception) { }
        }
    }

    private void ShowFatalErrorDialog(FatalExceptionHandlingResult result)
    {
        try
        {
            var writtenPath = result.WriteResult?.WrittenPath ?? result.ReentrantFallbackPath;
            var reportDetails = writtenPath is not null
                ? $"A crash report was saved to:{Environment.NewLine}{writtenPath}"
                : result.Report is { } report
                    ? $"A crash report could not be written. Attempted path:{Environment.NewLine}{report.Path}"
                    : $"A crash report could not be written. The crash-report directory is: {_crashReports.CrashDirectory}";
            WpfMessageBox.Show(
                $"Lyrics Displayer encountered an unexpected fatal error and must close.{Environment.NewLine}{Environment.NewLine}{reportDetails}",
                "Lyrics Displayer - Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception)
        {
            // Diagnostics are already written; a failed dialog must not delay fatal shutdown.
        }
    }

    private void WriteLog(string level, string category, string message)
    {
        var logger = Logger;
        if (logger is null) throw new InvalidOperationException("The application logger is not initialized.");
        logger.Write(level, category, message);
    }

    private void WriteFatalLog(string level, string category, string message)
    {
        var logger = Logger;
        if (logger is null) throw new InvalidOperationException("The application logger is not initialized.");
        logger.WriteMultiline(level, category, message);
    }
}
