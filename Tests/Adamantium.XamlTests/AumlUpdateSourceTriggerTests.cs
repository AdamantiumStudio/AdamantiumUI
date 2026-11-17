using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary><c>UpdateSourceTrigger</c> on a {Binding}, an {Ancestor} or a {Self} is the binding's: written in markup, it
/// reaches the binding.</summary>
[TestFixture]
public class AumlUpdateSourceTriggerTests
{
    [TestCase("{Binding Name, Mode=TwoWay, UpdateSourceTrigger=LostFocus}")]
    [TestCase("{Ancestor Window, Title, Mode=TwoWay, UpdateSourceTrigger=LostFocus}")]
    [TestCase("{Self Name, Mode=TwoWay, UpdateSourceTrigger=LostFocus}")]
    public void ItIsSetOnTheBinding_AndTheCodeCompiles(string binding)
    {
        var view = AumlCodegenHarness.WindowHeader + $"><TextBox Text=\"{binding}\"/></Window>";

        var code = AumlCodegenHarness.Generate(view, out var errors);

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Empty, AumlCodegenHarness.Errors(errors));
            Assert.That(AumlCodegenHarness.Warnings(view), Is.Empty);
            Assert.That(code, Does.Contain("UpdateSourceTrigger = global::Adamantium.UI.Core.Data.UpdateSourceTrigger.LostFocus"));
            Assert.That(AumlCodegenHarness.Compile(view), Is.Empty);
        });
    }
}
