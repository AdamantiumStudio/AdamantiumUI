using System.Linq;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core.Markup;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A value that starts with "{}" is the text after it, braces and all, not a markup extension - in the build
/// and in the preview alike, as in WPF.</summary>
[TestFixture]
public class EscapedBraceValueTests
{
    [TestCase("{}{", "{")]
    [TestCase("{}{Hello}", "{Hello}")]
    [TestCase("{}", "")]
    public void AnEscapedValue_IsTheTextAfterTheEscape(string written, string text)
    {
        var markup = AumlCodegenHarness.WindowHeader + $"><TextBlock Text=\"{written}\"/></Window>";
        AumlCodegenHarness.Generate(markup, out var generatorErrors);
        var compileErrors = AumlCodegenHarness.Compile(markup);
        var preview = AumlLoader.Load($"<Grid xmlns=\"http://adamantium/ui\"><TextBlock Text=\"{written}\"/></Grid>");

        Assert.Multiple(() =>
        {
            Assert.That(generatorErrors.Concat(compileErrors).Select(e => e.GetMessage()), Is.Empty);
            Assert.That(preview.Diagnostics, Is.Empty);
            Assert.That(((TextBlock)((Grid)preview.Root).Children[0]).Text, Is.EqualTo(text));
        });
    }
}
