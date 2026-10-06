using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adamantium.UI.LanguageServer;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>The editor finds no fault the build does not find: every markup file of the repository builds, so none may
/// carry an error the language server gives with the build's own code. Run over the built projects, the way the server
/// itself sees them.</summary>
[TestFixture]
[Explicit("Builds a type model for every project of the repository: minutes, not seconds.")]
[Category("MarkupSweep")]
public class MarkupConsistencyTests
{
    private const string BuildCode = "AUM001";

    [Test]
    public void TheEditorFlagsNothingTheBuildAccepts()
    {
        var root = RepositoryRoot();
        using var workspace = new AumlWorkspace();
        var findings = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.auml", SearchOption.AllDirectories).Where(IsSource))
        {
            if (workspace.GetModelForFile(file) is not { } model)
            {
                findings.Add($"{Path.GetRelativePath(root, file)}: no type model ({workspace.WhyNoModel(file)})");
                continue;
            }

            foreach (var diagnostic in AumlValidator.Validate(File.ReadAllText(file), model).Where(d => d.Code == BuildCode))
            {
                findings.Add($"{Path.GetRelativePath(root, file)}({diagnostic.Line + 1},{diagnostic.Character + 1}): {diagnostic.Message}");
            }
        }

        Assert.That(findings, Is.Empty, string.Join(Environment.NewLine, findings));
    }

    private static bool IsSource(string file) =>
        InAProject(file)
        && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
        && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
        && !file.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}")
        && !file.Contains("Adamantium.UI.Templates");

    private static bool InAProject(string file)
    {
        for (var dir = new DirectoryInfo(Path.GetDirectoryName(file)); dir != null; dir = dir.Parent)
        {
            if (dir.GetFiles("*.csproj").Length > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AdamantiumUI.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("The repository root (AdamantiumUI.sln) is not above the test output.");
    }
}
