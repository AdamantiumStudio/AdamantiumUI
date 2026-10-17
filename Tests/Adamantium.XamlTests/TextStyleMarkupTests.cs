using Adamantium.Fonts;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core.Markup;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>Paragraph and character styles written in markup as resources and set on paragraphs and runs: the build
/// compiles them, the designer's loader builds the same.</summary>
[TestFixture]
public class TextStyleMarkupTests
{
    private const string Namespaces =
        "xmlns=\"http://adamantium/ui\" xmlns:x=\"http://adamantium/ui/xaml/extensions\"";

    private const string Panel =
        "<StackPanel " + Namespaces + "><StackPanel.Resources>" +
        "<ParagraphStyle x:Key=\"Body\" FontSize=\"16\" Margin=\"0,0,0,8\"/>" +
        "<ParagraphStyle x:Key=\"Heading\" BasedOn=\"{StaticResource Body}\" FontSize=\"28\" TextIndent=\"0\"/>" +
        "<CharacterStyle x:Key=\"Emphasis\" FontWeight=\"Bold\"/>" +
        "</StackPanel.Resources>" +
        "<TextBlock><Paragraph ParagraphStyle=\"{StaticResource Heading}\">Title</Paragraph>" +
        "<Paragraph ParagraphStyle=\"{StaticResource Body}\">Body <Run CharacterStyle=\"{StaticResource Emphasis}\" Text=\"em\"/></Paragraph>" +
        "</TextBlock></StackPanel>";

    [Test]
    public void Styles_Compile()
    {
        var errors = AumlCodegenHarness.Compile(
            "<Window " + Namespaces + ">" + Panel.Replace(" " + Namespaces, string.Empty) + "</Window>");

        Assert.That(errors, Is.Empty, AumlCodegenHarness.Errors(errors));
    }

    // The designer's loader takes no static resources; the styles written in place.
    private const string Block =
        "<TextBlock " + Namespaces + "><Paragraph><Paragraph.ParagraphStyle>" +
        "<ParagraphStyle FontSize=\"28\" Margin=\"0,0,0,8\" TextIndent=\"0\"><ParagraphStyle.BasedOn>" +
        "<ParagraphStyle FontSize=\"16\"/></ParagraphStyle.BasedOn></ParagraphStyle>" +
        "</Paragraph.ParagraphStyle>Title</Paragraph>" +
        "<Paragraph>Body <Run Text=\"em\"><Run.CharacterStyle><CharacterStyle FontWeight=\"Bold\"/></Run.CharacterStyle></Run>" +
        "</Paragraph></TextBlock>";

    [Test]
    public void TheDesignersLoader_BuildsTheStyles()
    {
        var result = AumlLoader.Load(Block);

        Assert.That(result.Diagnostics, Is.Empty, string.Join(" | ", result.Diagnostics));
        var text = (TextBlock)result.Root;
        var heading = ((Paragraph)text.Inlines[0]).ParagraphStyle;
        var body = (Paragraph)text.Inlines[1];
        Assert.Multiple(() =>
        {
            Assert.That(heading.FontSize, Is.EqualTo(28));
            Assert.That(heading.BasedOn.FontSize, Is.EqualTo(16));
            Assert.That(heading.Margin, Is.EqualTo(new Thickness(0, 0, 0, 8)));
            Assert.That(((Run)body.Inlines[1]).CharacterStyle.FontWeight, Is.EqualTo(FontWeight.Bold));
        });
    }
}
