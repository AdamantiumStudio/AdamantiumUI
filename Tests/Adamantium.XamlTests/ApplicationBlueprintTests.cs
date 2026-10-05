using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Adamantium.Core;
using Adamantium.UI.ApplicationModel;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A project's application blueprint becomes a class, the attribute that names it to the assembly - where the
/// application finds it while it initializes - and the entry point that runs the application. A project holds one
/// blueprint and one application class, and writes no entry point of its own.</summary>
[TestFixture]
public class ApplicationBlueprintTests
{
    private const string Namespaces = "xmlns=\"http://adamantium/ui\" xmlns:x=\"http://adamantium/ui/xaml/extensions\"";

    private const string Application = """
        namespace Test.App;

        public class App : Adamantium.UI.Universes.MultiverseApplication { }
        """;

    private const string Blueprint =
        "<ApplicationBlueprint " + Namespaces +
        " StartupWindow=\"MainWindow\" StartupTheme=\"EditorPro\" StartupThemeVariant=\"Light\" StartupLanguage=\"en\"" +
        " ShutDownMode=\"OnLastWindowClosed\">" +
        "<ApplicationBlueprint.Resources><ResourceLink Source=\"Icons\"/></ApplicationBlueprint.Resources>" +
        "<ApplicationBlueprint.StyleIncludes><StyleInclude Source=\"Buttons\"/></ApplicationBlueprint.StyleIncludes>" +
        "</ApplicationBlueprint>";

    private static Dictionary<string, string> Files(params (string Name, string Markup)[] extra)
    {
        var files = new Dictionary<string, string>
        {
            ["MainWindow.auml"] = $"<Window {Namespaces}><Grid/></Window>",
            ["Icons.auml"] = $"<ResourceDictionary {Namespaces}/>",
            ["Buttons.auml"] = $"<StyleSet {Namespaces}/>",
        };
        foreach (var (name, markup) in extra)
        {
            files[name] = markup;
        }

        return files;
    }

    [Test]
    public void TheBlueprint_IsNamedToTheAssembly_AndRunsTheApplication()
    {
        var project = AumlCodegenHarness.Project(Files(("AppBlueprint.auml", Blueprint)), sources: [Application]);
        Assert.That(project.Errors, Is.Empty, string.Join(" | ", project.Errors.Select(e => e.GetMessage())));

        var assembly = project.Load();
        Assert.That(assembly.GetCustomAttribute<ApplicationBlueprintAttribute>()?.BlueprintType,
            Is.EqualTo(assembly.GetType("Test.App.AppBlueprint")));
        Assert.That(project.Source, Does.Contain("new global::Test.App.App().Run();"));
        Assert.That(project.Source, Does.Contain("[global::System.STAThread]"));
    }

    [Test]
    public void TheBlueprint_HoldsWhatItsMarkupSays()
    {
        var assembly = AumlCodegenHarness.Project(Files(("AppBlueprint.auml", Blueprint)), sources: [Application]).Load();

        var blueprint = (IApplicationBlueprint)Activator.CreateInstance(assembly.GetType("Test.App.AppBlueprint"));

        Assert.Multiple(() =>
        {
            Assert.That(blueprint.StartupWindow, Is.EqualTo(assembly.GetType("Test.App.MainWindow")));
            Assert.That(blueprint.StartupTheme, Is.EqualTo(typeof(Adamantium.UI.Themes.EditorProTheme.EditorPro)));
            Assert.That(blueprint.StartupThemeVariant, Is.EqualTo(ThemeVariant.Light));
            Assert.That(blueprint.StartupLanguage, Is.EqualTo("en"));
            Assert.That(blueprint.ShutDownMode, Is.EqualTo(ShutDownMode.OnLastWindowClosed));
            Assert.That(blueprint.Resources.Includes.Single().Source, Is.EqualTo(assembly.GetType("Test.App.Icons")));
            Assert.That(blueprint.StyleIncludes.Single().Source, Is.EqualTo(assembly.GetType("Test.App.Buttons")));
        });
    }

    [Test]
    public void ABlueprintThatSaysNothing_SetsNothing()
    {
        var assembly = AumlCodegenHarness.Project(Files(("AppBlueprint.auml", $"<ApplicationBlueprint {Namespaces}/>")),
            sources: [Application]).Load();

        var blueprint = (IApplicationBlueprint)Activator.CreateInstance(assembly.GetType("Test.App.AppBlueprint"));

        Assert.Multiple(() =>
        {
            Assert.That(blueprint.ShutDownMode, Is.Null, "an unset value must not reach the application as if the markup had said it");
            Assert.That(blueprint.StartupWindow, Is.Null);
            Assert.That(blueprint.StartupTheme, Is.Null);
            Assert.That(blueprint.StartupThemeVariant, Is.Null);
            Assert.That(blueprint.StartupLanguage, Is.Null);
        });
    }

    [Test]
    public void ASecondBlueprint_FailsTheBuild_WhateverItsName() =>
        Assert.That(Errors(Files(("AppBlueprint.auml", Blueprint), ("Other/Startup.auml", Blueprint)), [Application]),
            Has.Some.Contains("An application has one blueprint, and this project holds 2: ").And.Some.Contains("Other/Startup.auml"));

    [Test]
    public void AProjectWithNoApplicationClass_FailsTheBuild() =>
        Assert.That(Errors(Files(("AppBlueprint.auml", Blueprint)), []),
            Has.Some.Contains("the project declares none: add a class deriving from MultiverseApplication"));

    [Test]
    public void AProjectWithTwoApplicationClasses_FailsTheBuild() =>
        Assert.That(Errors(Files(("AppBlueprint.auml", Blueprint)),
                [Application, "namespace Test.App; public class Other : Adamantium.UI.Universes.MultiverseApplication { }"]),
            Has.Some.Contains("starts one application, and the project declares 2"));

    [Test]
    public void AnEntryPointWrittenByHand_FailsTheBuild() =>
        Assert.That(Errors(Files(("AppBlueprint.auml", Blueprint)),
                [Application, "namespace Test.App; public static class Program { public static void Main() { } }"]),
            Has.Some.Contains("remove Test.App.Program.Main"));

    [Test]
    public void AStartupWindowThatIsNoWindow_FailsTheBuild() =>
        Assert.That(Errors(Files(("AppBlueprint.auml", $"<ApplicationBlueprint {Namespaces} StartupWindow=\"Icons\"/>")), [Application]),
            Has.Some.Contains("Icons is not a Window: ApplicationBlueprint.StartupWindow"));

    [Test]
    public void AStartupThemeThatIsNoTheme_FailsTheBuild() =>
        Assert.That(Errors(Files(("AppBlueprint.auml", $"<ApplicationBlueprint {Namespaces} StartupTheme=\"Icons\"/>")), [Application]),
            Has.Some.Contains("Icons is not a Theme: ApplicationBlueprint.StartupTheme"));

    [Test]
    public void AProjectWithNoBlueprint_GetsNoEntryPoint() =>
        Assert.That(AumlCodegenHarness.Project(Files(), sources: [Application]).Source, Does.Not.Contain("ApplicationBlueprintAttribute"));

    private static List<string> Errors(Dictionary<string, string> files, string[] sources) =>
        AumlCodegenHarness.Project(files, sources: sources).GeneratorDiagnostics
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(d => d.GetMessage())
            .ToList();
}
