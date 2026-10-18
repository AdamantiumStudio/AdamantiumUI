using System.Linq;
using Adamantium.Fonts;
using Adamantium.UI.Controls.Buttons;
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

        var edit = AumlLoader.Reconcile(text, load, after);

        Assert.That(edit.Reconciled, Is.True, string.Join(" | ", edit.Diagnostics));
        Assert.That(text.Inlines, Has.Count.EqualTo(1));
        Assert.That(((Run)text.Inlines[0]).Text, Is.EqualTo("one"));
    }

    // A control among inlines is set into the line as WPF sets it, in an InlineUIContainer of its own.
    [TestCase("<TextBlock><TextBlock.Inlines><Bold><Button/></Bold></TextBlock.Inlines></TextBlock>")]
    [TestCase("<TextBlock><TextBlock.Inlines><Button/></TextBlock.Inlines></TextBlock>")]
    [TestCase("<TextBlock>Press <Button/> here</TextBlock>")]
    public void AControlAmongInlines_Compiles(string body)
    {
        var auml = "<Window " + Namespaces + ">" + body + "</Window>";
        AumlCodegenHarness.Generate(auml, out var generated);
        var errors = AumlCodegenHarness.Compile(auml);

        Assert.That(generated, Is.Empty, AumlCodegenHarness.Errors(generated));
        Assert.That(errors, Is.Empty, AumlCodegenHarness.Errors(errors));
    }

    [Test]
    public void AControlAmongInlines_IsWrappedInAContainer()
    {
        var result = AumlLoader.Load(
            "<TextBlock " + Namespaces + ">Press <Button Content=\"OK\"/> here <Bold><Border Width=\"8\"/></Bold></TextBlock>");

        Assert.That(result.Diagnostics, Is.Empty, string.Join(" | ", result.Diagnostics));
        var inlines = ((TextBlock)result.Root).Inlines;
        Assert.That(inlines[1], Is.InstanceOf<InlineUIContainer>());
        Assert.That(((InlineUIContainer)inlines[1]).Child, Is.InstanceOf<Adamantium.UI.Controls.Buttons.Button>());
        Assert.That(((Run)inlines[2]).Text, Is.EqualTo(" here "));
        Assert.That(((InlineUIContainer)((Bold)inlines[3]).Inlines[0]).Child,
            Is.InstanceOf<Adamantium.UI.Controls.Decorators.Border>());
    }

    // White space between two inlines on lines of their own is a space, as in WPF.
    [Test]
    public void InlinesOnLinesOfTheirOwn_KeepASpaceBetweenThem()
    {
        var result = AumlLoader.Load(
            "<TextBlock " + Namespaces + ">\n    <Bold>a</Bold>\n    <Italic>b</Italic>\n</TextBlock>");

        Assert.That(result.Diagnostics, Is.Empty, string.Join(" | ", result.Diagnostics));
        var inlines = ((TextBlock)result.Root).Inlines;
        Assert.That(inlines, Has.Count.EqualTo(3));
        Assert.That(((Run)inlines[1]).Text, Is.EqualTo(" "));
    }

    // A comment between them takes nothing away, and adds no second space.
    [TestCase("\n    <!-- note -->\n    ")]
    [TestCase("<!-- note -->\n    ")]
    [TestCase("\n    <!-- note -->")]
    [TestCase("\n    <!-- note -->\n    <TextBlock.Tag>t</TextBlock.Tag>\n    ")]
    public void ACommentBetweenInlines_LeavesOneSpace(string gap)
    {
        var result = AumlLoader.Load(
            "<TextBlock " + Namespaces + ">\n    <Run Text=\"a\"/>" + gap + "<Run Text=\"b\"/>\n</TextBlock>");

        Assert.That(result.Diagnostics, Is.Empty, string.Join(" | ", result.Diagnostics));
        var text = (TextBlock)result.Root;
        Assert.That(string.Concat(text.Inlines.OfType<Run>().Select(run => run.Text)), Is.EqualTo("a b"));
    }

    // The texts either side of a comment are one text.
    [TestCase("Hello <!-- c --> world", "Hello world")]
    [TestCase("Hello<!-- c --> world", "Hello world")]
    [TestCase("Hello<!-- c -->world", "Helloworld")]
    [TestCase("日本\n    語 <Run Text=\"w\"/>", "日本語 w")]
    [TestCase("Hello <!-- c --> <Run Text=\"w\"/>", "Hello w")]
    public void ACommentInText_LeavesOneSpace(string content, string expected)
    {
        var result = AumlLoader.Load("<TextBlock " + Namespaces + ">" + content + "</TextBlock>");

        Assert.That(result.Diagnostics, Is.Empty, string.Join(" | ", result.Diagnostics));
        var text = (TextBlock)result.Root;
        Assert.That(string.Concat(text.Inlines.OfType<Run>().Select(run => run.Text)), Is.EqualTo(expected));
    }

    // A property element's text is the same with a comment in it as without: the text after the comment was lost.
    [Test]
    public void ACommentInAPropertyElementsText_KeepsAllOfIt()
    {
        var text = (TextBlock)AumlLoader.Load(
            "<TextBlock " + Namespaces + "><TextBlock.Text>Hello <!-- c --> world</TextBlock.Text></TextBlock>").Root;
        var button = (Button)AumlLoader.Load(
            "<Button " + Namespaces + "><Button.Content>Hello <!-- c --> world</Button.Content></Button>").Root;

        Assert.That(text.Text, Is.EqualTo("Hello world"));
        Assert.That(button.Content, Is.EqualTo("Hello world"));
    }

    // As WPF reads it: white space collapsed to one space, none at the ends; two spaces stayed two.
    [TestCase("Hello  world", "Hello world")]
    [TestCase("\n    Hello\n    world\n", "Hello world")]
    [TestCase("\tHello \t world ", "Hello world")]
    [TestCase("日本\n    語", "日本語")]
    [TestCase("日本 語", "日本 語")]
    [TestCase("日本\n    a", "日本 a")]
    [TestCase("한국\n어", "한국어")]
    public void APropertyElementsText_CollapsesItsWhiteSpace(string written, string expected)
    {
        var result = AumlLoader.Load("<TextBlock " + Namespaces + "><TextBlock.Text>" + written + "</TextBlock.Text></TextBlock>");

        Assert.That(result.Diagnostics, Is.Empty, string.Join(" | ", result.Diagnostics));
        Assert.That(((TextBlock)result.Root).Text, Is.EqualTo(expected));
    }

    // xml:space="preserve" keeps the white space as written, on the element or one around it; "default" undoes it.
    [TestCase("<TextBlock {0} xml:space=\"preserve\"><TextBlock.Text> Hello  world </TextBlock.Text></TextBlock>", " Hello  world ")]
    [TestCase("<TextBlock {0}><TextBlock.Text xml:space=\"preserve\"> Hello  world </TextBlock.Text></TextBlock>", " Hello  world ")]
    [TestCase("<TextBlock {0} xml:space=\"preserve\"> Hello  world </TextBlock>", " Hello  world ")]
    [TestCase("<TextBlock {0} xml:space=\"preserve\"><Run Text=\"a\"/>  <Run Text=\"b\"/></TextBlock>", "a  b")]
    [TestCase("<TextBlock {0} xml:space=\"preserve\"><TextBlock.Text xml:space=\"default\"> Hello  world </TextBlock.Text></TextBlock>", "Hello world")]
    public void XmlSpacePreserve_KeepsTheWhiteSpace(string markup, string expected)
    {
        var result = AumlLoader.Load(string.Format(markup, Namespaces));

        Assert.That(result.Diagnostics, Is.Empty, string.Join(" | ", result.Diagnostics));
        var text = (TextBlock)result.Root;
        var shown = text.Inlines.Count > 0 ? string.Concat(text.Inlines.OfType<Run>().Select(run => run.Text)) : text.Text;
        Assert.That(shown, Is.EqualTo(expected));
    }

    [Test]
    public void XmlSpacePreserve_Compiles()
    {
        var errors = AumlCodegenHarness.Compile("<Window " + Namespaces + " xml:space=\"preserve\">" +
                                                "<TextBlock><TextBlock.Text> a  b </TextBlock.Text></TextBlock></Window>");

        Assert.That(errors, Is.Empty, AumlCodegenHarness.Errors(errors));
    }

    // WPF does not take a property element in the middle of an element's text; ours joined the two texts with both
    // their spaces.
    [TestCase("Hello <TextBlock.Tag>t</TextBlock.Tag> world", true)]
    [TestCase("Hello <TextBlock.Tag>t</TextBlock.Tag><Run Text=\"w\"/>", true)]
    [TestCase("<Run Text=\"w\"/><TextBlock.Tag>t</TextBlock.Tag> world", true)]
    [TestCase("<TextBlock.Tag>t</TextBlock.Tag> Hello world", false)]
    [TestCase("Hello world <TextBlock.Tag>t</TextBlock.Tag>", false)]
    [TestCase("<Run Text=\"a\"/> <TextBlock.Tag>t</TextBlock.Tag> <Run Text=\"b\"/>", false)]
    public void APropertyElementAmidText_IsAnError(string content, bool error)
    {
        AumlCodegenHarness.Generate("<Window " + Namespaces + "><TextBlock>" + content + "</TextBlock></Window>", out var errors);

        Assert.That(errors.Any(e => e.GetMessage().Contains("TextBlock.Tag")), Is.EqualTo(error),
            AumlCodegenHarness.Errors(errors));
    }

    [Test]
    public void InlinesOnLinesOfTheirOwn_Compile()
    {
        var errors = AumlCodegenHarness.Compile("<Window " + Namespaces + ">\n<StackPanel>\n    <Border/>\n    <Border/>\n</StackPanel>" +
                                                "<TextBlock>\n    <Bold>a</Bold>\n    <Italic>b</Italic>\n</TextBlock></Window>");

        Assert.That(errors, Is.Empty, AumlCodegenHarness.Errors(errors));
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

        var edit = AumlLoader.Reconcile(text, load, Paragraph("after"));

        Assert.That(edit.Reconciled, Is.True, string.Join(" | ", edit.Diagnostics));
        Assert.That(text.Inlines, Has.Count.EqualTo(2), "the inlines were added again to the ones there");
        Assert.That(((Run)((Bold)text.Inlines[1]).Inlines[0]).Text, Is.EqualTo("after"));
    }

    private const string Paragraphs =
        "<TextBlock " + Namespaces + ">\n" +
        "    loose <Bold>words</Bold>\n" +
        "    <Paragraph Margin=\"10,4,20,8\" TextIndent=\"-12\" LineHeight=\"30\" HorizontalTextAlignment=\"Right\">\n" +
        "        first   paragraph\n" +
        "    </Paragraph>\n" +
        "    <Paragraph>second <Italic>one</Italic></Paragraph>\n" +
        "    tail\n" +
        "</TextBlock>";

    [Test]
    public void Paragraphs_Compile()
    {
        var errors = AumlCodegenHarness.Compile(
            "<Window " + Namespaces + ">" + Paragraphs.Replace(" " + Namespaces, string.Empty) + "</Window>");

        Assert.That(errors, Is.Empty, AumlCodegenHarness.Errors(errors));
    }

    // White space at a paragraph's edges is no text, as around a block in HTML; a space between words stays.
    [Test]
    public void Paragraphs_AreLoaded_WithNoSpaceAtTheirEdges()
    {
        var result = AumlLoader.Load(Paragraphs);

        Assert.That(result.Diagnostics, Is.Empty, string.Join(" | ", result.Diagnostics));
        var inlines = ((TextBlock)result.Root).Inlines;
        Assert.That(inlines.Select(inline => inline.GetType()),
            Is.EqualTo(new[] { typeof(Run), typeof(Bold), typeof(Paragraph), typeof(Paragraph), typeof(Run) }));
        Assert.That(((Run)inlines[0]).Text, Is.EqualTo("loose "));
        var first = (Paragraph)inlines[2];
        Assert.That(((Run)first.Inlines.Single()).Text, Is.EqualTo("first paragraph"));
        Assert.That(first.Margin, Is.EqualTo(new Adamantium.Mathematics.Thickness(10, 4, 20, 8)));
        Assert.That(first.TextIndent, Is.EqualTo(-12));
        Assert.That(first.LineHeight, Is.EqualTo(30));
        Assert.That(first.HorizontalTextAlignment, Is.EqualTo(Adamantium.Graphics.Fonts.HorizontalTextAlignment.Right));
        Assert.That(((Run)((Paragraph)inlines[3]).Inlines[0]).Text, Is.EqualTo("second "));
        Assert.That(((Run)inlines[4]).Text, Is.EqualTo("tail"));
    }

    [Test]
    public void ANullableEnum_IsSetByTheDesignersLoader_WithNoWarning()
    {
        var result = AumlLoader.Load("<TextBlock " + Namespaces + "><Run BaselineAlignment=\"Top\">x</Run></TextBlock>");

        Assert.That(result.Diagnostics, Is.Empty, string.Join(" | ", result.Diagnostics));
        Assert.That(((Run)((TextBlock)result.Root).Inlines[0]).BaselineAlignment,
            Is.EqualTo(Adamantium.Graphics.Fonts.BaselineAlignment.Top));
    }

    [TestCase("<TextBlock><Bold><Paragraph>x</Paragraph></Bold></TextBlock>", "Bold")]
    [TestCase("<TextBlock><Paragraph><Paragraph>x</Paragraph></Paragraph></TextBlock>", "Paragraph")]
    [TestCase("<TextBlock><TextBlock.Inlines><Span><Span.Inlines><Paragraph/></Span.Inlines></Span></TextBlock.Inlines></TextBlock>", "Span")]
    public void AParagraphInsideASpan_IsReportedWhereItStands(string body, string span)
    {
        AumlCodegenHarness.Generate("<Window " + Namespaces + ">" + body + "</Window>", out var errors);

        Assert.That(errors.Select(e => e.GetMessage()), Has.Some.Contains("A Paragraph cannot stand in a " + span));
    }

    [Test]
    public void AParagraphInsideASpan_IsReportedByTheDesignersLoaderToo()
    {
        var result = AumlLoader.Load("<TextBlock " + Namespaces + "><Bold><Paragraph>x</Paragraph></Bold></TextBlock>");

        Assert.That(result.Diagnostics.Select(diagnostic => diagnostic.ToString()),
            Has.Some.Contains("A Paragraph cannot stand in a Bold"));
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
