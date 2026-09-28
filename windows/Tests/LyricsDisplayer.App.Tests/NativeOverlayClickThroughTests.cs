namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class NativeOverlayClickThroughTests
{
    [Test]
    public void EnablingAddsTransparentAndNoActivateWithoutDiscardingOtherFlags()
    {
        const long existing = 0x00000100;
        var result = NativeOverlayClickThrough.ApplyToStyle(existing, true);
        Assert.Multiple(() =>
        {
            Assert.That(result & existing, Is.EqualTo(existing));
            Assert.That(result & NativeOverlayClickThrough.TransparentStyle, Is.Not.Zero);
            Assert.That(result & NativeOverlayClickThrough.NoActivateStyle, Is.Not.Zero);
        });
    }

    [Test]
    public void DisablingRemovesOnlyTransparentAndKeepsNoActivate()
    {
        var existing = 0x00000100L | NativeOverlayClickThrough.TransparentStyle |
                       NativeOverlayClickThrough.NoActivateStyle;
        var result = NativeOverlayClickThrough.ApplyToStyle(existing, false);
        Assert.Multiple(() =>
        {
            Assert.That(result & NativeOverlayClickThrough.TransparentStyle, Is.Zero);
            Assert.That(result & NativeOverlayClickThrough.NoActivateStyle, Is.Not.Zero);
            Assert.That(result & 0x00000100L, Is.Not.Zero);
        });
    }
}
