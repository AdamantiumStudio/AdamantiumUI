using System.Linq;
using Adamantium.Fonts;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core.Markup;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>Spans written in markup: the build compiles them, and the designer's loader builds the same nesting.</summary>
[TestFixture]
public class InlineSpansMarkupTests
{
    private const string Namespaces =
        "xmlns=\"http://adamantium/ui\" xmlns:x=\"http://adamantium/ui/xaml/extensions\"";

    private const string Block =
        "<TextBlock " + Namespaces + "><TextBlock.Inlines>" +
        "<Run Text=\"plain \"/>" +
        "<Bold><Run Text=\"bold \"/><Italic><Run Text=\"both\"/></Italic></Bold>" +
        "<LineBreak/>" +
        "<Span FontSize=\"22\"><Underline><Run Text=\"under\"/></Underline></Span>" +
        "</TextBlock.Inlines></TextBlock>";

    [Test]
    public void Spans_Compile()
    {
        var errors = AumlCodegenHarness.Compile(
            "<Window " + Namespaces + ">" + Block.Replace(" " + Namespaces, string.Empty) + "</Window>");

        Assert.That(errors, Is.Empty, AumlCodegenHarness.Errors(errors));
    }

    [Test]
    public void TheDesignersLoader_BuildsTheSameNesting()
    {
        var result = AumlLoader.Load(Block);

        Assert.That(result.Diagnostics, Is.Empty, string.Join(" | ", result.Diagnostics));
        var inlines = ((TextBlock)result.Root).Inlines;
        Assert.That(inlines.Select(inline => inline.GetType()),
            Is.EqualTo(new[] { typeof(Run), typeof(Bold), typeof(LineBreak), typeof(Span) }));
        var bold = (Bold)inlines[1];
        Assert.That(bold.FontWeight, Is.EqualTo(FontWeight.Bold));
        Assert.That(bold.Inlines[1], Is.InstanceOf<Italic>());
        Assert.That(((Run)((Italic)bold.Inlines[1]).Inlines[0]).Text, Is.EqualTo("both"));
        var span = (Span)inlines[3];
        Assert.That(span.FontSize, Is.EqualTo(22));
        Assert.That(span.Inlines[0], Is.InstanceOf<Underline>());
    }
}
