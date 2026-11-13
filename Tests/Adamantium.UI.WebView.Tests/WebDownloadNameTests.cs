using System;
using System.IO;
using NUnit.Framework;

namespace Adamantium.UI.WebView.Tests;

/// <summary>
/// The name a download is saved under, made from the one the server suggests: a device's name before any dot of it is
/// no file on Windows, however many extensions follow.
/// </summary>
[TestFixture]
public class WebDownloadNameTests
{
    private string folder;

    [SetUp]
    public void MakeFolder()
    {
        folder = Path.Combine(Path.GetTempPath(), "adamantium-download-names-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
    }

    [TearDown]
    public void DeleteFolder() => Directory.Delete(folder, true);

    [TestCase("CON.txt", "_CON.txt")]
    [TestCase("NUL.tar.gz", "_NUL.tar.gz")]
    [TestCase("aux .log", "_aux .log")]
    [TestCase("COM¹.bin", "_COM¹.bin")]
    [TestCase("Console.txt", "Console.txt")]
    [TestCase("COM10.txt", "COM10.txt")]
    public void ADevicesName_IsNotSavedAsItIs(string suggested, string saved)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("only Windows keeps names for devices");
        }

        var download = new WebDownload(0, null, null, null);
        download.Suggest(suggested);

        Assert.That(Path.GetFileName(download.FreePathIn(folder)), Is.EqualTo(saved));
    }
}
