using System.Windows.Controls;
using System.Windows.Data;
using LyricsDisplayer.Resources;
using MenuItem = System.Windows.Controls.MenuItem;
using Separator = System.Windows.Controls.Separator;
using Binding = System.Windows.Data.Binding;

namespace LyricsDisplayer.Controls;

public sealed class MainLyricsContextMenu : LyricsContextMenuBase
{
    public MainLyricsContextMenu() : base(nameof(IMainLyricsViewModel.SurfaceActions))
    {
        Items.Add(new Separator());
        var settings = new MenuItem { Header = Strings.Settings };
        settings.SetBinding(MenuItem.CommandProperty,
            new Binding(nameof(IMainLyricsSurfaceActionsViewModel.OpenSettingsCommand)));
        Items.Add(settings);
    }
}
