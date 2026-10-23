using System;
using System.IO;
using Adamantium.Imaging;
using Adamantium.Imaging.Ico;
using Adamantium.UI.Platforms.Linux;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>GNOME takes a window's icon from the desktop entry named by its app id, not from the window. The application
/// writes its own entry - hidden from the application list - and its icon into the user's icon theme, never over an entry
/// that is not its own, and only again when something changed.</summary>
[TestFixture]
public class DesktopEntryTests
{
    private const string AppId = "Adamantium.Test.App";
    private string _root;
    private string _dataHome;
    private string _systemData;

    [SetUp]
    public void MakeFolders()
    {
        _root = Path.Combine(Path.GetTempPath(), "adamantium-desktop-entry-" + Guid.NewGuid().ToString("N"));
        _dataHome = Path.Combine(_root, "home");
        _systemData = Path.Combine(_root, "system");
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void RemoveFolders() => Directory.Delete(_root, true);

    private static ApplicationIcon Icon(params int[] sizes)
    {
        var ico = new IcoImage(ImageDescription.Default2D((uint)sizes[0], (uint)sizes[0], SurfaceFormat.R8G8B8A8.UNorm));
        for (var level = 0; level < sizes.Length; level++)
        {
            var size = (uint)sizes[level];
            var pixels = new byte[size * size * 4];
            Array.Fill(pixels, (byte)200);
            ico.AddMipLevel(new FrameData(pixels, ImageDescription.Default2D(size, size, SurfaceFormat.R8G8B8A8.UNorm), (uint)level));
        }

        return ApplicationIcon.From(ico);
    }

    private string Entry => Path.Combine(_dataHome, "applications", AppId + ".desktop");

    private string ThemeIcon(int size) => Path.Combine(_dataHome, "icons", "hicolor", $"{size}x{size}", "apps", AppId + ".png");

    [Test]
    public void TheEntry_IsHidden_NamesTheIcon_AndTheIconGoesIntoTheThemeAtItsSizes()
    {
        var written = DesktopEntry.Write(AppId, _dataHome, [_systemData], Icon(256, 20, 16), "Test App", "\"/opt/app\"");

        Assert.That(written, Is.True);
        var text = File.ReadAllText(Entry);
        Assert.That(text, Does.StartWith("[Desktop Entry]\n"));
        Assert.That(text, Does.Contain("\nName=Test App\n").And.Contain("\nIcon=" + AppId + "\n")
            .And.Contain("\nExec=\"/opt/app\"\n").And.Contain("\nNoDisplay=true\n")
            .And.Contain("\nStartupWMClass=" + AppId + "\n"));
        Assert.That(File.Exists(ThemeIcon(256)) && File.Exists(ThemeIcon(16)), Is.True);
        Assert.That(File.Exists(ThemeIcon(20)), Is.False, "20 pixels is no size of the icon theme");

        var png = BitmapLoader.Load(ThemeIcon(16));
        Assert.That((png.Width, png.Height), Is.EqualTo((16u, 16u)));
    }

    [Test]
    public void AnEntryOfAnotherName_IsLeftAlone_AndNothingIsWritten()
    {
        var installed = Path.Combine(_systemData, "applications", AppId + ".desktop");
        Directory.CreateDirectory(Path.GetDirectoryName(installed));
        File.WriteAllText(installed, "[Desktop Entry]\nType=Application\nName=Installed\nIcon=installed\n");

        var written = DesktopEntry.Write(AppId, _dataHome, [_systemData], Icon(32), "Test App", "app");

        Assert.That(written, Is.False);
        Assert.That(Directory.Exists(_dataHome), Is.False);
    }

    [Test]
    public void ItsOwnEntry_IsWrittenAgainOnlyWhenItChanged()
    {
        DesktopEntry.Write(AppId, _dataHome, [], Icon(32), "Test App", "app");
        var stamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Entry, stamp);
        File.SetLastWriteTimeUtc(ThemeIcon(32), stamp);

        Assert.That(DesktopEntry.Write(AppId, _dataHome, [], Icon(32), "Test App", "app"), Is.True);
        Assert.That(File.GetLastWriteTimeUtc(Entry), Is.EqualTo(stamp));
        Assert.That(File.GetLastWriteTimeUtc(ThemeIcon(32)), Is.EqualTo(stamp));

        DesktopEntry.Write(AppId, _dataHome, [], Icon(32), "Test App", "moved");
        Assert.That(File.ReadAllText(Entry), Does.Contain("\nExec=moved\n"));
    }

    [Test]
    public void AnEntryOfAnotherApplication_InTheUsersOwnFolder_IsLeftAlone()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Entry));
        File.WriteAllText(Entry, "[Desktop Entry]\nType=Application\nName=Mine\n");

        Assert.That(DesktopEntry.Write(AppId, _dataHome, [], Icon(32), "Test App", "app"), Is.False);
        Assert.That(File.ReadAllText(Entry), Does.Contain("Name=Mine"));
    }

    [Test]
    public void ALauncherThatClaimsTheWindowsByTheirClass_IsLeftToDoSo()
    {
        var vendor = Path.Combine(_systemData, "applications", "vendor", "app.desktop");
        Directory.CreateDirectory(Path.GetDirectoryName(vendor));
        File.WriteAllText(vendor, $"[Desktop Entry]\nType=Application\nName=Packaged\nStartupWMClass={AppId}\n");

        Assert.That(DesktopEntry.Write(AppId, _dataHome, [_systemData], Icon(32), "Test App", "app"), Is.False);
        Assert.That(File.Exists(Entry), Is.False);
    }

    [Test]
    public void AnIconItDidNotWrite_IsNotWrittenOver()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ThemeIcon(48)));
        File.WriteAllBytes(ThemeIcon(48), [1, 2, 3]);

        Assert.That(DesktopEntry.Write(AppId, _dataHome, [], Icon(48, 32), "Test App", "app"), Is.True);

        Assert.That(File.ReadAllBytes(ThemeIcon(48)), Is.EqualTo(new byte[] { 1, 2, 3 }));
        Assert.That(File.Exists(ThemeIcon(32)), Is.True);
    }

    [Test]
    public void ASizeTheIconNoLongerHas_IsRemoved_OnlyIfItWroteIt()
    {
        DesktopEntry.Write(AppId, _dataHome, [], Icon(48, 32), "Test App", "app");

        DesktopEntry.Write(AppId, _dataHome, [], Icon(32), "Test App", "app");

        Assert.That(File.Exists(ThemeIcon(48)), Is.False);
        Assert.That(File.Exists(ThemeIcon(32)), Is.True);
        Assert.That(Directory.GetFiles(Path.GetDirectoryName(Entry)), Has.Length.EqualTo(1), "no temporary file is left");
    }

    [Test]
    public void ACommandWithQuotesBackslashesAndBackticks_IsEscapedTwice_ForTheArgumentAndForTheValue()
    {
        Assert.That(DesktopEntry.Exec("/opt/a\"b\\c`d"), Is.EqualTo("\"/opt/a\\\\\"b\\\\\\\\c\\\\`d\""));
    }

    [Test]
    public void AProgramInAFolderWithAPercent_IsStartedThroughTheShell()
    {
        Assert.That(DesktopEntry.Exec("/opt/50%/run"),
            Is.EqualTo("\"/bin/sh\" \"-c\" \"exec \\\\\"\\\\$0\\\\\" \\\\\"\\\\$@\\\\\"\" \"/opt/50%%/run\""));
    }

    [Test]
    public void NoIcon_NoEntry()
    {
        Assert.That(DesktopEntry.Write(AppId, _dataHome, [], null, "Test App", "app"), Is.False);
        Assert.That(File.Exists(Entry), Is.False);
    }

    [Test]
    public void TheCommand_IsQuotedAndEscaped_AsTheSpecificationAsks()
    {
        Assert.That(DesktopEntry.Exec("/opt/my app/run", "a$b", "50%"),
            Is.EqualTo("\"/opt/my app/run\" \"a\\\\$b\" \"50%%\""));
    }

    [Test]
    public void TheIcon_KeepsItsSquarePictures_LargestFirst_AndPremultipliesForWayland()
    {
        var icon = Icon(16, 256);
        Assert.That(icon.Pictures[0].Size, Is.EqualTo(256));
        Assert.That(icon.Pictures[1].Size, Is.EqualTo(16));

        var ico = new IcoImage(ImageDescription.Default2D(1, 1, SurfaceFormat.R8G8B8A8.UNorm));
        ico.AddMipLevel(new FrameData([255, 128, 0, 128], ImageDescription.Default2D(1, 1, SurfaceFormat.R8G8B8A8.UNorm)));
        Assert.That(ApplicationIcon.From(ico).Pictures[0].PremultipliedBgra, Is.EqualTo(new byte[] { 0, 64, 128, 128 }));
    }

    [Test]
    public void AnAssemblyWithoutAnIcon_HasNone()
    {
        Assert.That(ApplicationIcon.Load(typeof(DesktopEntryTests).Assembly), Is.Null);
    }
}
