using System;
using System.IO;
using Adamantium.UI.LanguageServer;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>The editor underlines a blueprint in a project that already holds one - whatever either file is called -
/// with the build's own message, while the file is written.</summary>
[TestFixture]
public class ApplicationBlueprintCheckTests
{
    private const string Blueprint = """<ApplicationBlueprint xmlns="http://adamantium/ui" StartupTheme="Fluent"/>""";

    private string _project;

    [SetUp]
    public void CreateProject()
    {
        _project = Path.Combine(Path.GetTempPath(), $"aumlblueprint-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_project, "Setup"));
        File.WriteAllText(Path.Combine(_project, "Probe.csproj"), "<Project/>");
        File.WriteAllText(Path.Combine(_project, "MainWindow.auml"), """<Window xmlns="http://adamantium/ui"/>""");
    }

    [TearDown]
    public void RemoveProject() => Directory.Delete(_project, true);

    [Test]
    public void TheProjectsOnlyBlueprint_IsNotFlagged()
    {
        var path = Write("AppBlueprint.auml", Blueprint);

        Assert.That(ApplicationBlueprintCheck.Check(path, Blueprint), Is.Null);
    }

    [Test]
    public void ASecondBlueprint_IsFlaggedOnItsRoot_NamingBoth()
    {
        Write("AppBlueprint.auml", Blueprint);
        var text = "<?xml version=\"1.0\"?>\n<!-- <Window> -->\n" + Blueprint;
        var path = Write("Setup/Startup.auml", text);

        var problem = ApplicationBlueprintCheck.Check(path, text);

        Assert.That(problem?.Message, Is.EqualTo("An application has one blueprint, and this project holds 2: AppBlueprint.auml, Setup/Startup.auml."));
        Assert.That((problem.Line, problem.Character, problem.Length), Is.EqualTo((2, 1, "ApplicationBlueprint".Length)));
    }

    [Test]
    public void ABlueprintBeingWrittenBeforeItIsSaved_IsCountedToo()
    {
        Write("AppBlueprint.auml", Blueprint);
        var path = Write("Startup.auml", """<Window xmlns="http://adamantium/ui"/>""");

        Assert.That(ApplicationBlueprintCheck.Check(path, Blueprint), Is.Not.Null, "the editor's text, not the saved file, says what it is");
    }

    [Test]
    public void AFileThatIsNoBlueprint_IsNotFlagged()
    {
        Write("AppBlueprint.auml", Blueprint);
        Write("Startup.auml", Blueprint);
        var path = Path.Combine(_project, "MainWindow.auml");

        Assert.That(ApplicationBlueprintCheck.Check(path, File.ReadAllText(path)), Is.Null);
    }

    private string Write(string name, string text)
    {
        var path = Path.Combine(_project, name);
        File.WriteAllText(path, text);
        return path;
    }
}
