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
}
