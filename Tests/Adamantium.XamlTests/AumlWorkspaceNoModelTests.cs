using System;
using System.IO;
using System.Threading;
using Adamantium.UI.LanguageServer;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A file the language server has no type model for says why - no project above it, or a project not built yet
/// - instead of offering nothing in silence; the project's first build is noticed and open files are checked again.</summary>
[TestFixture]
public class AumlWorkspaceNoModelTests
{
    private string _root;

    [SetUp]
    public void CreateRoot()
    {
        _root = Path.Combine(Path.GetTempPath(), $"aumlnomodel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void RemoveRoot() => Directory.Delete(_root, true);

    [Test]
    public void AFileInNoProject_SaysSo()
    {
        using var workspace = new AumlWorkspace();

        Assert.That(workspace.WhyNoModel(Path.Combine(_root, "Loose.auml")), Does.Contain("belongs to no project"));
    }

    [Test]
    public void AProjectNotBuilt_SaysSo_UntilItIs()
    {
        File.WriteAllText(Path.Combine(_root, "Probe.csproj"), "<Project/>");
        var file = Path.Combine(_root, "MainWindow.auml");
        using var workspace = new AumlWorkspace();

        Assert.That(workspace.WhyNoModel(file), Is.EqualTo(
            "Probe has not been built yet: completion, checks and type colors start once it has been built."));

        var output = Path.Combine(_root, "bin", "Debug", "net10.0");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "Probe.dll"), string.Empty);

        Assert.That(workspace.WhyNoModel(file), Is.Null);
    }

    [Test]
    public void AProjectsFirstBuild_ChecksTheOpenFilesAgain()
    {
        File.WriteAllText(Path.Combine(_root, "Probe.csproj"), "<Project/>");
        using var workspace = new AumlWorkspace();
        using var changed = new ManualResetEventSlim();
        workspace.ModelsChanged += changed.Set;
        Assert.That(workspace.GetModelForFile(Path.Combine(_root, "MainWindow.auml")), Is.Null);

        var output = Path.Combine(_root, "bin", "Debug", "net10.0");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "Probe.dll"), string.Empty);

        Assert.That(changed.Wait(TimeSpan.FromSeconds(10)), Is.True, "the build was not noticed");
    }
}
