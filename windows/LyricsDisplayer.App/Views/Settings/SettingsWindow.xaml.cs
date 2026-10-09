using System.Windows;

namespace LyricsDisplayer;

public partial class SettingsWindow : Window
{
    public SettingsWindow(ISettingsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }
}
