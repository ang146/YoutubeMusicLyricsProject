using System.Windows.Controls;
using System.Windows.Data;
using LyricsDisplayer.Core.Settings;
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
        var openLyricsWindow = new MenuItem { Header = "Open Lyrics Window" };
        openLyricsWindow.SetBinding(MenuItem.CommandProperty, new Binding("OpenLyricsWindowCommand"));
        Items.Insert(0, openLyricsWindow);

        Items.Add(new Separator());
        var display = new MenuItem { Header = "Lyrics Display" };
        _oneLine = AddCheckable(display, "One Line", "SetOneLineCommand");
        _twoLines = AddCheckable(display, "Two Lines", "SetTwoLinesCommand");
        _allLyrics = AddCheckable(display, "All Lyrics", "SetAllLyricsCommand");
        Items.Add(display);

        var overlay = new MenuItem { Header = "Overlay" };
        _locked = AddCheckable(overlay, "Lock Position", "ToggleLockedCommand");
        _clickThrough = AddCheckable(overlay, "Click Through", "ToggleClickThroughCommand");
        Items.Add(overlay);

        Items.Add(new Separator());
        var hide = new MenuItem { Header = "Hide Desktop Lyrics" };
        hide.SetBinding(MenuItem.CommandProperty, new Binding("HideCommand"));
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
