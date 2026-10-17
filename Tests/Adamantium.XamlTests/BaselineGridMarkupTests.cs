using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core.Markup;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A baseline grid written in markup on a panel: the build compiles it, the designer's loader builds the same
/// and the blocks inside inherit it.</summary>
[TestFixture]
public class BaselineGridMarkupTests
{
    private const string Namespaces =
        "xmlns=\"http://adamantium/ui\" xmlns:x=\"http://adamantium/ui/xaml/extensions\"";

    private const string Panel =
        "<StackPanel " + Namespaces + " BaselineGridStep=\"24\" BaselineGridOffset=\"5\" BaselineGridFirstLineOnly=\"True\">" +
        "<TextBlock Text=\"A heading\" FontSize=\"30\"/><TextBlock Text=\"Body\"/>" +
        "</StackPanel>";

    [Test]
    public void TheGrid_Compiles()
    {
        var errors = AumlCodegenHarness.Compile(
            "<Window " + Namespaces + ">" + Panel.Replace(" " + Namespaces, string.Empty) + "</Window>");

        Assert.That(errors, Is.Empty, AumlCodegenHarness.Errors(errors));
    }

    [Test]
    public void TheDesignersLoader_BuildsTheGrid()
    {
        var result = AumlLoader.Load(Panel);

        Assert.That(result.Diagnostics, Is.Empty, string.Join(" | ", result.Diagnostics));
        var panel = (StackPanel)result.Root;
        var body = (TextBlock)panel.Children[1];
        Assert.Multiple(() =>
        {
            Assert.That(panel.BaselineGridStep, Is.EqualTo(24));
            Assert.That(body.BaselineGridStep, Is.EqualTo(24));
            Assert.That(body.BaselineGridOffset, Is.EqualTo(5));
            Assert.That(body.BaselineGridFirstLineOnly, Is.True);
        });
    }
}
