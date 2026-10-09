using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using LyricsDisplayer.Controls;

namespace LyricsDisplayer.App.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class LyricsContextMenuTests
{
    [Test]
    public void MainMenuHasSharedGroupedActionsAndSettingsWithoutReset()
    {
        var menu = new MainLyricsContextMenu();
        var items = menu.Items.OfType<MenuItem>().ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(menu, Is.InstanceOf<LyricsContextMenuBase>());
            Assert.That(items.Select(item => item.Header?.ToString()), Does.Contain("Current Line"));
            Assert.That(items.Select(item => item.Header?.ToString()), Does.Contain("All Lyrics"));
            Assert.That(items.Select(item => item.Header?.ToString()), Does.Contain("Settings"));
            Assert.That(items.Single(item => Equals(item.Header, "Current Line")).Items.OfType<MenuItem>().Count(),
                Is.EqualTo(4));
            Assert.That(items.Single(item => Equals(item.Header, "All Lyrics")).Items.OfType<MenuItem>().Count(),
                Is.EqualTo(4));
            Assert.That(AllItems(menu).Any(item => item.Header?.ToString()?.Contains("Reset",
                StringComparison.OrdinalIgnoreCase) == true), Is.False);
        });
    }

    [Test]
    public void MainMenuBindingsAreRelativeToResolvedSurfaceActionsDataContext()
    {
        var menu = new MainLyricsContextMenu();
        var topLevelItems = menu.Items.OfType<MenuItem>().ToArray();
        var currentLine = topLevelItems.Single(item => Equals(item.Header, "Current Line"));
        var allLyrics = topLevelItems.Single(item => Equals(item.Header, "All Lyrics"));
        var currentLineAction = currentLine.Items.OfType<MenuItem>().First();
        var allLyricsAction = allLyrics.Items.OfType<MenuItem>().ElementAt(2);
        var settings = topLevelItems.Single(item => Equals(item.Header, "Settings"));
        var externalLyrics = topLevelItems.First();

        Assert.Multiple(() =>
        {
            Assert.That(BindingPath(currentLineAction, MenuItem.HeaderProperty),
                Is.EqualTo("AdjustCurrentLineMinus500.Header"));
            Assert.That(BindingPath(currentLineAction, MenuItem.CommandProperty),
                Is.EqualTo("AdjustCurrentLineMinus500.Command"));
            Assert.That(BindingPath(allLyricsAction, MenuItem.HeaderProperty),
                Is.EqualTo("ShiftAllPlus100.Header"));
            Assert.That(BindingPath(allLyricsAction, MenuItem.CommandProperty),
                Is.EqualTo("ShiftAllPlus100.Command"));
            Assert.That(BindingPath(settings, MenuItem.CommandProperty), Is.EqualTo("OpenSettingsCommand"));
            Assert.That(BindingPath(externalLyrics, FrameworkElement.ToolTipProperty),
                Is.EqualTo("ExternalLyricsStatus"));
        });
    }

    [Test]
    public void OverlayMenuAddsItsCommandsAroundTheSameSharedGroups()
    {
        var menu = new OverlayLyricsContextMenu();
        var headers = menu.Items.OfType<MenuItem>().Select(item => item.Header?.ToString()).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(headers, Does.Contain("Open Lyrics Window"));
            Assert.That(headers, Does.Contain("Current Line"));
            Assert.That(headers, Does.Contain("All Lyrics"));
            Assert.That(headers, Does.Contain("Lyrics Display"));
            Assert.That(headers, Does.Contain("Overlay"));
            Assert.That(headers, Does.Contain("Hide Desktop Lyrics"));
            Assert.That(headers.Any(header => header?.Contains("Reset", StringComparison.OrdinalIgnoreCase) == true),
                Is.False);
            Assert.That(menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Lyrics Display"))
                .Items.OfType<MenuItem>().Select(item => item.Header?.ToString()),
                Is.EqualTo(new[] { "One Line", "Two Lines", "All Lyrics" }));
        });
    }

    [Test]
    public void SeparateSurfaceMenusDoNotShareMenuItemsOrBindings()
    {
        var main = new MainLyricsContextMenu();
        var overlay = new OverlayLyricsContextMenu();
        var mainItems = AllItems(main).ToArray();
        var overlayItems = AllItems(overlay).ToArray();

        Assert.That(mainItems.Intersect(overlayItems, ReferenceEqualityComparer.Instance), Is.Empty);
        Assert.Multiple(() =>
        {
            Assert.That(BindingOperations.GetBinding(main, FrameworkElement.DataContextProperty)?.Path.Path,
                Is.EqualTo("PlacementTarget.DataContext.SurfaceActions"));
            Assert.That(BindingOperations.GetBinding(overlay, FrameworkElement.DataContextProperty)?.Path.Path,
                Is.EqualTo("PlacementTarget.DataContext"));
        });
    }

    private static IEnumerable<MenuItem> AllItems(ContextMenu menu)
    {
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            yield return item;
            foreach (var child in AllItems(item)) yield return child;
        }
    }

    private static IEnumerable<MenuItem> AllItems(MenuItem parent)
    {
        foreach (var item in parent.Items.OfType<MenuItem>())
        {
            yield return item;
            foreach (var child in AllItems(item)) yield return child;
        }
    }

    private static string? BindingPath(DependencyObject target, DependencyProperty property) =>
        BindingOperations.GetBinding(target, property)?.Path.Path;
}
