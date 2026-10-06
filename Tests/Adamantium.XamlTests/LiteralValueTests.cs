using System.Linq;
using Adamantium.UI.Core.Markup;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A written value the property's type cannot take - an enum member that is not there, a word where a number
/// goes - is named by the build and by the preview alike, with what the property expects.</summary>
[TestFixture]
public class LiteralValueTests
{
    [TestCase("TextBlock", "HorizontalAlignment", "Middle")]
    [TestCase("TextBlock", "HorizontalAlignment", "")]
    [TestCase("TextBlock", "HorizontalAlignment", "Left, Middle")]
    [TestCase("TextBlock", "IsEnabled", "yes")]
    [TestCase("TextBlock", "Width", "wide")]
    [TestCase("TextBlock", "Width", "")]
    [TestCase("DataPager", "PageSize", "ten")]
    [TestCase("DataPager", "PageSize", "2.5")]
    public void AValueTheTypeCannotTake_IsNamed(string element, string property, string value)
    {
        var markup = $"<{element} {property}=\"{value}\"/>";
        AumlCodegenHarness.Generate(AumlCodegenHarness.WindowHeader + $">{markup}</Window>", out var build);
        var preview = AumlLoader.Load($"<Grid xmlns=\"http://adamantium/ui\">{markup}</Grid>").Diagnostics;

        var expected = $"'{value}' is not a valid";
        Assert.That(build.Select(e => e.GetMessage()), Has.Some.Contains(expected));
        Assert.That(preview, Has.Some.Contains(expected));
    }

    [TestCase("TextBlock", "HorizontalAlignment", "Center")]
    [TestCase("TextBlock", "IsEnabled", "True")]
    [TestCase("TextBlock", "Width", "12.5")]
    [TestCase("TextBlock", "Width", "Auto")]
    [TestCase("TextBlock", "Width", "Infinity")]
    [TestCase("TextBlock", "Width", "-3")]
    [TestCase("DataPager", "PageSize", "10")]
    public void AValueTheTypeTakes_Builds(string element, string property, string value)
    {
        var markup = AumlCodegenHarness.WindowHeader + $"><{element} {property}=\"{value}\"/></Window>";
        AumlCodegenHarness.Generate(markup, out var generatorErrors);
        var compileErrors = AumlCodegenHarness.Compile(markup);

        Assert.That(generatorErrors.Concat(compileErrors).Select(e => e.GetMessage()), Is.Empty);
    }
}
