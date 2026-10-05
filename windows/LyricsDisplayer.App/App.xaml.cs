using System.Windows;
using System.Windows.Threading;
using LyricsDisplayer.Core.Logging;
using LyricsDisplayer.Infrastructure.DependencyInjection;
using LyricsDisplayer.Infrastructure.Errors;
using WpfMessageBox = System.Windows.MessageBox;

namespace LyricsDisplayer;

public partial class App : System.Windows.Application
{
    private readonly ApplicationCompositionRoot _compositionRoot;

    static App()
    {
        System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);
    }

    public App()
    {
        _compositionRoot = new ApplicationCompositionRoot();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        _compositionRoot.Initialize();
        _compositionRoot.Logger.Write("Information", "Application", "LyricsDisplayer.App started.");
        base.OnStartup(e);
        MainWindow = _compositionRoot.CreateMainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_compositionRoot.IsInitialized && !_compositionRoot.Lifetime.IsFatalShutdown)
            _compositionRoot.Logger.Write("Information", "Application", "LyricsDisplayer.App shut down.");
        _compositionRoot.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        try
        {
            _compositionRoot.FatalExceptionCoordinator.HandleDispatcherFatal(e.Exception,
                "Application.DispatcherUnhandledException", ShowFatalErrorDialog, () => Shutdown(-1));
        }
        catch (Exception reportingFailure)
        {
            try
            {
                _compositionRoot.CrashReports.WriteMinimalFallback(e.Exception, "Application.DispatcherUnhandledException",
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
            _compositionRoot.FatalExceptionCoordinator.ReportFatal(exception, "AppDomain.CurrentDomain.UnhandledException",
                e.IsTerminating);
        }
        catch (Exception) { }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        try { _compositionRoot.FatalExceptionCoordinator.ReportUnobservedTaskException(e.Exception); }
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
                    : $"A crash report could not be written. The crash-report directory is: {_compositionRoot.CrashReports.CrashDirectory}";
            WpfMessageBox.Show(
                $"Lyrics Displayer encountered an unexpected fatal error and must close.{Environment.NewLine}{Environment.NewLine}{reportDetails}",
                "Lyrics Displayer - Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception)
        {
            // Diagnostics are already written; a failed dialog must not delay fatal shutdown.
        }
}
}
