using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Separator = System.Windows.Controls.Separator;
using Binding = System.Windows.Data.Binding;

namespace LyricsDisplayer.Controls;

/// <summary>Builds a fresh copy of the shared lyrics-surface menu for every WPF menu instance.</summary>
public abstract class LyricsContextMenuBase : ContextMenu
{
    protected LyricsContextMenuBase(string surfaceActionsPath)
    {
        var path = string.IsNullOrWhiteSpace(surfaceActionsPath) ? string.Empty : surfaceActionsPath + ".";
        var dataContextPath = string.IsNullOrWhiteSpace(surfaceActionsPath)
            ? "PlacementTarget.DataContext"
            : $"PlacementTarget.DataContext.{surfaceActionsPath}";
        SetBinding(DataContextProperty, new Binding(dataContextPath)
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.Self)
        });

        Items.Add(Action("OpenLrcExternally"));
        var external = (MenuItem)Items[^1];
        external.SetBinding(ToolTipProperty, new Binding(path + "ExternalLyricsStatus"));
        Items.Add(Action("OpenBuiltInEditor"));
        Items.Add(new Separator());
        Items.Add(TimingSubmenu("Current Line", path, "AdjustCurrentLine"));
        Items.Add(TimingSubmenu("All Lyrics", path, "ShiftAll"));
    }

    private MenuItem Action(string name)
    {
        var menuItem = new MenuItem();
        menuItem.SetBinding(MenuItem.HeaderProperty, new Binding($"{name}.Header"));
        menuItem.SetBinding(MenuItem.CommandProperty, new Binding($"{name}.Command"));
        return menuItem;
    }

    private static MenuItem TimingSubmenu(string header, string path, string prefix)
    {
        var submenu = new MenuItem { Header = header };
        submenu.Items.Add(TimingItem(path, prefix + "Minus500", "-0.5s"));
        submenu.Items.Add(TimingItem(path, prefix + "Minus100", "-0.1s"));
        submenu.Items.Add(TimingItem(path, prefix + "Plus100", "+0.1s"));
        submenu.Items.Add(TimingItem(path, prefix + "Plus500", "+0.5s"));
        return submenu;
    }

    private static MenuItem TimingItem(string path, string definition, string fallbackHeader)
    {
        var item = new MenuItem { Header = fallbackHeader };
        item.SetBinding(MenuItem.HeaderProperty, new Binding(path + definition + ".Header"));
        item.SetBinding(MenuItem.CommandProperty, new Binding(path + definition + ".Command"));
        return item;
    }
}
