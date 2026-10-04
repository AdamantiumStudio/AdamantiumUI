using System.Linq;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>
/// A resource named on a component's property used to go through the property system by name - and a plain CLR property,
/// or an attached one named without its owner, is not found there, so the value was dropped without a word. A one-shot
/// {ResourceReference} on a plain property is now resolved and assigned at once; an attached property is named with its
/// owner; a LIVE resource on a plain property has nothing to keep live, and the build says so. A setter's value is not
/// such a property: it keeps the live resource for the style to apply, in a style written in a view as in a style set.
/// </summary>
[TestFixture]
public class ResourceOnPlainPropertyTests
{
    private const string LiveInAStyleOfAView =
        AumlCodegenHarness.WindowHeader + "><ListBox><ListBox.ItemContainerStyle><Style Selector=\"ListBoxItem\">" +
        "<Setter Property=\"Foreground\" Value=\"{ObservableResource Ink}\"/>" +
        "<PropertyTrigger Property=\"IsSelected\" Value=\"true\">" +
        "<Setter Property=\"BorderBrush\" Value=\"{ThemeResource AccentFillColorDefault}\"/>" +
        "</PropertyTrigger></Style></ListBox.ItemContainerStyle></ListBox></Window>";

    private const string OneShotOnPlainProperty =
        AumlCodegenHarness.WindowHeader + "><ContextMenu HorizontalOffset=\"{ResourceReference Gap}\"/></Window>";

    private const string OnAttachedProperty =
        AumlCodegenHarness.WindowHeader + "><Grid><Border Grid.Row=\"{ResourceReference Row}\"/></Grid></Window>";

    private const string LiveOnPlainProperty =
        AumlCodegenHarness.WindowHeader + "><ContextMenu HorizontalOffset=\"{ThemeResource Gap}\"/></Window>";

    [Test]
    public void AOneShotResource_OnAPlainProperty_IsAssignedAtOnce()
    {
        var code = AumlCodegenHarness.Generate(OneShotOnPlainProperty, out var errors);

        Assert.That(errors, Is.Empty, AumlCodegenHarness.Errors(errors));
        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain(".HorizontalOffset = "));
            Assert.That(code, Does.Contain("ResolveNow(\"Gap\")"));
            Assert.That(code, Does.Not.Contain("SetDeferred"), "nothing to defer into");
        });
    }

    [Test]
    public void AResource_OnAnAttachedProperty_NamesItsOwner()
    {
        var code = AumlCodegenHarness.Generate(OnAttachedProperty, out var errors);

        Assert.That(errors, Is.Empty, AumlCodegenHarness.Errors(errors));
        Assert.That(code, Does.Contain("SetDeferred(").And.Contain("\"Grid.Row\""));
    }

    [Test]
    public void ALiveResource_OnAPlainProperty_FailsTheBuild()
    {
        AumlCodegenHarness.Generate(LiveOnPlainProperty, out var errors);

        Assert.That(errors.Select(e => e.GetMessage()), Has.Some.Contains("ContextMenu.HorizontalOffset"));
    }

    [Test]
    public void ALiveResource_InAStyleWrittenInAView_IsTheSettersValue()
    {
        var code = AumlCodegenHarness.Generate(LiveInAStyleOfAView, out var errors);

        Assert.That(errors, Is.Empty, AumlCodegenHarness.Errors(errors));
        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("ThemeResource(\"AccentFillColorDefault\")"));
            Assert.That(code, Does.Contain("ObservableResource(\"Ink\")"));
            Assert.That(code, Does.Not.Contain(".Apply("), "the setter keeps the resource; the style applies it");
        });
    }

    [TestCase(OneShotOnPlainProperty)]
    [TestCase(OnAttachedProperty)]
    [TestCase(LiveInAStyleOfAView)]
    public void AndTheGeneratedCodeCompiles(string auml)
    {
        var errors = AumlCodegenHarness.Compile(auml);

        Assert.That(errors, Is.Empty,
            "generated code did not compile: " + string.Join(" | ", errors.Select(d => d.ToString())));
    }
}
