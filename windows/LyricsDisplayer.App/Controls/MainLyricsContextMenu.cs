using System.Windows.Controls;
using System.Windows.Data;
using MenuItem = System.Windows.Controls.MenuItem;
using Separator = System.Windows.Controls.Separator;
using Binding = System.Windows.Data.Binding;

namespace LyricsDisplayer.Controls;

public sealed class MainLyricsContextMenu : LyricsContextMenuBase
{
    public MainLyricsContextMenu() : base("SurfaceActions")
    {
        Items.Add(new Separator());
        var settings = new MenuItem { Header = "Settings" };
        settings.SetBinding(MenuItem.CommandProperty, new Binding("OpenSettingsCommand"));
        Items.Add(settings);
    }
}
