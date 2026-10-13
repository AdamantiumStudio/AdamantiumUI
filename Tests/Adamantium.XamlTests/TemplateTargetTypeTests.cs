using System.Linq;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A template carries the TargetType its markup names, in the application as in the designer: both build it from
/// one plan.</summary>
[TestFixture]
public class TemplateTargetTypeTests
{
    private const string TemplatedWindow =
        AumlCodegenHarness.WindowHeader + "><Window.Template><ControlTemplate TargetType=\"Window\">" +
        "<Border/></ControlTemplate></Window.Template></Window>";

    [Test]
    public void AControlTemplate_IsGivenItsTargetType()
    {
        var code = AumlCodegenHarness.Generate(TemplatedWindow, out var errors);

        Assert.That(errors, Is.Empty, AumlCodegenHarness.Errors(errors));
        Assert.That(code, Does.Contain(".TargetType = typeof(global::Adamantium.UI.Controls.Window);"));
    }

    [Test]
    public void AndTheGeneratedCodeCompiles()
    {
        var errors = AumlCodegenHarness.Compile(TemplatedWindow);

        Assert.That(errors, Is.Empty,
            "generated code did not compile: " + string.Join(" | ", errors.Select(d => d.ToString())));
    }
}
