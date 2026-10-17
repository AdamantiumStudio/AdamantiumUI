using Adamantium.Graphics.Fonts;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core.Markup;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>Ruby written in markup, as HTML's: the build compiles it, the designer's loader builds the same.</summary>
[TestFixture]
public class RubyMarkupTests
{
    private const string Namespaces =
        "xmlns=\"http://adamantium/ui\" xmlns:x=\"http://adamantium/ui/xaml/extensions\"";

    private const string Block =
        "<TextBlock " + Namespaces + ">日本の<Ruby Annotation=\"かんじ\">漢字</Ruby>" +
        "<Ruby Annotation=\"よ\" RubyPosition=\"Under\" AnnotationFontSize=\"9\">読</Ruby>む</TextBlock>";

    [Test]
    public void Ruby_Compiles()
    {
        var errors = AumlCodegenHarness.Compile(
            "<Window " + Namespaces + ">" + Block.Replace(" " + Namespaces, string.Empty) + "</Window>");

        Assert.That(errors, Is.Empty, AumlCodegenHarness.Errors(errors));
    }

    [Test]
    public void TheDesignersLoader_BuildsRuby()
    {
        var result = AumlLoader.Load(Block);

        Assert.That(result.Diagnostics, Is.Empty, string.Join(" | ", result.Diagnostics));
        var text = (TextBlock)result.Root;
        var reading = (Ruby)text.Inlines[1];
        var under = (Ruby)text.Inlines[2];
        Assert.Multiple(() =>
        {
            Assert.That(reading.Annotation, Is.EqualTo("かんじ"));
            Assert.That(((Run)reading.Inlines[0]).Text, Is.EqualTo("漢字"));
            Assert.That(under.RubyPosition, Is.EqualTo(RubyPosition.Under));
            Assert.That(under.AnnotationFontSize, Is.EqualTo(9));
        });
    }
}
