using System;
using System.IO;
using System.Runtime.Versioning;
using Adamantium.UI.Core;
using Adamantium.UI.Platforms.Linux;
using Adamantium.UI.Platforms.Windows;
using Microsoft.Win32;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>The application in the user's startup list: put in and taken out, and read off when the system's own
/// settings turned it off or the entry starts something else.</summary>
[TestFixture]
public class SystemStartupTests
{
    private const string Name = "Adamantium.Test.App";
    private const string Command = "\"C:\\Programs\\App\\App.exe\" " + ISystemStartup.Argument;
    private const string Exec = "\"/opt/app/App\" \"" + ISystemStartup.Argument + "\"";

    private string _root;
    private string _keys;

    private string RunKey => _keys + @"\Run";
    private string ApprovedKey => _keys + @"\StartupApproved\Run";
    private string Entry => Path.Combine(_root, "autostart", Name + ".desktop");

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "adamantium-startup-" + Guid.NewGuid().ToString("N"));
        _keys = TestsKey + @"\" + Guid.NewGuid().ToString("N");
    }

    private const string TestsKey = @"Software\AdamantiumTests";

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }

        if (OperatingSystem.IsWindows())
        {
            Registry.CurrentUser.DeleteSubKeyTree(_keys, false);
            using (var tests = Registry.CurrentUser.OpenSubKey(TestsKey))
            {
                if (tests is not { SubKeyCount: 0, ValueCount: 0 })
                {
                    return;
                }
            }

            Registry.CurrentUser.DeleteSubKey(TestsKey, false);
        }
    }

    // The same program started by another spelling of its path.
    [Test]
    [Platform("Win")]
    [SupportedOSPlatform("windows")]
    public void RunKey_TheSameProgramInAnotherCase_ReadsOn()
    {
        using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            run.SetValue(Name, Command.ToLowerInvariant().Replace(ISystemStartup.Argument.ToLowerInvariant(),
                ISystemStartup.Argument));
        }

        Assert.That(RunKeyStartup().IsOn, Is.True);
    }

    [SupportedOSPlatform("windows")]
    private RunKeyStartup RunKeyStartup() => new(RunKey, ApprovedKey, Name, Command);

    private AutostartFolderStartup FolderStartup() => new(_root, Name, "Test App", Exec);

    [Test]
    [Platform("Win")]
    [SupportedOSPlatform("windows")]
    public void RunKey_PutInAndTakenOut()
    {
        var startup = RunKeyStartup();
        Assert.That(startup.IsOn, Is.False, "not in the list at first");

        startup.IsOn = true;
        using (var run = Registry.CurrentUser.OpenSubKey(RunKey))
        {
            Assert.That(run?.GetValue(Name), Is.EqualTo(Command));
        }

        Assert.That(startup.IsOn, Is.True);

        startup.IsOn = false;
        using (var run = Registry.CurrentUser.OpenSubKey(RunKey))
        {
            Assert.That(run?.GetValue(Name), Is.Null);
        }

        Assert.That(startup.IsOn, Is.False);
    }

    // Task Manager's Startup apps leaves the value and marks it off: 03 and the time it was turned off.
    [Test]
    [Platform("Win")]
    [SupportedOSPlatform("windows")]
    public void RunKey_TurnedOffInTaskManager_ReadsOff_AndPuttingItBackTakesTheMarkAway()
    {
        var startup = RunKeyStartup();
        startup.IsOn = true;
        Approve([3, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8]);

        Assert.That(startup.IsOn, Is.False);

        startup.IsOn = true;
        Assert.That(startup.IsOn, Is.True);
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
        Assert.That(approved?.GetValue(Name), Is.Null);
    }

    [Test]
    [Platform("Win")]
    [SupportedOSPlatform("windows")]
    public void RunKey_TurnedOnInTaskManager_ReadsOn()
    {
        var startup = RunKeyStartup();
        startup.IsOn = true;
        Approve([2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);

        Assert.That(startup.IsOn, Is.True);
    }

    // The application moved, or another copy of it put itself in: this one is not started.
    [Test]
    [Platform("Win")]
    [SupportedOSPlatform("windows")]
    public void RunKey_StartingAnotherProgram_ReadsOff_AndTurningItOnPutsThisOneIn()
    {
        using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            run.SetValue(Name, "\"D:\\Old\\App.exe\" " + ISystemStartup.Argument);
        }

        var startup = RunKeyStartup();
        Assert.That(startup.IsOn, Is.False);

        startup.IsOn = true;
        Assert.That(startup.IsOn, Is.True);
    }

    [Test]
    public void AutostartFolder_PutInAndTakenOut()
    {
        var startup = FolderStartup();
        Assert.That(startup.IsOn, Is.False, "not in the folder at first");

        startup.IsOn = true;
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(Entry), Is.EqualTo(
                "[Desktop Entry]\nType=Application\nName=Test App\nExec=" + Exec + "\nX-GNOME-Autostart-enabled=true\n"));
            Assert.That(startup.IsOn, Is.True);
        });

        startup.IsOn = false;
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Entry), Is.False);
            Assert.That(startup.IsOn, Is.False);
        });
    }

    [TestCase("Hidden=true")]
    [TestCase("X-GNOME-Autostart-enabled=false")]
    public void AutostartFolder_TurnedOffByTheDesktop_ReadsOff_AndTurningItOnPutsItBack(string off)
    {
        var startup = FolderStartup();
        startup.IsOn = true;
        File.WriteAllText(Entry, "[Desktop Entry]\nType=Application\nName=Test App\nExec=" + Exec + "\n" + off + "\n");

        Assert.That(startup.IsOn, Is.False);

        startup.IsOn = true;
        Assert.That(startup.IsOn, Is.True);
    }

    [Test]
    public void AutostartFolder_StartingAnotherProgram_ReadsOff()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Entry)!);
        File.WriteAllText(Entry, "[Desktop Entry]\nType=Application\nName=Test App\nExec=\"/old/App\"\n");

        Assert.That(FolderStartup().IsOn, Is.False);
    }

    [Test]
    public void AutostartFolder_TakenOutWhenNotIn_IsNoError() =>
        Assert.That(() => FolderStartup().IsOn = false, Throws.Nothing);

    [Test]
    [Platform("Win")]
    [SupportedOSPlatform("windows")]
    public void RunKey_TakenOutWhenNotIn_IsNoError() =>
        Assert.That(() => RunKeyStartup().IsOn = false, Throws.Nothing);

    [Test]
    public void ARunNotStartedBySystem_SaysSo() =>
        Assert.That(((ISystemStartup)FolderStartup()).StartedTheApplication, Is.False);

    [SupportedOSPlatform("windows")]
    private void Approve(byte[] mark)
    {
        using var approved = Registry.CurrentUser.CreateSubKey(ApprovedKey);
        approved.SetValue(Name, mark, RegistryValueKind.Binary);
    }
}
