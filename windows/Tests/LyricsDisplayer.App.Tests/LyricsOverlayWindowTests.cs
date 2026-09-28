using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
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
    public void InteractionStateControlsWidthHeightTopmostSecondaryLineAndNativeStyle()
    {
        var window = new LyricsOverlayWindow();
        try
        {
            window.ApplyInteractionState(new(true, true, false, LyricsContentMode.OneLine, 1200));
            var secondary = window.SecondaryTextForTesting;

            Assert.Multiple(() =>
            {
                Assert.That(window.Width, Is.EqualTo(1200));
                Assert.That(window.Height, Is.EqualTo(150));
                Assert.That(window.Topmost, Is.False);
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
    public void RealWindowKeepsPartialClickThroughStateOnTheExistingWindow()
    {
        var window = new LyricsOverlayWindow();
        try
        {
            window.Show();
            _ = new WindowInteropHelper(window).Handle;
            window.ApplyInteractionState(new(false, true, true, LyricsContentMode.TwoLines, 900));
            Assert.That(window.Interaction.ClickThrough, Is.True);
        }
        finally
        {
            window.CloseForApplicationShutdown();
        }
    }

    [Test]
    public void AllLyricsModeBuildsRoleAnnotatedListAndTimingAvailabilityFollowsCoordinator()
    {
        var window = new LyricsOverlayWindow();
        try
        {
            window.ApplyInteractionState(new(false, false, true, LyricsContentMode.AllLyrics, 900));
            window.SetLyrics(new LyricsOverlayPresentationState("", "")
            {
                ContentMode = LyricsContentMode.AllLyrics,
                CurrentIndex = 1,
                AllLines = [
                    new(0, "Past", LyricLineRole.Past),
                    new(1, "Current", LyricLineRole.Current),
                    new(2, "Next", LyricLineRole.Upcoming)
                ]
            });
            window.ApplyTimingState(currentLineEnabled: false, globalTimingEnabled: false, globalOffsetMs: 0);

            Assert.Multiple(() =>
            {
                Assert.That(window.AllLyricsListForTesting.Visibility, Is.EqualTo(Visibility.Visible));
                Assert.That(window.AllLyricsListForTesting.Items.Count, Is.EqualTo(3));
                Assert.That(window.CurrentLineMenuEnabledForTesting, Is.False);
            });

            window.ApplyTimingState(currentLineEnabled: true, globalTimingEnabled: true, globalOffsetMs: 500);
            Assert.That(window.CurrentLineMenuEnabledForTesting, Is.True);
        }
        finally
        {
            window.CloseForApplicationShutdown();
        }
    }

}
