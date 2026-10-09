using System.Globalization;
using System.Reflection;
using LyricsDisplayer.Resources;

namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class StringsTests
{
    [TestCaseSource(nameof(PublicStringProperties))]
    public void EveryPublicStringResourceHasANonBlankDefaultValue(PropertyInfo property)
    {
        var previousCulture = Strings.Culture;
        try
        {
            Strings.Culture = CultureInfo.InvariantCulture;

            string? value;
            try
            {
                value = (string?)property.GetValue(null);
            }
            catch (Exception exception)
            {
                Assert.Fail($"Getter for Strings.{property.Name} threw an exception: {exception}");
                return;
            }

            Assert.That(value, Is.Not.Null, $"Strings.{property.Name} returned null.");
            Assert.That(value, Is.Not.Empty, $"Strings.{property.Name} returned an empty string.");
            Assert.That(string.IsNullOrWhiteSpace(value), Is.False,
                $"Strings.{property.Name} returned a whitespace-only string.");
        }
        finally
        {
            Strings.Culture = previousCulture;
        }
    }

    private static IEnumerable<TestCaseData> PublicStringProperties() =>
        typeof(Strings)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(property => property.PropertyType == typeof(string) &&
                property.GetMethod is { IsPublic: true, IsStatic: true })
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => new TestCaseData(property)
                .SetName($"DefaultResourceString_{property.Name}_IsNonBlank"));
}
