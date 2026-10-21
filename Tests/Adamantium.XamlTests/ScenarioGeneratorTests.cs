using System.Collections.Generic;
using System.Linq;
using Adamantium.UI.Generators.Scenarios;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>
/// A scenario file becomes an NUnit test on the automation library: each step a call of the typed API with the line it
/// was written on, and a file the schema refuses, a broken selector path or a step that cannot run a build error at its
/// line.
/// </summary>
[TestFixture]
public class ScenarioGeneratorTests
{
    private const string Header = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<Scenario xmlns=\"http://adamantium/automation/scenario\"";

    private static ScenarioFile Read(string steps, string attributes = "", string file = "Save") =>
        ScenarioTranslator.Read(AumlCodegenHarness.PathOf($"Scenarios/{file}.ascenario"), $"{Header}{attributes}>\n{steps}\n</Scenario>");

    private static GeneratedProject Generate(IReadOnlyDictionary<string, string> files, string application = "App") =>
        AumlCodegenHarness.Project(files, application == null ? null : new Dictionary<string, string> { ["AdamantiumScenarioApplication"] = application });

    [Test]
    public void AStep_CallsTheTypedApi_WithTheLineItWasWrittenOn()
    {
        var scenario = Read("  <Invoke Target=\"id=Save\" />\n  <Expect Target=\"id=Status\" Name=\"Saved.\" ScaleX=\"1\" />");

        Assert.Multiple(() =>
        {
            Assert.That(scenario.Problems, Is.Empty);
            Assert.That(scenario.Steps[0], Is.EqualTo(
                $"#line 3 \"{AumlCodegenHarness.Literal(AumlCodegenHarness.PathOf("Scenarios/Save.ascenario"))}\"\n" +
                "await session.Find(global::Adamantium.UI.Automation.By.Id(\"Save\")).InvokeAsync();"));
            Assert.That(scenario.Steps[1], Does.StartWith("#line 4 ")
                .And.Contain("Expectation(\"name\", \"Saved.\")")
                .And.Contain("Expectation(\"ScaleX\", \"1\")"));
        });
    }

    [Test]
    public void ASelectorPath_IsWrittenAsItsSteps_AtBuild()
    {
        var scenario = Read("  <Invoke Target='id=Shell>type=Button,name=\"Cut, out\"[-1]/next' />");

        Assert.That(scenario.Steps[0], Does.EndWith(
            "await session.Find(global::Adamantium.UI.Automation.By.Id(\"Shell\"))" +
            ".Child(global::Adamantium.UI.Automation.By.Type(global::Adamantium.UI.Core.Automation.AutomationControlType.Button)" +
            ".And(global::Adamantium.UI.Automation.By.Name(\"Cut, out\")).At(-1)).Next().InvokeAsync();"));
    }

    [Test]
    public void AStepThatMayLeaveErrors_AllowsThemForItselfAlone()
    {
        var scenario = Read("  <Invoke Target=\"id=Save\" AllowErrors=\"true\" />");

        Assert.That(scenario.Steps[0].Split('\n').Where(line => !line.StartsWith("#line")),
            Is.EqualTo(new[] { "session.AllowErrors = true;", "await session.Find(global::Adamantium.UI.Automation.By.Id(\"Save\")).InvokeAsync();", "session.AllowErrors = false;" }));
    }

    [Test]
    public void APenDrawnHarderAlongTheStroke_PressesFromOneToTheOther()
    {
        var scenario = Read("  <Pen Target=\"id=Canvas\" Gesture=\"Drag\" From=\"400,150\" To=\"700,150\" Pressure=\"0.2..1\" />");

        Assert.That(scenario.Steps[0], Does.Contain(".PenDragAsync(400, 150, 700, 150, 0.2f, 1f, barrel: false, eraser: false, throughSystem: false)"));
    }

    [Test]
    public void TheName_IsTheFilesUnlessGiven_AndHasToBeAnIdentifier()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Read("  <WaitIdle />").Name, Is.EqualTo("Save"));
            Assert.That(Read("  <WaitIdle />", " Name=\"SaveAs\"").Name, Is.EqualTo("SaveAs"));
            Assert.That(Read("  <WaitIdle />", file: "Save as").Problems.Single().Id, Is.EqualTo("AUS003"));
            Assert.That(Read("  <WaitIdle />", file: "class").Problems.Single().Id, Is.EqualTo("AUS003"));
            Assert.That(Read("  <WaitIdle />", " Name=\"ToString\"").Problems.Single().Id, Is.EqualTo("AUS003"));
        });
    }

    [Test]
    public void AFileOutsideTheScenarioNamespace_IsRefused_NotSkipped()
    {
        var scenario = ScenarioTranslator.Read(AumlCodegenHarness.PathOf("Scenarios/Save.ascenario"), "<Scenario>\n  <Set Target=\"id=Notes\" />\n</Scenario>");

        Assert.That(scenario.Problems.Select(problem => (problem.Id, problem.Line)), Is.EqualTo(new[] { ("AUS002", 1) }));
    }

    [TestCase("  <Invoke Target=\"id=Save\" />\n  <Press Target=\"id=Save\" />", "AUS002", 4)]
    [TestCase("  <Move Target=\"id=Save\" By=\"1,a\" />", "AUS002", 3)]
    [TestCase("  <Invoke Target=\"id=Save\" />\n  <Invoke Target=\"id=Save/\" />", "AUS004", 4)]
    [TestCase("  <Touch Target=\"id=Canvas\" Gesture=\"Drag\" />", "AUS005", 3)]
    [TestCase("  <Touch Target=\"id=Canvas\" Gesture=\"Tap\" From=\"1,1\" />", "AUS005", 3)]
    [TestCase("  <Pen Target=\"id=Canvas\" Gesture=\"Hold\" Barrel=\"true\" />", "AUS006", 3)]
    [TestCase("  <Expect Target=\"id=Status\" />", "AUS007", 3)]
    [TestCase("  <Invoke Target=\"id=Save\">", "AUS001", 4)]
    [TestCase("  <Zoom Target=\"id=Canvas\" Percent=\"INF\" />", "AUS002", 3)]
    [TestCase("  <Caret Target=\"id=Notes\" At=\"99999999999\" />", "AUS002", 3)]
    [TestCase("  <Expect Target=\"id=Status\" Name=\"Saved.\" Timeout=\"5s\" />", "AUS005", 3)]
    [TestCase("  <Wait Target=\"id=Status\" Name=\"Saved.\" AllowErrors=\"true\" />", "AUS005", 3)]
    [TestCase("  <Touches Gesture=\"Tap\" By=\"0,80\"><Finger Target=\"id=A\" /></Touches>", "AUS005", 3)]
    public void WhatCannotRun_IsAProblemAtItsLine(string steps, string id, int line)
    {
        var problem = Read(steps).Problems.First();

        Assert.That((problem.Id, problem.Line), Is.EqualTo((id, line)), problem.Message);
    }

    [Test]
    public void AScenario_BecomesATestOfTheScenariosFixture_ThatCompiles()
    {
        var project = Generate(new Dictionary<string, string>
        {
            ["Scenarios/Save.ascenario"] = $"{Header} Description=\"Saving says so.\">\n  <Invoke Target=\"id=Save\" />\n</Scenario>",
            ["Scenarios/SaveBySystem.ascenario"] = $"{Header} Explicit=\"Real input.\">\n  <Invoke Target=\"id=Save\" />\n</Scenario>"
        });

        Assert.Multiple(() =>
        {
            Assert.That(project.Errors, Is.Empty, AumlCodegenHarness.Errors(project.Errors));
            Assert.That(project.Source, Does.Contain("[global::NUnit.Framework.Category(\"Scenario\")]")
                .And.Contain("[global::NUnit.Framework.Description(\"Saving says so.\")]")
                .And.Contain("public async global::System.Threading.Tasks.Task Save()")
                .And.Contain("AutomationSession.LaunchAsync(")
                .And.Contain("GetEnvironmentVariable(\"ADAM_SCENARIO_PIPE\")")
                .And.Contain("AutomationSession.AttachAsync(running)")
                .And.Contain("session.CloseWindowsOpenedFromNowAsync()")                .And.Contain("? \"App.exe\" : \"App\"")
                .And.Contain("FreshData = true")
                .And.Contain("[global::NUnit.Framework.Explicit(\"Real input.\")]"));
        });
    }

    [Test]
    public void AControlTypeThatIsNone_IsACompileError_AtTheScenariosLine()
    {
        var project = Generate(new Dictionary<string, string>
        {
            ["Scenarios/Save.ascenario"] = $"{Header}>\n  <WaitIdle />\n  <Invoke Target=\"type=Buton\" />\n</Scenario>"
        });

        var error = project.Errors.Single();
        var where = error.Location.GetMappedLineSpan();
        Assert.That((where.Path, where.StartLinePosition.Line + 1), Is.EqualTo((AumlCodegenHarness.PathOf("Scenarios/Save.ascenario"), 4)), error.ToString());
    }

    [Test]
    public void TwoScenariosOfOneName_AndNoApplication_AreBuildErrors()
    {
        var twice = new Dictionary<string, string>
        {
            ["Scenarios/Save.ascenario"] = $"{Header}>\n  <WaitIdle />\n</Scenario>",
            ["Scenarios/Other.ascenario"] = $"{Header} Name=\"Save\">\n  <WaitIdle />\n</Scenario>"
        };

        Assert.Multiple(() =>
        {
            Assert.That(Generate(twice).GeneratorDiagnostics.Select(d => d.Id), Is.EqualTo(new[] { "AUS008" }));
            Assert.That(Generate(twice, null).GeneratorDiagnostics.Select(d => d.Id), Is.EqualTo(new[] { "AUS009" }));
        });
    }
}
