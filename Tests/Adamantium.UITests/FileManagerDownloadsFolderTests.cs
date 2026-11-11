using System;
using System.IO;
using Adamantium.UI.Core;
using Adamantium.UI.Platforms.Linux;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>
/// The user's downloads folder: where the desktop keeps it - xdg-user-dirs names it in the user's language under the
/// home folder, read as glib reads it - and "Downloads" in the home folder when the desktop does not say. The reading of
/// user-dirs.dirs is text only, so it runs on every system against that system's home folder.
/// </summary>
[TestFixture]
[NonParallelizable]
public class FileManagerDownloadsFolderTests
{
    private string config;
    private string previous;

    [SetUp]
    public void MakeConfig()
    {
        config = Path.Combine(Path.GetTempPath(), "AdamantiumUserDirs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(config);
        previous = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", config);
    }

    [TearDown]
    public void DeleteConfig()
    {
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previous);
        try
        {
            Directory.Delete(config, true);
        }
        catch (IOException)
        {
        }
    }

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private string Read(string dirs)
    {
        File.WriteAllText(Path.Combine(config, "user-dirs.dirs"), dirs);
        return new DesktopFileManager().DownloadsFolder;
    }

    [Test]
    public void UserDirs_NameIt_UnderTheHomeFolder_InTheUsersLanguage() =>
        Assert.That(Read("# written by xdg-user-dirs-update\nXDG_DESKTOP_DIR=\"$HOME/Рабочий стол\"\n" +
                         "XDG_DOWNLOAD_DIR=\"$HOME/Загрузки\"\n"), Is.EqualTo(Path.Combine(Home, "Загрузки")));

    [TestCase("XDG_DOWNLOAD_DIR=\"/data/dl\"", "/data/dl", TestName = "a full path")]
    [TestCase("XDG_DOWNLOAD_DIR = \"/data/dl\"", "/data/dl", TestName = "spaces round the equals sign")]
    [TestCase("XDG_DOWNLOAD_DIR=\"/data/dl\" # moved", "/data/dl", TestName = "a comment after it")]
    [TestCase("XDG_DOWNLOAD_DIR=\"/data/My \\\"DL\\\"\"", "/data/My \"DL\"", TestName = "escaped quotes")]
    [TestCase("XDG_DOWNLOAD_DIR=\"/data/a\\$b\"", "/data/a$b", TestName = "an escaped dollar")]
    [TestCase("XDG_DOWNLOAD_DIR=\"/first\"\nXDG_DOWNLOAD_DIR=\"/last\"", "/last", TestName = "the last assignment")]
    public void UserDirs_AreReadAsGlibReadsThem(string dirs, string expected) =>
        Assert.That(Read(dirs), Is.EqualTo(expected));

    [TestCase("#XDG_DOWNLOAD_DIR=\"/data/dl\"", TestName = "a commented line")]
    [TestCase("XDG_DOWNLOAD_DIR=\"relative/dl\"", TestName = "a relative path")]
    [TestCase("XDG_DOWNLOAD_DIR=\"$HOMEdl\"", TestName = "$HOME run into a name")]
    [TestCase("XDG_DOWNLOAD_DIR=\"/data/dl", TestName = "an unclosed quote")]
    [TestCase("XDG_DOWNLOAD_DIR=/data/dl", TestName = "no quotes")]
    [TestCase("XDG_DESKTOP_DIR=\"$HOME/Desktop\"", TestName = "no downloads line")]
    public void UserDirs_ThatSayNoPath_SayNothing(string dirs) => Assert.That(Read(dirs), Is.Null);

    [TestCase("XDG_DOWNLOAD_DIR=\"$HOME\"")]
    [TestCase("XDG_DOWNLOAD_DIR=\"$HOME/\"")]
    public void UserDirs_SetToTheHomeFolder_GiveTheHomeFolder(string dirs) =>
        Assert.That(Read(dirs), Is.EqualTo(Home), "the user keeps no downloads folder of their own");

    [Test]
    public void NoUserDirs_SayNothing() => Assert.That(new DesktopFileManager().DownloadsFolder, Is.Null);

    [Test]
    public void ARelativeConfigHome_IsIgnored()
    {
        File.WriteAllText(Path.Combine(config, "user-dirs.dirs"), "XDG_DOWNLOAD_DIR=\"/data/dl\"\n");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", "relative-config");

        Assert.That(new DesktopFileManager().DownloadsFolder, Is.Not.EqualTo("/data/dl"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("relative/Downloads")]
    public void ADesktopThatSaysNoFullPath_GivesDownloadsInTheHomeFolder(string said)
    {
        var platform = FileManager.Platform;
        try
        {
            FileManager.Platform = new SayingPlatform(said);
            Assert.That(FileManager.DownloadsFolder, Is.EqualTo(Path.GetFullPath(Path.Combine(Home, "Downloads"))));
        }
        finally
        {
            FileManager.Platform = platform;
        }
    }

    [Test]
    public void ADesktopThatSaysAFullPath_GivesIt()
    {
        var platform = FileManager.Platform;
        var folder = Path.Combine(Path.GetTempPath(), "SomeDownloads");
        try
        {
            FileManager.Platform = new SayingPlatform(folder);
            Assert.That(FileManager.DownloadsFolder, Is.EqualTo(folder));
        }
        finally
        {
            FileManager.Platform = platform;
        }
    }

    [Test, Platform("Win")]
    public void OnWindows_TheShellsFolder_IsAFullPath()
    {
        var folder = new Adamantium.UI.Platforms.Windows.ExplorerFileManager().DownloadsFolder;
        Assert.That(folder == null || Path.IsPathFullyQualified(folder), Is.True, folder);
    }

    private sealed class SayingPlatform(string folder) : IFileManagerPlatform
    {
        public bool ShowItem(string path) => false;

        public bool OpenItem(string path) => false;

        public string DownloadsFolder => folder;
    }
}
