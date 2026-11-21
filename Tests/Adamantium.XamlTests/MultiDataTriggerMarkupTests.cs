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
