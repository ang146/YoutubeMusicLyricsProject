using System.Windows.Controls;
using System.Windows.Data;
using LyricsDisplayer.Core.Settings;
using LyricsDisplayer.Resources;
using MenuItem = System.Windows.Controls.MenuItem;
using Separator = System.Windows.Controls.Separator;
using Binding = System.Windows.Data.Binding;

namespace LyricsDisplayer.Controls;

public sealed class OverlayLyricsContextMenu : LyricsContextMenuBase
{
    private readonly MenuItem _oneLine;
    private readonly MenuItem _twoLines;
    private readonly MenuItem _allLyrics;
    private readonly MenuItem _locked;
    private readonly MenuItem _clickThrough;

    public OverlayLyricsContextMenu() : base(string.Empty)
    {
        Items.Insert(0, new Separator());
        var openLyricsWindow = new MenuItem { Header = Strings.OpenLyricsWindow };
        openLyricsWindow.SetBinding(MenuItem.CommandProperty,
            new Binding(nameof(IOverlayLyricsSurfaceActionsViewModel.OpenLyricsWindowCommand)));
        Items.Insert(0, openLyricsWindow);

        Items.Add(new Separator());
        var display = new MenuItem { Header = Strings.LyricsDisplay };
        _oneLine = AddCheckable(display, Strings.OneLine,
            nameof(IOverlayLyricsSurfaceActionsViewModel.SetOneLineCommand));
        _twoLines = AddCheckable(display, Strings.TwoLines,
            nameof(IOverlayLyricsSurfaceActionsViewModel.SetTwoLinesCommand));
        _allLyrics = AddCheckable(display, Strings.MenuAllLyrics,
            nameof(IOverlayLyricsSurfaceActionsViewModel.SetAllLyricsCommand));
        Items.Add(display);

        var overlay = new MenuItem { Header = Strings.Overlay };
        _locked = AddCheckable(overlay, Strings.LockPosition,
            nameof(IOverlayLyricsSurfaceActionsViewModel.ToggleLockedCommand));
        _clickThrough = AddCheckable(overlay, Strings.ClickThrough,
            nameof(IOverlayLyricsSurfaceActionsViewModel.ToggleClickThroughCommand));
        Items.Add(overlay);

        Items.Add(new Separator());
        var hide = new MenuItem { Header = Strings.HideDesktopLyrics };
        hide.SetBinding(MenuItem.CommandProperty,
            new Binding(nameof(IOverlayLyricsSurfaceActionsViewModel.HideCommand)));
        Items.Add(hide);
    }

    public void UpdateInteractionState(OverlayInteractionState state)
    {
        _oneLine.IsChecked = state.ContentMode == LyricsContentMode.OneLine;
        _twoLines.IsChecked = state.ContentMode == LyricsContentMode.TwoLines;
        _allLyrics.IsChecked = state.ContentMode == LyricsContentMode.AllLyrics;
        _locked.IsChecked = state.Locked;
        _clickThrough.IsChecked = state.ClickThrough;
    }

    private static MenuItem AddCheckable(MenuItem parent, string header, string commandPath)
    {
        var item = new MenuItem { Header = header, IsCheckable = true };
        item.SetBinding(MenuItem.CommandProperty, new Binding(commandPath));
        parent.Items.Add(item);
        return item;
    }
}
