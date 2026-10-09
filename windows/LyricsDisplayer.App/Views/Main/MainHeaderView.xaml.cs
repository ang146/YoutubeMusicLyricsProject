using System.Windows;
using UserControl = System.Windows.Controls.UserControl;

namespace LyricsDisplayer.Views.Main;

public partial class MainHeaderView : UserControl
{
    public event RoutedEventHandler? ShowDesktopLyricsRequested;

    public MainHeaderView() => InitializeComponent();

    private void OnShowDesktopLyricsClick(object sender, RoutedEventArgs e) =>
        ShowDesktopLyricsRequested?.Invoke(this, e);
}
