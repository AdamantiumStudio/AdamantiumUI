using System.Linq;
using Adamantium.UI.Core.Markup;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A property element left empty - nothing in it yet, or only a comment - sets nothing. It crashed the
/// generator with an index out of range.</summary>
[TestFixture]
public class EmptyPropertyElementTests
{
    [TestCase("<Grid.Tag></Grid.Tag>")]
    [TestCase("<Grid.Tag>\n    <!-- to come -->\n</Grid.Tag>")]
    [TestCase("<Grid.ColumnDefinitions/>")]
    public void AnEmptyPropertyElement_SetsNothing_AndBuilds(string property)
    {
        var markup = AumlCodegenHarness.WindowHeader + $"><Grid>{property}</Grid></Window>";
        AumlCodegenHarness.Generate(markup, out var generatorErrors);
        var compileErrors = AumlCodegenHarness.Compile(markup);

        Assert.That(generatorErrors.Concat(compileErrors).Select(e => e.GetMessage()), Is.Empty);
    }

    [Test]
    public void AnEmptyPropertyElement_SetsNothing_InThePreview()
    {
        var result = AumlLoader.Load(
            "<Grid xmlns=\"http://adamantium/ui\"><Grid.Tag><!-- to come --></Grid.Tag></Grid>");

        Assert.That(result.Diagnostics, Is.Empty, string.Join(" | ", result.Diagnostics));
        Assert.That(result.Root, Is.Not.Null);
    }
}
