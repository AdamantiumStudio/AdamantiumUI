using System.IO;
using Adamantium.UI.Automation.Cli;
using NUnit.Framework;

namespace Adamantium.UI.Sandbox.Tests;

/// <summary>adam-auto talks, by default, on a pipe of the working copy it was built in: two copies driven at once
/// never reach each other's instances.</summary>
[TestFixture]
public class AdamAutoPipeTests
{
    private static readonly string Root = Path.GetPathRoot(Path.GetTempPath());

    [Test]
    public void EveryProjectOfOneCopyTakesTheSamePipe()
    {
        var sandbox = Path.Combine(Root, "Work", "CopyA", "artifacts", "bin", "Adamantium.UI.Sandbox", "Release", "net10.0");
        var cli = Path.Combine(Root, "Work", "CopyA", "artifacts", "bin", "Adamantium.UI.Automation.Cli", "Debug", "net10.0");

        Assert.That(Program.DefaultPipe(cli), Is.EqualTo(Program.DefaultPipe(sandbox)));
        Assert.That(Program.DefaultPipe(sandbox), Does.StartWith("adam-auto-"));
    }

    [Test]
    public void TwoCopiesTakeTwoPipes()
    {
        var first = Path.Combine(Root, "Work", "CopyA", "artifacts", "bin", "Adamantium.UI.Sandbox", "Release", "net10.0");
        var second = Path.Combine(Root, "Work", "CopyB", "artifacts", "bin", "Adamantium.UI.Sandbox", "Release", "net10.0");

        Assert.That(Program.DefaultPipe(first), Is.Not.EqualTo(Program.DefaultPipe(second)));
    }

    [Test]
    public void AToolOutsideAnyCopyTakesThePipeOfItsFolder()
    {
        var tools = Path.Combine(Root, "home", "someone", ".dotnet", "tools");

        Assert.That(Program.DefaultPipe(tools), Is.EqualTo(Program.DefaultPipe(tools + Path.DirectorySeparatorChar)));
        Assert.That(Program.DefaultPipe(tools), Is.Not.EqualTo(Program.DefaultPipe(Path.Combine(tools, "other"))));
    }
}
