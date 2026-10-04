using System.Collections.Frozen;
using LyricsDisplayer.Core.Library;

namespace LyricsDisplayer.Core.Tests;

[TestFixture]
public sealed class LrcLineSemanticsTests
{
    [Test]
    public void BlankAndWhitespaceAreIntrinsicSemanticAnchors()
    {
        Assert.Multiple(() =>
        {
            Assert.That(LrcLineSemantics.IsZeroTimeIntroOrBreakAnchor(null), Is.True);
            Assert.That(LrcLineSemantics.IsZeroTimeIntroOrBreakAnchor(string.Empty), Is.True);
            Assert.That(LrcLineSemantics.IsZeroTimeIntroOrBreakAnchor(" \t\r\n"), Is.True);
            Assert.That(LrcLineSemantics.IsZeroTimeIntroOrBreakAnchor(" \t", FrozenSet<string>.Empty), Is.True,
                "Blank text remains intrinsic even when the effective explicit-marker set is empty.");
        });
    }

    [Test]
    public void DefaultExplicitBreakMarkerMatchesExactlyAfterTrimming()
    {
        Assert.Multiple(() =>
        {
            Assert.That(LrcLineSemantics.IsZeroTimeIntroOrBreakAnchor("♪"), Is.True);
            Assert.That(LrcLineSemantics.IsZeroTimeIntroOrBreakAnchor("  ♪\t"), Is.True);
            Assert.That(LrcLineSemantics.IsZeroTimeIntroOrBreakAnchor("♪ with text"), Is.False);
            Assert.That(LrcLineSemantics.IsZeroTimeIntroOrBreakAnchor("Actual lyric"), Is.False);
            Assert.That(LrcLineSemantics.DefaultExplicitBreakMarkers, Is.EquivalentTo(new[] { "♪" }));
            Assert.That(LrcLineSemantics.DefaultExplicitBreakMarkers, Is.InstanceOf<FrozenSet<string>>(),
                "The shared default is a frozen set rather than a mutable collection.");
        });
    }

    [Test]
    public void SuppliedEffectiveMarkersReplaceDefaultsAndUseTrimmedOrdinalExactMatching()
    {
        IReadOnlySet<string> customMarkers = new HashSet<string>(["[Music]", "☕"], StringComparer.Ordinal);
        IReadOnlySet<string> caseInsensitiveMarkerSet = new HashSet<string>(["[Music]"], StringComparer.OrdinalIgnoreCase);

        Assert.Multiple(() =>
        {
            Assert.That(LrcLineSemantics.IsZeroTimeIntroOrBreakAnchor("[Music]", customMarkers), Is.True);
            Assert.That(LrcLineSemantics.IsZeroTimeIntroOrBreakAnchor("  ☕\t", customMarkers), Is.True);
            Assert.That(LrcLineSemantics.IsZeroTimeIntroOrBreakAnchor("♪", customMarkers), Is.False,
                "A supplied set is the effective set and does not implicitly extend the defaults.");
            Assert.That(LrcLineSemantics.IsZeroTimeIntroOrBreakAnchor("Actual lyric", customMarkers), Is.False);
            Assert.That(LrcLineSemantics.IsZeroTimeIntroOrBreakAnchor("[music]", caseInsensitiveMarkerSet), Is.False,
                "Marker matching is ordinal even if the caller's set uses another comparer.");
            Assert.That(LrcLineSemantics.IsZeroTimeIntroOrBreakAnchor("♪"), Is.True,
                "Omitting a set falls back to the default marker collection.");
        });
    }
}
