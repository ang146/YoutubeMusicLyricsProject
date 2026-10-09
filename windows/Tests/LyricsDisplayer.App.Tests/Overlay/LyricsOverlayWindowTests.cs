using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using LyricsDisplayer.Core.Settings;

namespace LyricsDisplayer.App.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class LyricsOverlayWindowTests
{
    [Test]
    public void WindowUsesDesktopOverlayChromeTopmostAndNonActivatingConfiguration()
    {
        var window = new LyricsOverlayWindow();
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(window.WindowStyle, Is.EqualTo(WindowStyle.None));
                Assert.That(window.ResizeMode, Is.EqualTo(ResizeMode.CanResize));
                Assert.That(window.AllowsTransparency, Is.True);
                Assert.That(window.Topmost, Is.True);
                Assert.That(window.ShowInTaskbar, Is.False);
                Assert.That(window.ShowActivated, Is.False);
                Assert.That(window.Focusable, Is.False);
                Assert.That(window.Opacity, Is.EqualTo(1));
            });
        }
        finally
        {
            window.CloseForApplicationShutdown();
        }
    }

    [Test]
    public void OverlayWindowStartsInNormalState()
    {
        var window = new LyricsOverlayWindow();
        try
        {
            Assert.That(window.WindowState, Is.EqualTo(WindowState.Normal));
        }
        finally
        {
            window.CloseForApplicationShutdown();
        }
    }

    [Test]
    public void TextBlocksWrapAndPreserveUnicodeWholeLineText()
    {
        var window = new LyricsOverlayWindow();
        try
        {
            var primary = window.PrimaryTextForTesting;
            var secondary = window.SecondaryTextForTesting;
            var state = new LyricsOverlayPresentationState(
                "繁體中文、简体中文、日本語、한국어 — a deliberately long whole lyric line ♪",
                "下一行");
            window.SetLyrics(state);

            Assert.Multiple(() =>
            {
                Assert.That(primary.Text, Is.EqualTo(state.PrimaryText));
                Assert.That(secondary.Text, Is.EqualTo(state.SecondaryText));
                Assert.That(primary.TextWrapping, Is.EqualTo(TextWrapping.Wrap));
                Assert.That(secondary.TextWrapping, Is.EqualTo(TextWrapping.Wrap));
            });
        }
        finally
        {
            window.CloseForApplicationShutdown();
        }
    }

    [Test]
    public void InteractionStateAppliesPersistentWidthAndHeightIndependentOfContentMode()
    {
        var window = new LyricsOverlayWindow();
        try
        {
            window.ApplyInteractionState(new(true, true, LyricsContentMode.OneLine, 1200, 420), effectiveTopmost: true);
            var secondary = window.SecondaryTextForTesting;

            Assert.Multiple(() =>
            {
                Assert.That(window.Width, Is.EqualTo(1200));
                Assert.That(window.Height, Is.EqualTo(420));
                Assert.That(window.Topmost, Is.True);
                Assert.That(secondary.Visibility, Is.EqualTo(Visibility.Collapsed));
                Assert.That(window.Interaction.ClickThrough, Is.True);
                Assert.That(((System.Windows.Media.SolidColorBrush)window.SurfaceBackgroundForTesting).Color.A, Is.Zero);
            });
        }
        finally
        {
            window.CloseForApplicationShutdown();
        }
    }

    [Test]
    public void ModalSuppressionChangesTopmostAndReleaseRestoresIt()
    {
        var window = new LyricsOverlayWindow();
        try
        {
            var interaction = new OverlayInteractionState(false, false,
                LyricsContentMode.TwoLines, 900, 220);
            window.ApplyInteractionState(interaction, effectiveTopmost: false);
            Assert.That(window.Topmost, Is.False);
            window.ApplyInteractionState(interaction, effectiveTopmost: true);

            Assert.That(window.Topmost, Is.True);
        }
        finally
        {
            window.CloseForApplicationShutdown();
        }
    }

    [Test]
    public void OverlayContextMenuDoesNotExposeAlwaysOnTopToggle()
    {
        var window = new LyricsOverlayWindow();
        try
        {
            var overlayMenu = window.ContextMenuForTesting.Items.OfType<MenuItem>()
                .Single(item => Equals(item.Header, "Overlay"));
            Assert.That(overlayMenu.Items.OfType<MenuItem>()
                .Any(item => Equals(item.Header, "Always on Top")), Is.False);
        }
        finally
        {
            window.CloseForApplicationShutdown();
        }
    }

    [Test]
    public void ContentModeChangesDoNotAlterWindowGeometry()
    {
        var window = new LyricsOverlayWindow();
        try
        {
            foreach (var mode in Enum.GetValues<LyricsContentMode>())
            {
                window.ApplyInteractionState(new(false, false, mode, 913, 377), effectiveTopmost: true);
                Assert.That(window.Width, Is.EqualTo(913));
                Assert.That(window.Height, Is.EqualTo(377));
            }
        }
        finally
        {
            window.CloseForApplicationShutdown();
        }
    }

    [Test]
    public void RealWindowKeepsPartialClickThroughStateOnTheExistingWindow()
    {
        var window = new LyricsOverlayWindow();
        try
        {
            window.Show();
            _ = new WindowInteropHelper(window).Handle;
            window.ApplyInteractionState(new(false, true, LyricsContentMode.TwoLines, 900, 220), effectiveTopmost: true);
            Assert.That(window.Interaction.ClickThrough, Is.True);
        }
        finally
        {
            window.CloseForApplicationShutdown();
        }
    }

    [Test]
    public void ContextMenuKeepsItsOverlayOwnedActionsSeparateFromMainSettings()
    {
        var window = new LyricsOverlayWindow();
        try
        {
            var items = window.ContextMenuForTesting.Items.OfType<MenuItem>().ToArray();
            var headers = items.Select(item => item.Header?.ToString() ?? string.Empty).ToArray();
            var displayMenu = items.Single(item => Equals(item.Header, "Lyrics Display"));
            var overlayMenu = items.Single(item => Equals(item.Header, "Overlay"));

            Assert.Multiple(() =>
            {
                Assert.That(headers, Does.Contain("Open Lyrics Window"));
                Assert.That(headers, Does.Contain("Lyrics Display"));
                Assert.That(headers, Does.Contain("Overlay"));
                Assert.That(headers, Does.Contain("Hide Desktop Lyrics"));
                Assert.That(headers, Does.Not.Contain("Settings"));
                Assert.That(displayMenu.Items.OfType<MenuItem>().Select(item => item.Header?.ToString()),
                    Is.EqualTo(new[] { "One Line", "Two Lines", "All Lyrics" }));
                Assert.That(overlayMenu.Items.OfType<MenuItem>().Select(item => item.Header?.ToString()),
                    Is.EqualTo(new[] { "Lock Position", "Click Through" }));
            });
        }
        finally
        {
            window.CloseForApplicationShutdown();
        }
    }

    [Test]
    public void AllLyricsVisualTreeHasNoScrollViewer()
    {
        var window = new LyricsOverlayWindow();
        try
        {
            window.Show();
            window.SetLyrics(new LyricsOverlayPresentationState("", "")
            {
                ContentMode = LyricsContentMode.AllLyrics,
                AllLines = [new(3, "current", LyricLineRole.Current)]
            });
            window.UpdateLayout();
            Assert.That(Descendants(window.AllLyricsItemsForTesting).OfType<ScrollViewer>(), Is.Empty);
        }
        finally
        {
            window.CloseForApplicationShutdown();
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

}
