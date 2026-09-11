using System.Threading;
using System.Windows;
using System.Windows.Controls;

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
                Assert.That(window.ResizeMode, Is.EqualTo(ResizeMode.NoResize));
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
    public void TextBlocksWrapAndPreserveUnicodeWholeLineText()
    {
        var window = new LyricsOverlayWindow();
        try
        {
            var grid = (Grid)((Border)window.Content).Child;
            var primary = (TextBlock)grid.Children[0];
            var secondary = (TextBlock)grid.Children[1];
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
}
