using System;
using System.Collections.Generic;
using System.IO;
using Adamantium.UI.Core;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>Files from outside - a PDF's attachments, downloads: names made safe to save under, kinds that run code only
/// saved, the system's mark and checks before one is opened.</summary>
[NonParallelizable]
public class UntrustedFilesTests
{
    [TestCase(@"..\..\evil.txt", "evil.txt")]
    [TestCase("../../evil.txt", "evil.txt")]
    [TestCase(@"C:\Windows\System32\drivers\etc\hosts", "hosts")]
    [TestCase("report.pdf", "report.pdf")]
    [TestCase("Отчёт 2026.pdf", "Отчёт 2026.pdf")]
    [TestCase("invoice\u202Efdp.exe", "invoice_fdp.exe")]
    [TestCase("tab\there.txt", "tab_here.txt")]
    [TestCase("..", "attachment")]
    [TestCase("  ", "attachment")]
    [TestCase(null, "attachment")]
    [TestCase("name. . .", "name")]
    public void AName_IsMadeSafe(string name, string safe)
    {
        Assert.That(FileNames.MakeSafe(name, "attachment"), Is.EqualTo(safe));
    }

    [Test, Platform("Win")]
    public void OnWindows_DeviceNamesAndStreams_AreNotKept()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FileNames.MakeSafe("CON.txt", "attachment"), Is.EqualTo("_CON.txt"));
            Assert.That(FileNames.MakeSafe("NUL.tar.gz", "attachment"), Is.EqualTo("_NUL.tar.gz"));
            Assert.That(FileNames.MakeSafe("a.txt:hidden.exe", "attachment"), Is.EqualTo("a.txt_hidden.exe"));
            Assert.That(FileNames.MakeSafe("what?.txt", "attachment"), Is.EqualTo("what_.txt"));
        });
    }

    [Test]
    public void ALongName_KeepsItsExtension_WithinTheLimit()
    {
        var safe = FileNames.MakeSafe(new string('a', 500) + ".pdf", "attachment");

        Assert.Multiple(() =>
        {
            Assert.That(safe, Has.Length.LessThanOrEqualTo(FileNames.Limit).And.Length.GreaterThan(FileNames.Limit - 5));
            Assert.That(safe, Does.EndWith(".pdf"));
        });
    }

    [Test]
    public void ACut_NeverMakesAKindTheNameDidNotEndWith()
    {
        var longExtension = FileNames.MakeSafe("report.exe" + new string(' ', 97) + "pdf", "attachment");
        var longStem = FileNames.MakeSafe(new string('a', 190) + ".exe" + new string(' ', 20) + "x.pdf", "attachment");

        Assert.Multiple(() =>
        {
            Assert.That(Path.GetExtension(longExtension), Is.Not.EqualTo(".exe"));
            Assert.That(Path.GetExtension(longStem), Is.EqualTo(".pdf"));
        });
    }

    [Test]
    public void ACut_NeverSplitsACharacter_NorLeavesASpaceAtTheEnd()
    {
        var emoji = FileNames.MakeSafe(string.Concat(System.Linq.Enumerable.Repeat("😀", 150)) + ".txt", "attachment");
        var spaced = FileNames.MakeSafe("a." + new string('x', 98) + " " + new string('y', 50), "attachment");

        Assert.Multiple(() =>
        {
            Assert.That(emoji, Has.Length.LessThanOrEqualTo(FileNames.Limit));
            Assert.That(emoji, Does.EndWith("😀.txt"), "no half of a character before the extension");
            Assert.That(spaced, Does.Not.EndWith(" "));
        });
    }

    [Test]
    public void InvisibleTagCharacters_AreNotKept()
    {
        Assert.That(FileNames.MakeSafe("a\U000E0041b.txt", "attachment"), Is.EqualTo("a_b.txt"));
    }

    [Test]
    public void ASecondFile_IsNumbered()
    {
        Assert.That(FileNames.Numbered("report.pdf", 2), Is.EqualTo("report (2).pdf"));
    }

    [TestCase("setup.exe", true)]
    [TestCase("run.bat", true)]
    [TestCase("script.ps1", true)]
    [TestCase("installer.msi", true)]
    [TestCase("shortcut.lnk", true)]
    [TestCase("page.url", true)]
    [TestCase("screen.scr", true)]
    [TestCase("macro.js", true)]
    [TestCase("SETUP.EXE", true)]
    [TestCase("report.pdf", false)]
    [TestCase("notes.txt", false)]
    [TestCase("photo.png", false)]
    [TestCase("letter.docx", false)]
    [TestCase("noextension", false)]
    [Platform("Win")]
    public void OnWindows_TheSystemSays_WhatIsDangerousToOpen(string fileName, bool dangerous)
    {
        Assert.That(new Adamantium.UI.Platforms.Windows.ExplorerFileManager().IsDangerousToOpen(fileName),
            Is.EqualTo(dangerous));
    }

    [Test, Platform("Win"), Apartment(System.Threading.ApartmentState.STA)]
    public void OnWindows_AFileMarkedUntrusted_CarriesTheMarkOfTheWeb()
    {
        var folder = Directory.CreateTempSubdirectory("adam-untrusted-").FullName;
        try
        {
            var path = Path.Combine(folder, "notes.txt");
            File.WriteAllText(path, "from a PDF");

            var marked = new Adamantium.UI.Platforms.Windows.ExplorerFileManager().MarkUntrusted(path);

            var zone = path + ":Zone.Identifier";
            Assert.Multiple(() =>
            {
                Assert.That(marked, Is.True);
                Assert.That(File.Exists(zone), Is.True, "the mark Explorer, SmartScreen and Office read");
                Assert.That(File.ReadAllText(zone), Does.Contain("ZoneId=4"), "restricted, as nothing vouches for it");
            });
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Test]
    public void ADangerousFile_IsNeverHandedToThePlatformToOpen()
    {
        var folder = Directory.CreateTempSubdirectory("adam-untrusted-").FullName;
        var platform = FileManager.Platform;
        var recording = new RecordingPlatform();
        try
        {
            var program = Path.Combine(folder, "setup.exe");
            var text = Path.Combine(folder, "notes.txt");
            File.WriteAllText(program, "MZ");
            File.WriteAllText(text, "notes");
            FileManager.Platform = recording;

            var script = Path.Combine(folder, "tool.py");
            File.WriteAllText(script, "print()");

            Assert.Multiple(() =>
            {
                Assert.That(FileManager.OpenUntrustedItem(program, null), Is.False);
                Assert.That(FileManager.OpenUntrustedItem(script, null), Is.False, "Python runs it, whatever the system says");
                Assert.That(FileManager.OpenUntrustedItem(text, null), Is.True);
                Assert.That(FileManager.OpenUntrustedItem(Path.Combine(folder, "gone.txt"), null), Is.False);
                if (OperatingSystem.IsWindows())
                {
                    Assert.That(FileManager.OpenUntrustedItem(program + ".", null), Is.False, "Windows drops the dot");
                    Assert.That(FileManager.OpenUntrustedItem(program + " ", null), Is.False, "and the space");
                    Assert.That(FileManager.IsDangerousToOpen("setup.exe. "), Is.True, "asked by name alone too");
                    Assert.That(FileManager.IsDangerousToOpen("notes.txt:setup.exe"), Is.True);
                }
                else
                {
                    Assert.That(FileManager.IsDangerousToOpen("Meeting 10:30.pdf"), Is.False, "a colon is a letter here");
                }

                Assert.That(recording.Opened, Is.EqualTo(new[] { Path.GetFullPath(text) }));
            });
        }
        finally
        {
            FileManager.Platform = platform;
            Directory.Delete(folder, true);
        }
    }

    [Test]
    public void WithoutAPlatform_EveryFileIsDangerousToOpen()
    {
        var platform = FileManager.Platform;
        try
        {
            FileManager.Platform = null;
            Assert.That(FileManager.IsDangerousToOpen("notes.txt"), Is.True);
        }
        finally
        {
            FileManager.Platform = platform;
        }
    }

    [Test, Platform("Linux")]
    public void OnLinux_AFileFromOutside_LosesTheRightToRun()
    {
        var folder = Directory.CreateTempSubdirectory("adam-untrusted-").FullName;
        try
        {
            var path = Path.Combine(folder, "notes.txt");
            File.WriteAllText(path, "#!/bin/sh");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            Assert.Multiple(() =>
            {
                Assert.That(new Adamantium.UI.Platforms.Linux.DesktopFileManager().MarkUntrusted(path), Is.True);
                Assert.That(File.GetUnixFileMode(path) & UnixFileMode.UserExecute, Is.EqualTo(UnixFileMode.None));
            });
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Test, Platform("Linux,MacOsX")]
    public void OnUnix_AProgramByItsContent_IsNotOpened_WhateverItsName()
    {
        var folder = Directory.CreateTempSubdirectory("adam-untrusted-").FullName;
        var platform = FileManager.Platform;
        var recording = new RecordingPlatform();
        try
        {
            var windowsProgram = Path.Combine(folder, "invoice");
            var linuxProgram = Path.Combine(folder, "invoice.dat");
            var launcher = Path.Combine(folder, "readme");
            var text = Path.Combine(folder, "notes");
            File.WriteAllBytes(windowsProgram, "MZ\u0090\0"u8.ToArray());
            File.WriteAllBytes(linuxProgram, "\u007FELF\u0002"u8.ToArray());
            File.WriteAllText(launcher, "# made by hand\n[Desktop Entry]\nExec=sh -c evil\n");
            File.WriteAllText(text, "just notes");
            FileManager.Platform = recording;

            Assert.Multiple(() =>
            {
                Assert.That(FileManager.OpenUntrustedItem(windowsProgram, null), Is.False, "Wine would run it");
                Assert.That(FileManager.OpenUntrustedItem(linuxProgram, null), Is.False);
                Assert.That(FileManager.OpenUntrustedItem(launcher, null), Is.False);
                Assert.That(FileManager.OpenUntrustedItem(text, null), Is.True);
                Assert.That(recording.Opened, Is.EqualTo(new[] { text }));
            });
        }
        finally
        {
            FileManager.Platform = platform;
            Directory.Delete(folder, true);
        }
    }

    [Test, Platform("Linux,MacOsX")]
    public void OnUnix_ALink_IsNeitherMarkedNorOpened()
    {
        var folder = Directory.CreateTempSubdirectory("adam-untrusted-").FullName;
        var platform = FileManager.Platform;
        var recording = new RecordingPlatform();
        try
        {
            var target = Path.Combine(folder, "tool");
            File.WriteAllText(target, "#!/bin/sh");
            var link = Path.Combine(folder, "notes.txt");
            File.CreateSymbolicLink(link, target);
            FileManager.Platform = recording;

            Assert.Multiple(() =>
            {
                Assert.That(FileManager.MarkUntrusted(link), Is.False);
                Assert.That(FileManager.OpenUntrustedItem(link, null), Is.False);
                Assert.That(recording.Opened, Is.Empty);
            });
        }
        finally
        {
            FileManager.Platform = platform;
            Directory.Delete(folder, true);
        }
    }

    private sealed class RecordingPlatform : IFileManagerPlatform
    {
        public List<string> Opened { get; } = [];

        public bool ShowItem(string path) => false;

        public bool OpenItem(string path) => false;

        public bool IsDangerousToOpen(string fileName) => false;

        public bool OpenUntrustedItem(string path, IntPtr ownerWindow)
        {
            Opened.Add(path);
            return true;
        }

        public bool MarkUntrusted(string path) => true;

        public string DownloadsFolder => null;
    }
}
