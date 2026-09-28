using System.Linq;
using NUnit.Framework;

namespace Adamantium.XamlTests;

// x:DataType must resolve at build time; binding path members are not checked, since MVVM-generated members are
// invisible to this generator.
[TestFixture]
public class AumlDataTypeTests
{
    private static string Template(string dataType) =>
        AumlCodegenHarness.WindowHeader + "><ItemsControl><ItemsControl.ItemTemplate>" +
        $"<DataTemplate x:DataType=\"{dataType}\"><TextBlock Text=\"x\"/></DataTemplate>" +
        "</ItemsControl.ItemTemplate></ItemsControl></Window>";

    [Test]
    public void ATypeThatResolvesIsAccepted()
    {
        AumlCodegenHarness.Generate(Template("TextBlock"), out var errors);

        Assert.That(errors, Is.Empty, AumlCodegenHarness.Errors(errors));
    }

    [Test]
    public void TheMarkupExtensionFormIsAcceptedToo()
    {
        AumlCodegenHarness.Generate(Template("{x:Type TextBlock}"), out var errors);

        Assert.That(errors, Is.Empty, AumlCodegenHarness.Errors(errors));
    }

    [Test]
    public void ATypeThatDoesNotResolveFailsTheBuild()
    {
        AumlCodegenHarness.Generate(Template("NoSuchModel"), out var errors);

        Assert.That(errors.Select(e => e.GetMessage()), Has.Some.Contains("x:DataType"),
            string.Join(" | ", errors.Select(e => e.GetMessage())));
    }

    // The template still builds: declaring the type is metadata for tooling, not something the generator consumes.
    [Test]
    public void TheTemplateIsStillGenerated()
    {
        var code = AumlCodegenHarness.Generate(Template("TextBlock"), out _);

        Assert.That(code, Does.Contain("DataTemplate"), "the directive must not swallow the template it sits on");
    }
}
