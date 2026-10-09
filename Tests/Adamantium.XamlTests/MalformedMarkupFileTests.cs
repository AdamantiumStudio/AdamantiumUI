using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A markup file that is not well-formed XML - a prefix nobody declared - is reported against that file, and
/// every other file of the project is still generated. It used to stop the generator outright: no class from any file,
/// the build reporting each of them missing and the real cause only as a warning.</summary>
[TestFixture]
public class MalformedMarkupFileTests
{
    [Test]
    public void ABrokenFile_IsReportedByName_AndTheOthersAreStillGenerated()
    {
        var project = AumlCodegenHarness.Project(new Dictionary<string, string>
        {
            ["Views/Good.auml"] = "<View xmlns=\"http://adamantium/ui\" xmlns:x=\"http://adamantium/ui/xaml/extensions\">" +
                                  "<TextBlock x:Name=\"Label\" Text=\"Fine\"/></View>",
            ["Views/Broken.auml"] = "<View xmlns=\"http://adamantium/ui\"><TextBlock x:Name=\"Label\" Text=\"Oops\"/></View>",
        });

        var messages = project.GeneratorDiagnostics.Select(d => d.GetMessage()).ToList();
        Assert.That(project.GeneratorDiagnostics.Select(d => d.Id), Has.None.EqualTo("CS8785"), string.Join(" | ", messages));
        Assert.That(project.GeneratorDiagnostics.Where(d => d.GetMessage().Contains("'x'")).Select(d => d.Location.GetLineSpan().Path),
            Has.Some.EndsWith("Broken.auml"));
        Assert.That(project.Compilation.SyntaxTrees.Select(t => t.FilePath), Has.Some.Contains("Good"));
        Assert.That(project.Compilation.SyntaxTrees.Select(t => t.FilePath), Has.None.Contains("Broken"));
    }
}
