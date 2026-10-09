using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using LyricsDisplayer.Resources;
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
        var dataContextPath = string.IsNullOrWhiteSpace(surfaceActionsPath)
            ? $"{nameof(ContextMenu.PlacementTarget)}.{nameof(FrameworkElement.DataContext)}"
            : $"{nameof(ContextMenu.PlacementTarget)}.{nameof(FrameworkElement.DataContext)}.{surfaceActionsPath}";
        SetBinding(DataContextProperty, new Binding(dataContextPath)
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.Self)
        });

        Items.Add(Action(nameof(ILyricsSurfaceActionsViewModel.OpenLrcExternally)));
        var external = (MenuItem)Items[^1];
        external.SetBinding(ToolTipProperty,
            new Binding(nameof(ILyricsSurfaceActionsViewModel.ExternalLyricsStatus)));
        Items.Add(Action(nameof(ILyricsSurfaceActionsViewModel.OpenBuiltInEditor)));
        Items.Add(new Separator());
        Items.Add(TimingSubmenu(Strings.MenuCurrentLine,
            nameof(ILyricsSurfaceActionsViewModel.AdjustCurrentLineMinus500),
            nameof(ILyricsSurfaceActionsViewModel.AdjustCurrentLineMinus100),
            nameof(ILyricsSurfaceActionsViewModel.AdjustCurrentLinePlus100),
            nameof(ILyricsSurfaceActionsViewModel.AdjustCurrentLinePlus500)));
        Items.Add(TimingSubmenu(Strings.MenuAllLyrics,
            nameof(ILyricsSurfaceActionsViewModel.ShiftAllMinus500),
            nameof(ILyricsSurfaceActionsViewModel.ShiftAllMinus100),
            nameof(ILyricsSurfaceActionsViewModel.ShiftAllPlus100),
            nameof(ILyricsSurfaceActionsViewModel.ShiftAllPlus500)));
    }

    private MenuItem Action(string name)
    {
        var menuItem = new MenuItem();
        menuItem.SetBinding(MenuItem.HeaderProperty,
            new Binding($"{name}.{nameof(LyricsSurfaceActionDefinition.Header)}"));
        menuItem.SetBinding(MenuItem.CommandProperty,
            new Binding($"{name}.{nameof(LyricsSurfaceActionDefinition.Command)}"));
        return menuItem;
    }

    private static MenuItem TimingSubmenu(string header, string minus500, string minus100,
        string plus100, string plus500)
    {
        var submenu = new MenuItem { Header = header };
        submenu.Items.Add(TimingItem(minus500, Strings.TimingMinus500Earlier));
        submenu.Items.Add(TimingItem(minus100, Strings.TimingMinus100Earlier));
        submenu.Items.Add(TimingItem(plus100, Strings.TimingPlus100Later));
        submenu.Items.Add(TimingItem(plus500, Strings.TimingPlus500Later));
        return submenu;
    }

    private static MenuItem TimingItem(string definition, string header)
    {
        var item = new MenuItem { Header = header };
        item.SetBinding(MenuItem.HeaderProperty,
            new Binding($"{definition}.{nameof(LyricsSurfaceActionDefinition.Header)}"));
        item.SetBinding(MenuItem.CommandProperty,
            new Binding($"{definition}.{nameof(LyricsSurfaceActionDefinition.Command)}"));
        return item;
    }
}
