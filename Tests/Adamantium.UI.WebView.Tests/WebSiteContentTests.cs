using System;
using System.IO;
using NUnit.Framework;

namespace Adamantium.UI.WebView.Tests;

[TestFixture]
public class WebSiteContentTests
{
    private string folder;

    [SetUp]
    public void MakeSite()
    {
        var parent = Path.Combine(Path.GetTempPath(), "AdamantiumSiteTests", Guid.NewGuid().ToString("N"));
        folder = Path.Combine(parent, "site");
        Directory.CreateDirectory(Path.Combine(folder, "css"));
        File.WriteAllText(Path.Combine(folder, "index.html"), "root");
        File.WriteAllText(Path.Combine(folder, "css", "site.css"), "css");
        File.WriteAllText(Path.Combine(parent, "secret.txt"), "secret");
    }

    [TearDown]
    public void RemoveSite() => Directory.Delete(Path.GetDirectoryName(folder), true);

    [TestCase("", "root")]
    [TestCase("/", "root")]
    [TestCase("index.html", "root")]
    [TestCase("css/site.css", "css")]
    [TestCase("css/../index.html", "root")]
    public void AFolder_ServesTheFileAPathNames_AFoldersPathItsIndex(string path, string expected)
    {
        using var file = new WebSiteFolder(folder).Open(path);
        Assert.That(file == null ? null : new StreamReader(file).ReadToEnd(), Is.EqualTo(expected));
    }

    [TestCase("../secret.txt")]
    [TestCase("css/../../secret.txt")]
    [TestCase("..\\secret.txt")]
    [TestCase("/../secret.txt")]
    [TestCase("nothing.txt")]
    [TestCase("index.html\0")]
    [TestCase("index.html:Zone.Identifier")]
    [TestCase("index.html::$DATA")]
    [TestCase("index.html.")]
    [TestCase("index.html ")]
    [TestCase("css")]
    public void APathLeavingTheFolder_OrNamingNoFileAsItIs_NamesNothing(string path)
    {
        Assert.That(new WebSiteFolder(folder).Open(path), Is.Null);
    }

    [Test]
    public void AFolderAtTheRootOfADrive_ServesItsFiles()
    {
        var root = Path.GetPathRoot(folder);
        var relative = Path.GetRelativePath(root, Path.Combine(folder, "index.html")).Replace('\\', '/');
        using var file = new WebSiteFolder(root).Open(relative);
        Assert.That(file, Is.Not.Null);
    }

    // A link inside the folder could lead anywhere: a path through one names nothing.
    [Test]
    public void APathThroughALink_NamesNothing()
    {
        var outside = Path.Combine(Path.GetDirectoryName(folder), "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "data.txt"), "outside");
        var link = Path.Combine(folder, "lib");
        if (OperatingSystem.IsWindows())
        {
            // A junction, which needs no privilege a symbolic link would.
            using var made = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe",
                $"/c mklink /J \"{link}\" \"{outside}\"") { CreateNoWindow = true, UseShellExecute = false })!;
            made.WaitForExit();
        }
        else
        {
            Directory.CreateSymbolicLink(link, outside);
        }

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(Path.Combine(link, "data.txt")), Is.True, "the link leads out");
                Assert.That(new WebSiteFolder(folder).Open("lib/data.txt"), Is.Null);
            });
        }
        finally
        {
            // The link alone, not what it leads to.
            Directory.Delete(link);
        }
    }

    [TestCase("")]
    [TestCase("index.html")]
    public void ResourcesBuiltIn_ServeTheFileAPathNames(string path)
    {
        using var file = new WebSiteResources(typeof(WebSiteContentTests).Assembly, "Site").Open(path);
        Assert.That(file == null ? null : new StreamReader(file).ReadToEnd(), Does.Contain("<title>built in</title>"));
    }

    [Test]
    public void AResourceThereIsNoneOf_NamesNothing()
    {
        Assert.That(new WebSiteResources(typeof(WebSiteContentTests).Assembly, "Site").Open("nothing.html"), Is.Null);
    }
}
