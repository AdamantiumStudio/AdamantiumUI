using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A project that sets <c>AdamantiumRequireAutomationId</c> is told of every control a test acts on that has
/// nothing to be found by - neither an <c>AutomationId</c> nor an <c>x:Name</c>. Controls in templates repeat and are left
/// out. Without the property nothing is said.</summary>
[TestFixture]
public class AutomationIdCheckTests
{
    private const string View =
        "<View xmlns=\"http://adamantium/ui\" xmlns:x=\"http://adamantium/ui/xaml/extensions\"><StackPanel>" +
        "<Button Content=\"Save\"/>" +
        "<Button AutomationProperties.AutomationId=\"Open\" Content=\"Open\"/>" +
        "<TextBox x:Name=\"Query\"/>" +
        "<TextBlock Text=\"Not acted on\"/>" +
        "<ListBox AutomationProperties.AutomationId=\"Items\"><ListBox.ItemTemplate><DataTemplate>" +
        "<Button Content=\"In a template\"/></DataTemplate></ListBox.ItemTemplate></ListBox>" +
        "</StackPanel></View>";

    private static List<string> Findings(bool required)
    {
        var project = AumlCodegenHarness.Project(
            new Dictionary<string, string> { ["Views/Form.auml"] = View },
            required ? new Dictionary<string, string> { ["AdamantiumRequireAutomationId"] = "true" } : null);
        return [.. project.GeneratorDiagnostics.Where(d => d.Id == "AUI011").Select(d => d.GetMessage())];
    }

    [Test]
    public void WhenRequired_OnlyTheControlWithNothingToBeFoundBy_IsReported()
    {
        var findings = Findings(required: true);

        Assert.That(findings, Has.Count.EqualTo(1));
        Assert.That(findings[0], Does.Contain("Views/Form.auml").And.Contain("Button has no AutomationProperties.AutomationId"));
    }

    [Test]
    public void WhenNotRequired_NothingIsSaid()
    {
        Assert.That(Findings(required: false), Is.Empty);
    }
}
