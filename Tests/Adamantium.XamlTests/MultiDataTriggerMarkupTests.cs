using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core.Markup;
using Adamantium.UI.Core.Media;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A MultiDataTrigger written in markup: built and compiled by the generator, and built by the designer's
/// loader, its conditions' bindings read against the host's data.</summary>
[TestFixture]
public class MultiDataTriggerMarkupTests
{
    private const string Body =
        "<Border Classes=\"Pair\"><Border.Styles><Style Selector=\"Border.Pair\"><MultiDataTrigger>" +
        "<MultiDataTrigger.Conditions>" +
        "<Condition Binding=\"{Binding Kind}\" Value=\"Fire\"/><Condition Binding=\"{Binding IsOn}\" Value=\"true\"/>" +
        "</MultiDataTrigger.Conditions>" +
        "<Setter Property=\"Background\" Value=\"Red\"/></MultiDataTrigger></Style></Border.Styles></Border>";

    // A condition of the wrong kind would leave the trigger never holding without a word: the build says it.
    [TestCase("MultiDataTrigger", "<Condition Property=\"IsEnabled\" Value=\"true\"/>", "MultiDataTrigger's Condition tests a Binding")]
    [TestCase("MultiTrigger", "<Condition Binding=\"{Binding IsOn}\" Value=\"true\"/>", "MultiTrigger's Condition tests a Property")]
    public void AConditionOfTheWrongKind_IsWarnedOf(string trigger, string condition, string warning)
    {
        var warnings = AumlCodegenHarness.Warnings(AumlCodegenHarness.WindowHeader + "><Border Classes=\"Pair\"><Border.Styles>" +
            $"<Style Selector=\"Border.Pair\"><{trigger}><{trigger}.Conditions>{condition}</{trigger}.Conditions>" +
            $"<Setter Property=\"Background\" Value=\"Red\"/></{trigger}></Style></Border.Styles></Border></Window>");

        Assert.That(warnings, Has.Some.Contains(warning), string.Join(" | ", warnings));
    }

    [Test]
    public void ARightCondition_IsNotWarnedOf()
    {
        var warnings = AumlCodegenHarness.Warnings(AumlCodegenHarness.WindowHeader + ">" + Body + "</Window>");

        Assert.That(warnings, Has.None.Contains("Condition tests"), string.Join(" | ", warnings));
    }

    [Test]
    public void TheGeneratedCode_Compiles()
    {
        var errors = AumlCodegenHarness.Compile(AumlCodegenHarness.WindowHeader + ">" + Body + "</Window>");

        Assert.That(errors, Is.Empty, string.Join(" | ", errors.Select(d => d.ToString())));
    }

    [Test]
    public void TheLoader_BuildsIt_AndItHoldsOnlyWhileBothMatch()
    {
        var result = AumlLoader.Load(Body.Replace("<Border ",
            "<Border xmlns=\"http://adamantium/ui\" xmlns:x=\"http://adamantium/ui/xaml/extensions\" "));
        var border = (Border)result.Root;
        Assert.That(border, Is.Not.Null, string.Join(" | ", result.Diagnostics));

        border.DataContext = new { Kind = "Fire", IsOn = false };
        var oneOfTwo = (border.Background as SolidColorBrush)?.Color == Colors.Red;
        border.DataContext = new { Kind = "Fire", IsOn = true };
        var both = (border.Background as SolidColorBrush)?.Color == Colors.Red;

        Assert.Multiple(() =>
        {
            Assert.That(oneOfTwo, Is.False);
            Assert.That(both, Is.True);
        });
    }

    // A condition on a property of an ancestor - {Ancestor} is a binding as {Binding} is - next to one on the data.
    private const string OnAnAncestor =
        "<StackPanel Width=\"42\"><Border Classes=\"Pair\"><Border.Styles><Style Selector=\"Border.Pair\"><MultiDataTrigger>" +
        "<MultiDataTrigger.Conditions>" +
        "<Condition Binding=\"{Binding IsOn}\" Value=\"true\"/><Condition Binding=\"{Ancestor StackPanel, Width}\" Value=\"42\"/>" +
        "</MultiDataTrigger.Conditions>" +
        "<Setter Property=\"Background\" Value=\"Red\"/></MultiDataTrigger></Style></Border.Styles></Border></StackPanel>";

    // {Ancestor} and {Self} go wherever a binding does: a trigger's condition, a DataTrigger, a MultiBinding's input.
    [Test]
    public void AncestorAndSelf_AsBindings_Compile()
    {
        var errors = AumlCodegenHarness.Compile(AumlCodegenHarness.WindowHeader + "><StackPanel>" + OnAnAncestor +
            "<Border Classes=\"Wide\"><Border.Styles><Style Selector=\"Border.Wide\">" +
            "<DataTrigger Binding=\"{Self Width}\" Value=\"42\"><Setter Property=\"Background\" Value=\"Red\"/></DataTrigger>" +
            "</Style></Border.Styles></Border>" +
            "<TextBlock Text=\"{MultiBinding {Binding Name}, {Ancestor StackPanel, Width}, {Self Height}}\"/>" +
            "</StackPanel></Window>");

        Assert.That(errors, Is.Empty, string.Join(" | ", errors.Select(d => d.ToString())));
    }

    [Test]
    public void TheLoader_FeedsAMultiBinding_FromAnAncestorAndTheElementItself()
    {
        var result = AumlLoader.Load("<StackPanel xmlns=\"http://adamantium/ui\" Width=\"42\">" +
            "<Border Height=\"3\" Tag=\"{MultiBinding {Ancestor StackPanel, Width}, {Self Height}}\"/></StackPanel>");
        var panel = (Adamantium.UI.Controls.Panels.StackPanel)result.Root;
        Assert.That(panel, Is.Not.Null, string.Join(" | ", result.Diagnostics));

        Assert.That(((Border)panel.Children[0]).Tag, Is.EqualTo(new object[] { 42d, 3d }));
    }

    [Test]
    public void TheLoader_BuildsAConditionOnAnAncestor_AndItHoldsWithTheData()
    {
        var result = AumlLoader.Load(OnAnAncestor.Replace("<StackPanel ",
            "<StackPanel xmlns=\"http://adamantium/ui\" xmlns:x=\"http://adamantium/ui/xaml/extensions\" "));
        var panel = (Adamantium.UI.Controls.Panels.StackPanel)result.Root;
        Assert.That(panel, Is.Not.Null, string.Join(" | ", result.Diagnostics));
        var border = (Border)panel.Children[0];

        border.DataContext = new { IsOn = false };
        var dataOff = (border.Background as SolidColorBrush)?.Color == Colors.Red;
        border.DataContext = new { IsOn = true };
        var both = (border.Background as SolidColorBrush)?.Color == Colors.Red;

        Assert.Multiple(() =>
        {
            Assert.That(dataOff, Is.False);
            Assert.That(both, Is.True);
        });
    }
}
