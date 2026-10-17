using Adamantium.Graphics.Fonts;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core.Markup;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>Kerning written in markup, on a block and on an inline: the build compiles it, the designer's loader builds
/// the same.</summary>
[TestFixture]
public class KerningMarkupTests
{
    private const string Namespaces =
        "xmlns=\"http://adamantium/ui\" xmlns:x=\"http://adamantium/ui/xaml/extensions\"";

    private const string Block =
        "<TextBlock " + Namespaces + " Kerning=\"Optical\"><TextBlock.Inlines>" +
        "<Run Text=\"AV\"/><Run Text=\"To\" Kerning=\"None\"/>" +
        "</TextBlock.Inlines></TextBlock>";

    [Test]
    public void Kerning_Compiles()
    {
        var errors = AumlCodegenHarness.Compile(
            "<Window " + Namespaces + ">" + Block.Replace(" " + Namespaces, string.Empty) + "</Window>");

        Assert.That(errors, Is.Empty, AumlCodegenHarness.Errors(errors));
    }

    [Test]
    public void TheDesignersLoader_BuildsKerning()
    {
        var result = AumlLoader.Load(Block);

        Assert.That(result.Diagnostics, Is.Empty, string.Join(" | ", result.Diagnostics));
        var text = (TextBlock)result.Root;
        Assert.Multiple(() =>
        {
            Assert.That(text.Kerning, Is.EqualTo(TextKerning.Optical));
            Assert.That(text.Inlines[0].Kerning, Is.Null);
            Assert.That(text.Inlines[1].Kerning, Is.EqualTo(TextKerning.None));
        });
    }
}
