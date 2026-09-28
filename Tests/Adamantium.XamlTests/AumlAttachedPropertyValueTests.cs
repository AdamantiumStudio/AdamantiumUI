using NUnit.Framework;

namespace Adamantium.XamlTests;

// Every value form, not only literals (e.g. {x:Static}), reaches an attached property's Owner.SetName(target, value).
[TestFixture]
public class AumlAttachedPropertyValueTests
{
    // A real attached property with a real static to feed it: Grid.Column, and a public const int on a real type.
    private const string Header =
        AumlCodegenHarness.WindowHeader + "xmlns:panels=\"clr-namespace:Adamantium.UI.Controls.Panels;assembly=Adamantium.UI.Controls\">";

    // The control: a LITERAL on an attached property has always worked, and states what the emitted call looks like.
    [Test]
    public void ALiteralReachesTheAttachedSetter()
    {
        var code = AumlCodegenHarness.Generate(
            Header + "<Grid><Border Grid.Column=\"1\"/></Grid></Window>", out var errors);

        Assert.That(errors, Is.Empty, AumlCodegenHarness.Errors(errors));
        Assert.That(code, Does.Contain("SetColumn"), "an attached property is set through its Set method");
    }

    /// <summary>...and so must a value that is not a literal. Before the fix this generated no SetColumn at all.</summary>
    [Test]
    public void AStaticMemberReachesTheAttachedSetterToo()
    {
        var code = AumlCodegenHarness.Generate(
            Header + "<Grid><Border Grid.Column=\"{x:Static panels:VirtualizingPanel.MinBindsPerPassDefault}\"/></Grid></Window>",
            out var errors);

        Assert.That(errors, Is.Empty, AumlCodegenHarness.Errors(errors));
        Assert.That(code, Does.Contain("SetColumn"),
            "the attached setter was never called - the value went nowhere and the property kept its default\n" + code);
        Assert.That(code, Does.Contain("MinBindsPerPassDefault"), "and it has to be the member, read where it is used\n" + code);
    }
}
