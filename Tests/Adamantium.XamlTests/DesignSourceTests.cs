using System.Text.RegularExpressions;
using Adamantium.UI.Core;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>The designer takes a click on an element back to its markup. Elements of a nested view are built by that
/// view's generated code, which had no way to say where they came from - so a click inside a gallery tab could only
/// select the whole gallery.</summary>
[TestFixture]
public class DesignSourceTests
{
    [Test]
    public void AViewsElements_TellTheDesignerTheirFileAndLine()
    {
        var auml = AumlCodegenHarness.WindowHeader + ">\n<StackPanel>\n    <Button Content=\"A\"/>\n</StackPanel>\n</Window>";

        var code = AumlCodegenHarness.Generate(auml, out var errors);

        Assert.That(errors, Is.Empty, AumlCodegenHarness.Errors(errors));
        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Match(@"Design\.Source\(this, ""C:\\\\Test\\\\MainWindow\.auml"", 1, \d+\)"), "the root");
            Assert.That(code, Does.Match(@"Design\.Source\(\w+, ""C:\\\\Test\\\\MainWindow\.auml"", 3, \d+\)"), "the button, on its own line");
            Assert.That(Regex.Matches(code, "#if DEBUG").Count, Is.EqualTo(Regex.Matches(code, @"Design\.Source\(").Count),
                "every one only in a debug build: the path is the build machine's");
        });
    }

    [Test]
    public void AStyleSet_TellsNothing()
    {
        const string auml = "<StyleSet xmlns=\"http://adamantium/ui\" xmlns:x=\"http://adamantium/ui/xaml/extensions\">" +
                            "<Style Selector=\"Button\"><Setter Property=\"Width\" Value=\"10\"/></Style></StyleSet>";

        var code = AumlCodegenHarness.Generate(auml, out var errors);

        Assert.That(errors, Is.Empty, AumlCodegenHarness.Errors(errors));
        Assert.That(code, Does.Not.Contain("Design.Source("), "a theme's parts are not what a click on a view means");
    }

    [Test]
    public void ASourceIsKept_OnlyInTheDesigner()
    {
        var wasDesignMode = Design.IsDesignMode;
        try
        {
            var running = new object();
            Design.IsDesignMode = false;
            Design.Source(running, "a.auml", 1, 2);

            var previewed = new object();
            Design.IsDesignMode = true;
            Design.Source(previewed, "a.auml", 3, 4);

            Assert.Multiple(() =>
            {
                Assert.That(Design.SourceOf(running), Is.Null);
                Assert.That(Design.SourceOf(previewed), Is.EqualTo(new DesignSource("a.auml", 3, 4)));
            });
        }
        finally
        {
            Design.IsDesignMode = wasDesignMode;
        }
    }
}
