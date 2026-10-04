using System.Linq;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>
/// Inside a template a part's values go in at TEMPLATE priority, by name - which only means something where an
/// AdamantiumProperty stands behind the name. A plain CLR property has no slot, and setting it by name dropped the value
/// without a word: a style written inside a template lost its selector and styled nothing. The runtime loader already
/// assigned such a property; the generator now does too.
/// </summary>
[TestFixture]
public class TemplateClrPropertyTests
{
    private const string StyleInTemplate =
        AumlCodegenHarness.WindowHeader + "><Window.Template><ControlTemplate TargetType=\"Window\">" +
        "<ItemsControl x:Name=\"Part\" Width=\"40\"><ItemsControl.ItemContainerStyle>" +
        "<Style Selector=\"ContentPresenter\"/>" +
        "</ItemsControl.ItemContainerStyle></ItemsControl>" +
        "</ControlTemplate></Window.Template></Window>";

    [Test]
    public void APlainPropertyInsideATemplate_IsAssigned()
    {
        var code = AumlCodegenHarness.Generate(StyleInTemplate, out var errors);

        Assert.That(errors, Is.Empty, AumlCodegenHarness.Errors(errors));
        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Not.Contain("SetValue(\"Selector\""), "a name with no property behind it");
            Assert.That(code, Does.Contain(".Selector = "));
            Assert.That(code, Does.Contain("SetValue(\"Width\""), "a part's own property stays the template's");
        });
    }

    [Test]
    public void AndTheGeneratedCodeCompiles()
    {
        var errors = AumlCodegenHarness.Compile(StyleInTemplate);

        Assert.That(errors, Is.Empty,
            "generated code did not compile: " + string.Join(" | ", errors.Select(d => d.ToString())));
    }
}
