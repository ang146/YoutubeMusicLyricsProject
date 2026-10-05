using System.Windows;

namespace LyricsDisplayer;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        DataContext = new SettingsViewModel();
    }
}
