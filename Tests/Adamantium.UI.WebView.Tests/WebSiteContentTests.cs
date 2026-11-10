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
    public void APathLeavingTheFolder_OrNamingNoFile_NamesNothing(string path)
    {
        Assert.That(new WebSiteFolder(folder).Open(path), Is.Null);
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
