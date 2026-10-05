using WpfApplication = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;

namespace LyricsDisplayer.Infrastructure.Errors;

public sealed class WpfRecoverableExceptionPresenter : IRecoverableExceptionPresenter
{
    public void ShowOperationFailure(string operationName, string message)
    {
        var dispatcher = WpfApplication.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            ShowDialog(operationName, message);
            return;
        }

        dispatcher.Invoke(() => ShowDialog(operationName, message));
    }

    private static void ShowDialog(string operationName, string message) =>
        WpfMessageBox.Show(WpfApplication.Current?.MainWindow,
            $"The operation '{operationName}' could not be completed.{Environment.NewLine}{Environment.NewLine}{message}",
            "Lyrics Displayer", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
}
