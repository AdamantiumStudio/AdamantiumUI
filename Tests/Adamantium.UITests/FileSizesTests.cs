using Adamantium.Localization;
using Adamantium.UI.Text;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A file's size as a person reads it: the largest unit it fills, in the language's units and numbers.</summary>
[TestFixture]
public class FileSizesTests
{
    private string _language;

    [SetUp]
    public void RememberLanguage() => _language = Languages.Current;

    [TearDown]
    public void GiveLanguageBack() => Languages.Current = _language;

    [TestCase("en", 100, "100 B")]
    [TestCase("en", 43_008, "42 KB")]
    [TestCase("en", 2_516_582, "2.4 MB")]
    [TestCase("en", 3L << 30, "3.0 GB")]
    [TestCase("ru", 2_516_582, "2,4 МБ")]
    [TestCase("ru", 43_008, "42 КБ")]
    [TestCase("en", -1, "")]
    public void ASize_IsReadInTheLargestUnitItFills(string language, long bytes, string read)
    {
        Languages.Current = language;

        Assert.That(FileSizes.Format(bytes), Is.EqualTo(read));
    }
}
