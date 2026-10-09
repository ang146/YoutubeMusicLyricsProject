using System.Windows;
using System.Windows.Threading;
using LyricsDisplayer.Core.Logging;
using LyricsDisplayer.Infrastructure.DependencyInjection;
using LyricsDisplayer.Infrastructure.Errors;
using LyricsDisplayer.Resources;
using Microsoft.Extensions.Logging;
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
        _compositionRoot.AppLogger.LogInformation("LyricsDisplayer.App started.");
        base.OnStartup(e);
        MainWindow = _compositionRoot.CreateMainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_compositionRoot.IsInitialized && !_compositionRoot.Lifetime.IsFatalShutdown)
            _compositionRoot.AppLogger.LogInformation("LyricsDisplayer.App shut down.");
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
                ? $"{Strings.FatalCrashReportSavedTo}{Environment.NewLine}{writtenPath}"
                : result.Report is { } report
                    ? $"{Strings.FatalCrashReportCouldNotWriteAttemptedPath}{Environment.NewLine}{report.Path}"
                    : string.Format(Strings.FatalCrashReportCouldNotWriteDirectory,
                        _compositionRoot.CrashReports.CrashDirectory);
            WpfMessageBox.Show(
                string.Format(Strings.FatalErrorMessage, Environment.NewLine, reportDetails),
                Strings.FatalErrorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception)
        {
            // Diagnostics are already written; a failed dialog must not delay fatal shutdown.
        }
}
}
