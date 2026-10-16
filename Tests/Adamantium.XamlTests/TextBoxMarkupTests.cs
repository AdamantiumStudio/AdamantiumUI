using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core.Markup;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A text box's options written in markup: the build compiles them, and the designer's loader sets them.</summary>
[TestFixture]
public class TextBoxMarkupTests
{
    private const string Namespaces =
        "xmlns=\"http://adamantium/ui\" xmlns:x=\"http://adamantium/ui/xaml/extensions\"";

    [Test]
    public void CharacterCasing_Compiles()
    {
        var errors = AumlCodegenHarness.Compile(
            "<Window " + Namespaces + "><TextBox CharacterCasing=\"Upper\"/></Window>");

        Assert.That(errors, Is.Empty, AumlCodegenHarness.Errors(errors));
    }

    [Test]
    public void CharacterCasing_IsSetByTheDesignersLoader()
    {
        var result = AumlLoader.Load("<TextBox " + Namespaces + " CharacterCasing=\"Lower\"/>");

        Assert.That(result.Diagnostics, Is.Empty, string.Join(" | ", result.Diagnostics));
        Assert.That(((TextBox)result.Root).CharacterCasing, Is.EqualTo(CharacterCasing.Lower));
    }
}
