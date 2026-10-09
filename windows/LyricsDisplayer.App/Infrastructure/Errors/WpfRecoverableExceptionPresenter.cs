using WpfApplication = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;
using LyricsDisplayer.Resources;

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
            string.Format(Strings.RecoverableOperationFailure, operationName, Environment.NewLine, message),
            Strings.AppName, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
}
