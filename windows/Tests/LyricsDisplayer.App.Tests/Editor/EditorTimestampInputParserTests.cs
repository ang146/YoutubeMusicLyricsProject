namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class EditorTimestampInputParserTests
{
    [TestCase("0", "00:00.000")]
    [TestCase("0:1", "00:01.000")]
    [TestCase("1:2", "01:02.000")]
    [TestCase("01:12", "01:12.000")]
    [TestCase("1:12", "01:12.000")]
    [TestCase("1:12.3", "01:12.300")]
    [TestCase("1:12.34", "01:12.340")]
    [TestCase("1:12.345", "01:12.345")]
    [TestCase("03:12.123", "03:12.123")]
    [TestCase(" 1:12 ", "01:12.000")]
    [TestCase(" 0 ", "00:00.000")]
    [TestCase("1:59", "01:59.000")]
    [TestCase("72:15", "72:15.000")]
    public void NormalizeCommittedValueUsesCanonicalTimestampFormatting(string input, string expected)
    {
        Assert.That(EditorTimestampInputParser.NormalizeCommittedValue(input), Is.EqualTo(expected));
    }

    [TestCase("", "")]
    [TestCase("   ", "")]
    public void NormalizeCommittedValueKeepsEmptyInputEmpty(string input, string expected)
    {
        Assert.That(EditorTimestampInputParser.NormalizeCommittedValue(input), Is.EqualTo(expected));
    }

    [TestCase("abc")]
    [TestCase(":")]
    [TestCase("1::")]
    [TestCase("1:2.")]
    [TestCase("1:60")]
    [TestCase("1:12.1234")]
    [TestCase("-1")]
    [TestCase("-0:10")]
    [TestCase("72")]
    public void NormalizeCommittedValuePreservesUnsupportedOrInvalidInput(string input)
    {
        Assert.That(EditorTimestampInputParser.NormalizeCommittedValue(input), Is.EqualTo(input));
    }
}
