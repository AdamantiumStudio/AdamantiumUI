using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>
/// A text value for a property is parsed at run time by the TypeParser. A type it has no parser for still generates,
/// and then throws where the view is built - so a tab or a window comes up empty with nothing said at build time. The
/// generator warns instead.
/// </summary>
[TestFixture]
public class AumlTypeParserCheckTests
{
    private const string NoParser = "has no type parser";

    private static string WindowWith(string child) =>
        AumlCodegenHarness.WindowHeader + "><Grid>" + child + "</Grid></Window>";

    [Test]
    public void ATextValueForATypeWithNoParser_IsWarnedAbout()
    {
        var warnings = AumlCodegenHarness.Warnings(WindowWith("<ContentControl ContentTemplate=\"Card\"/>"));

        Assert.That(string.Join(" ", warnings), Does.Contain(NoParser).And.Contain("ContentTemplate=\"Card\""));
    }

    [TestCase("<TextBlock FontFamily=\"Bahnschrift\"/>")]
    [TestCase("<TextBlock FontWeight=\"SemiBold\"/>")]
    [TestCase("<Border Background=\"#FF0000\"/>")]
    [TestCase("<Border Margin=\"1,2,3,4\"/>")]
    [TestCase("<Border HorizontalAlignment=\"Left\"/>")]
    public void TypesTheParserKnows_AreNotWarnedAbout(string element)
    {
        var warnings = AumlCodegenHarness.Warnings(WindowWith(element));

        Assert.That(string.Join(" ", warnings), Does.Not.Contain(NoParser));
    }
}
