using System.IO;
using System.Linq;
using Adamantium.UI.LanguageServer;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>
/// A scenario in the editor: completion from its schema - the steps a place takes, a step's attributes, an attribute's
/// values - and, in a selector path, its keys and the AutomationIds the markup of the referenced projects declares; the
/// build's problems at their place; what a step does on hover.
/// </summary>
[TestFixture]
public class ScenarioFileAssistTests
{
    private const string Header = "<Scenario xmlns=\"http://adamantium/automation/scenario\">\n";

    private string _root;
    private string _scenario;

    [SetUp]
    public void Projects()
    {
        _root = Path.Combine(Path.GetTempPath(), $"scenario-assist-{TestContext.CurrentContext.Test.ID}");
        Directory.CreateDirectory(Path.Combine(_root, "App", "Views"));
        Directory.CreateDirectory(Path.Combine(_root, "App.Tests", "Scenarios"));
        File.WriteAllText(Path.Combine(_root, "App", "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(Path.Combine(_root, "App", "Views", "MainView.auml"),
            "<View x:Class=\"App.Views.MainView\" xmlns=\"http://adamantium/ui\" xmlns:x=\"http://adamantium/ui/xaml/extensions\">" +
            "<Button AutomationProperties.AutomationId=\"Save\"/><TextBox x:Name=\"Notes\"/></View>");
        File.WriteAllText(Path.Combine(_root, "App.Tests", "App.Tests.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"..\\App\\App.csproj\" /></ItemGroup></Project>");
        _scenario = Path.Combine(_root, "App.Tests", "Scenarios", "Save.ascenario");
    }

    [TearDown]
    public void Clean() => Directory.Delete(_root, true);

    private string[] Labels(string text) =>
        ScenarioFileAssist.Complete(_scenario, text, text.Length, _ => null, null).Select(item => item.Label).ToArray();

    [Test]
    public void AStep_IsOfferedWithItsRequiredAttributes_AndInATouchesOnlyAFinger()
    {
        var steps = ScenarioFileAssist.Complete(_scenario, Header + "  <", Header.Length + 3, _ => null, null);

        Assert.Multiple(() =>
        {
            Assert.That(steps.Select(item => item.Label), Does.Contain("Invoke").And.Contain("Expect").And.Contain("WaitIdle"));
            Assert.That(steps.Single(item => item.Label == "Drop").InsertText, Is.EqualTo("Drop Target=\"$1\" Onto=\"$2\" />$0"));
            Assert.That(Labels(Header + "  <Touches Gesture=\"Tap\">\n    <"), Is.EqualTo(new[] { "Finger" }));
        });
    }

    [Test]
    public void AStepsAttributes_ComeRequiredFirst_LeavingOutThoseWritten()
    {
        var attributes = Labels(Header + "  <Pen Target=\"id=Canvas\" ");

        Assert.Multiple(() =>
        {
            Assert.That(attributes[0], Is.EqualTo("Gesture"));
            Assert.That(attributes, Does.Contain("Pressure").And.Contain("Barrel").And.Not.Contain("Target"));
        });
    }

    [Test]
    public void AnAttributesValues_AreItsChoices()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Labels(Header + "  <Pen Gesture=\""), Is.EqualTo(new[] { "Tap", "Hold", "Drag" }));
            Assert.That(Labels(Header + "  <Pen Barrel=\""), Is.EqualTo(new[] { "true", "false" }));
            Assert.That(Labels(Header + "  <Dock Position=\"F"), Is.EqualTo(new[] { "Fill" }));
        });
    }

    [Test]
    public void ASelectorPath_OffersItsKeys_ThenTheIdsTheMarkupDeclares()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Labels(Header + "  <Invoke Target=\""), Is.EqualTo(new[] { "id=", "name=", "type=", "class=" }));
            Assert.That(Labels(Header + "  <Invoke Target=\"id=MainView/"), Does.Contain("parent").And.Contain("id="));
            Assert.That(Labels(Header + "  <Invoke Target=\"id=MainView/id="), Is.EqualTo(new[] { "MainView", "Notes", "Save" }));
            Assert.That(Labels(Header + "  <Invoke Target=\"id=S"), Is.EqualTo(new[] { "Save" }));
        });
    }

    [Test]
    public void TheBuildsProblems_AreShownWhereTheBuildPutsThem()
    {
        var problem = ScenarioFileAssist.Diagnostics(_scenario, Header + "  <Invoke Target=\"id=Save/\" />\n</Scenario>").Single();

        Assert.That((problem.Line, problem.Character, problem.Code), Is.EqualTo((1, 10, "AUS004")));
    }

    [Test]
    public void AStep_SaysWhatItDoes_OnHover()
    {
        var text = Header + "  <Invoke Target=\"id=Save\" />\n</Scenario>";

        Assert.Multiple(() =>
        {
            Assert.That(ScenarioFileAssist.Hover(text, text.IndexOf("Invoke") + 2), Does.StartWith("Invokes the element"));
            Assert.That(ScenarioFileAssist.Hover(text, text.IndexOf("Target") + 2), Is.EqualTo("The path to the element."));
        });
    }
}
