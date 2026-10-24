using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary><c>UpdateSourceTrigger</c> on a {Binding} is the binding's: written in markup, it reaches the binding.
/// </summary>
[TestFixture]
public class AumlUpdateSourceTriggerTests
{
    private const string View = AumlCodegenHarness.WindowHeader +
                                "><TextBox Text=\"{Binding Name, Mode=TwoWay, UpdateSourceTrigger=LostFocus}\"/></Window>";

    [Test]
    public void ItIsSetOnTheBinding_AndTheCodeCompiles()
    {
        var code = AumlCodegenHarness.Generate(View, out var errors);

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Empty, AumlCodegenHarness.Errors(errors));
            Assert.That(AumlCodegenHarness.Warnings(View), Is.Empty);
            Assert.That(code, Does.Contain("UpdateSourceTrigger = global::Adamantium.UI.Core.Data.UpdateSourceTrigger.LostFocus"));
            Assert.That(AumlCodegenHarness.Compile(View), Is.Empty);
        });
    }
}
