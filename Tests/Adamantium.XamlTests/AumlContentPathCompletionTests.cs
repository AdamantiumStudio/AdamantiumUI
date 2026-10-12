using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Adamantium.UI.LanguageServer;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A picture of a content project the application references is offered where the build puts it:
/// <c>Content/{Name}/...</c>.</summary>
[TestFixture]
public class AumlContentPathCompletionTests
{
    private const string Root = """<Window xmlns="http://adamantium/ui">""";

    private CompletionEngine _engine;
    private string _folder;

    [OneTimeSetUp]
    public void BuildModel()
    {
        _folder = Path.Combine(Path.GetTempPath(), $"aumlcontent-{Guid.NewGuid():N}");
        var app = Path.Combine(_folder, "Game.App");
        var content = Path.Combine(_folder, "Game.Content");
        Directory.CreateDirectory(app);
        Directory.CreateDirectory(Path.Combine(content, "Textures"));
        File.WriteAllText(Path.Combine(app, "Game.App.csproj"),
            """<Project><ItemGroup><ProjectReference Include="..\Game.Content\Game.Content.csproj" /></ItemGroup></Project>""");
        File.WriteAllText(Path.Combine(content, "Game.Content.csproj"),
            """
            <Project>
              <PropertyGroup><AdamantiumContentName>ShipContent</AdamantiumContentName></PropertyGroup>
              <ItemGroup><PackageReference Include="Adamantium.Content.Build" Version="1.0.0" /></ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(content, "Textures", "hull.png"), "");
        File.WriteAllText(Path.Combine(content, "notes.txt"), "");

        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dll in Directory.GetFiles(AppContext.BaseDirectory, "*.dll"))
        {
            byName[Path.GetFileName(dll)] = dll;
        }

        foreach (var dll in Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"))
        {
            byName.TryAdd(Path.GetFileName(dll), dll);
        }

        _engine = new CompletionEngine(AumlTypeModel.Build(byName.Values, []));
    }

    [OneTimeTearDown]
    public void RemoveProjects()
    {
        Directory.Delete(_folder, true);
    }

    [Test]
    public void TheProjectsRoot_OffersTheContentFolder() =>
        Assert.That(Labels("""<Image Source="|"/>"""), Is.EquivalentTo(new[] { "Content/" }));

    [Test]
    public void TheContentFolder_OffersTheContentProjectsByName() =>
        Assert.That(Labels("""<Image Source="Content/|"/>"""), Is.EquivalentTo(new[] { "ShipContent/" }));

    [Test]
    public void AContentProject_OffersItsPicturesAndTheFoldersHoldingThem() =>
        Assert.That(Labels("""<Image Source="Content/ShipContent/|"/>"""), Is.EquivalentTo(new[] { "Textures/" }));

    [Test]
    public void AFolderOfAContentProject_OffersItsPictures() =>
        Assert.That(Labels("""<Image Source="Content/ShipContent/Textures/|"/>"""), Is.EquivalentTo(new[] { "hull.png" }));

    private IReadOnlyList<string> Labels(string body)
    {
        var marked = Root + body + "</Window>";
        var caret = marked.IndexOf('|');
        var text = marked.Remove(caret, 1);
        return _engine.Complete(text, caret, Path.Combine(_folder, "Game.App", "Main.auml")).Select(i => i.Label).ToList();
    }
}
