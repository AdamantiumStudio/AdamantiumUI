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

    private const string Mixed =
        "<TextBlock " + Namespaces + ">Hello <Bold>bold <Italic>both</Italic></Bold> and <Run Foreground=\"Red\">red</Run>" +
        "</TextBlock>";

    [Test]
    public void TextWrittenAmongInlines_Compiles()
    {
        var errors = AumlCodegenHarness.Compile(
            "<Window " + Namespaces + ">" + Mixed.Replace(" " + Namespaces, string.Empty) + "</Window>");

        Assert.That(errors, Is.Empty, AumlCodegenHarness.Errors(errors));
    }

    [Test]
    public void TextWrittenAmongInlines_BecomesRuns()
    {
        var result = AumlLoader.Load(Mixed);

        Assert.That(result.Diagnostics, Is.Empty, string.Join(" | ", result.Diagnostics));
        var inlines = ((TextBlock)result.Root).Inlines;
        Assert.That(inlines.Select(inline => inline.GetType()),
            Is.EqualTo(new[] { typeof(Run), typeof(Bold), typeof(Run), typeof(Run) }));
        Assert.That(((Run)inlines[0]).Text, Is.EqualTo("Hello "));
        var bold = (Bold)inlines[1];
        Assert.That(((Run)bold.Inlines[0]).Text, Is.EqualTo("bold "));
        Assert.That(((Run)((Italic)bold.Inlines[1]).Inlines[0]).Text, Is.EqualTo("both"));
        Assert.That(((Run)inlines[2]).Text, Is.EqualTo(" and "));
        Assert.That(((Run)inlines[3]).Text, Is.EqualTo("red"), "a run's own content is its text");
    }

    [Test]
    public void LinesOfMarkup_CollapseToSpaces_AndNoneAroundALineBreak()
    {
        var result = AumlLoader.Load(
            "<TextBlock " + Namespaces + ">\n    First   line\n    <LineBreak/>\n    second <Bold>part</Bold>\n</TextBlock>");

        Assert.That(result.Diagnostics, Is.Empty, string.Join(" | ", result.Diagnostics));
        var inlines = ((TextBlock)result.Root).Inlines;
        Assert.That(((Run)inlines[0]).Text, Is.EqualTo("First line"));
        Assert.That(inlines[1], Is.InstanceOf<LineBreak>());
        Assert.That(((Run)inlines[2]).Text, Is.EqualTo("second "));
        Assert.That(((Run)((Bold)inlines[3]).Inlines[0]).Text, Is.EqualTo("part"));
    }

    [Test]
    public void ASpaceBetweenTwoInlinesOnALine_IsKept_ANoBreakSpaceIsText()
    {
        var result = AumlLoader.Load(
            "<TextBlock " + Namespaces + "><Bold>a</Bold> <Italic>b</Italic>&#160;<Run>c&#160;&#160;d</Run></TextBlock>");

        Assert.That(result.Diagnostics, Is.Empty, string.Join(" | ", result.Diagnostics));
        var inlines = ((TextBlock)result.Root).Inlines;
        Assert.That(inlines, Has.Count.EqualTo(5));
        Assert.That(((Run)inlines[1]).Text, Is.EqualTo(" "), "the space between the two inlines");
        Assert.That(((Run)inlines[3]).Text, Is.EqualTo(" "), "a no-break space is text, not white space");
        Assert.That(((Run)inlines[4]).Text, Is.EqualTo("c  d"));
    }

    [Test]
    public void AValueWrittenWithACommentBesideIt_KeepsItsText()
    {
        var result = AumlLoader.Load(
            "<Border " + Namespaces + "><Border.Width>\n  <!-- wide -->\n  120\n</Border.Width></Border>");

        Assert.That(result.Diagnostics, Is.Empty, string.Join(" | ", result.Diagnostics));
        Assert.That(((Adamantium.UI.Controls.Decorators.Border)result.Root).Width, Is.EqualTo(120));
    }

    [Test]
    public void SwitchingToAnInlinesElement_InADesignerEdit_KeepsTheInlines()
    {
        var before = "<TextBlock " + Namespaces + ">plain <Bold>bold</Bold></TextBlock>";
        var after = "<TextBlock " + Namespaces + "><TextBlock.Inlines><Run Text=\"one\"/></TextBlock.Inlines></TextBlock>";
        var load = AumlLoader.Load(before);
        var text = (TextBlock)load.Root;

        var edit = AumlLoader.Reconcile(text, load.Ast, after);

        Assert.That(edit.Reconciled, Is.True, string.Join(" | ", edit.Diagnostics));
        Assert.That(text.Inlines, Has.Count.EqualTo(1));
        Assert.That(((Run)text.Inlines[0]).Text, Is.EqualTo("one"));
    }

    [TestCase("<TextBlock><TextBlock.Inlines><Bold><Button/></Bold></TextBlock.Inlines></TextBlock>")]
    [TestCase("<TextBlock><TextBlock.Inlines><Button/></TextBlock.Inlines></TextBlock>")]
    public void AControlAmongInlines_IsReportedWhereItStands(string body)
    {
        AumlCodegenHarness.Generate("<Window " + Namespaces + ">" + body + "</Window>", out var errors);

        Assert.That(errors.Select(e => e.GetMessage()), Has.Some.Contains("Button is not a Inline"));
    }

    private static string Paragraph(string word) =>
        "<TextBlock " + Namespaces + "><TextBlock.Inlines>" +
        "<Run Text=\"a \"/><Bold><Run Text=\"" + word + "\"/></Bold>" +
        "</TextBlock.Inlines></TextBlock>";

    [Test]
    public void AnEditInsideASpan_IsAppliedInPlace_NothingDoubles()
    {
        var load = AumlLoader.Load(Paragraph("before"));
        var text = (TextBlock)load.Root;

        var edit = AumlLoader.Reconcile(text, load.Ast, Paragraph("after"));

        Assert.That(edit.Reconciled, Is.True, string.Join(" | ", edit.Diagnostics));
        Assert.That(text.Inlines, Has.Count.EqualTo(2), "the inlines were added again to the ones there");
        Assert.That(((Run)((Bold)text.Inlines[1]).Inlines[0]).Text, Is.EqualTo("after"));
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
