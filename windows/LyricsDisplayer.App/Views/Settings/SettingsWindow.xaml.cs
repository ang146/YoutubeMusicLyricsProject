using System.Windows;

namespace LyricsDisplayer;

public partial class SettingsWindow : Window
{
    public SettingsWindow() : this(new SettingsViewModel()) { }

    public SettingsWindow(SettingsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }
}
