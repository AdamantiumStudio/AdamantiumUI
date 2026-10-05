using System.Linq;
using Adamantium.UI.LanguageServer;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>Markup is laid out one way: an element per line indented by level, a single attribute on the tag's line and
/// several one per line under the first, a setter and a phrase on one line, an empty element closed in place, at most one
/// blank line between siblings - and nothing written inside a value, a comment or a text is touched.</summary>
[TestFixture]
public class AumlFormatterTests
{
    private static readonly AumlFormatOptions Markup = new();

    [Test]
    public void SeveralAttributes_GoOnePerLineUnderTheFirst()
    {
        var formatted = AumlFormatter.Format(
            """<Window xmlns="a" xmlns:x="b"><Button Width="120" Height="40" HorizontalAlignment="Left"/></Window>""", Markup);

        Assert.That(formatted, Is.EqualTo(Lines(
            """<Window xmlns="a" """.TrimEnd(),
            """        xmlns:x="b">""",
            """    <Button Width="120" """.TrimEnd(),
            """            Height="40" """.TrimEnd(),
            """            HorizontalAlignment="Left"/>""",
            "</Window>")));
    }

    [Test]
    public void ASingleAttribute_StaysOnTheTagLine() =>
        Assert.That(AumlFormatter.Format("<Grid>\n<Border\n     Margin=\"4\" />\n</Grid>", Markup),
            Is.EqualTo(Lines("<Grid>", "    <Border Margin=\"4\"/>", "</Grid>")));

    [Test]
    public void ASetter_KeepsItsAttributesOnOneLine() =>
        Assert.That(AumlFormatter.Format("<Style>\n<Setter Property=\"Width\"\n Value=\"10\"/>\n</Style>", Markup),
            Is.EqualTo(Lines("<Style>", "    <Setter Property=\"Width\" Value=\"10\"/>", "</Style>")));

    [Test]
    public void APhraseOfALanguageFile_KeepsItsAttributesOnOneLine()
    {
        const string text = "<Language Code=\"en\">\n<Phrase Key=\"Save\"\n        Text=\"Save\"/>\n</Language>";

        Assert.That(AumlFormatter.Format(text, new AumlFormatOptions(IsLanguageFile: true)),
            Is.EqualTo(Lines("<Language Code=\"en\">", "    <Phrase Key=\"Save\" Text=\"Save\"/>", "</Language>")));
        Assert.That(AumlFormatter.Format(text, Markup), Does.Contain("Key=\"Save\"\n"), "in markup a Phrase is an element like any");
    }

    [TestCase("<Grid></Grid>")]
    [TestCase("<Grid>\n    \n</Grid>")]
    public void AnEmptyElement_IsClosedInPlace(string text) =>
        Assert.That(AumlFormatter.Format(text, Markup), Is.EqualTo("<Grid/>"));

    [TestCase("<TextBlock>  Hello   <Run Text=\"x\"/>  world</TextBlock>")]
    [TestCase("<Run> </Run>")]
    [TestCase("<Phrase Key=\"a\">Line one\n  line two</Phrase>")]
    public void TheContentOfAnElementHoldingText_IsKeptAsWritten(string text) =>
        Assert.That(AumlFormatter.Format(text, Markup), Is.EqualTo(text));

    [Test]
    public void ValuesAndCommentsAreKeptAsWritten()
    {
        const string comment = "<!-- first line\n         second line -->";
        const string value = "Data=\"M 0 0\n      L 10 10\"";
        var formatted = AumlFormatter.Format($"<Grid>\n    {comment}\n<Path {value}/>\n</Grid>", Markup);

        Assert.That(formatted, Is.EqualTo(Lines("<Grid>", "    " + comment, $"    <Path {value}/>", "</Grid>")));
    }

    [Test]
    public void ABlankLine_IsKept_ButNeverMoreThanOne() =>
        Assert.That(AumlFormatter.Format("<Grid>\n\n\n<Border/>\n\n\n\n<Border/>\n<Border/>\n\n</Grid>", Markup),
            Is.EqualTo(Lines("<Grid>", "", "    <Border/>", "", "    <Border/>", "    <Border/>", "", "</Grid>")));

    [Test]
    public void AMovedComment_MovesWhole() =>
        Assert.That(AumlFormatter.Format("<Grid>\n<!-- one\n     two -->\n<StackPanel>\n<!-- three\n     four -->\n</StackPanel>\n</Grid>", Markup),
            Is.EqualTo(Lines("<Grid>", "    <!-- one", "         two -->", "    <StackPanel>", "        <!-- three", "             four -->",
                "    </StackPanel>", "</Grid>")));

    [Test]
    public void TheDeclarationAndTheFinalNewLine_AreKept() =>
        Assert.That(AumlFormatter.Format("<?xml version=\"1.0\" encoding=\"utf-8\" ?>\n  <Grid>  </Grid>\n", Markup),
            Is.EqualTo("<?xml version=\"1.0\" encoding=\"utf-8\" ?>\n<Grid>  </Grid>\n"));

    [Test]
    public void WindowsLineEndings_AreKept() =>
        Assert.That(AumlFormatter.Format("<Grid>\r\n<Border/>\r\n</Grid>\r\n", Markup), Is.EqualTo("<Grid>\r\n    <Border/>\r\n</Grid>\r\n"));

    [Test]
    public void TheIndentIsTheEditors() =>
        Assert.That(AumlFormatter.Format("<Grid><Border/></Grid>", new AumlFormatOptions(2)), Is.EqualTo(Lines("<Grid>", "  <Border/>", "</Grid>")));

    [TestCase("<Grid><Border></Grid>")]
    [TestCase("<Grid Width=\"4></Grid>")]
    [TestCase("<Grid>")]
    public void TextThatIsNotWellFormed_IsLeftAlone(string text) =>
        Assert.That(AumlFormatter.Format(text, Markup), Is.Null);

    [Test]
    public void FormattingTwice_ChangesNothingMore()
    {
        var once = AumlFormatter.Format(
            "<Window xmlns=\"a\" xmlns:x=\"b\">\n\n<Grid Width=\"1\" Height=\"2\"><!-- c --><Setter Property=\"A\" Value=\"B\"/>" +
            "<TextBlock>t</TextBlock></Grid></Window>\n", Markup);

        Assert.That(AumlFormatter.Format(once, Markup), Is.EqualTo(once));
    }

    [Test]
    public void ARange_LaysOutOnlyTheElementsWrittenInIt()
    {
        const string text = "<Grid>\n<Border  Width=\"1\" Height=\"2\"/>\n      <Border  Width=\"3\" Height=\"4\"/>\n</Grid>";
        var second = text.IndexOf("<Border  Width=\"3\"", System.StringComparison.Ordinal);

        var edits = AumlFormatter.FormatRange(text, second, second + 5, Markup);

        Assert.That(edits, Has.Count.EqualTo(1));
        var result = text[..edits[0].Start] + edits[0].NewText + text[edits[0].End..];
        Assert.That(result, Is.EqualTo(
            "<Grid>\n<Border  Width=\"1\" Height=\"2\"/>\n    <Border Width=\"3\"\n            Height=\"4\"/>\n</Grid>"));
    }

    [Test]
    public void ARangeInsideAnElementsContent_DoesNotTakeTheElementItself()
    {
        const string text = "<Grid  Width=\"1\" Height=\"2\">\n<Border/>\n</Grid>";
        var border = text.IndexOf("<Border", System.StringComparison.Ordinal);

        var edits = AumlFormatter.FormatRange(text, border, border + 3, Markup);

        Assert.That(edits.Select(e => e.NewText), Is.EqualTo(new[] { "    <Border/>" }));
    }

    private static string Lines(params string[] lines) => string.Join("\n", lines);
}
