using System;
using System.Diagnostics;
using System.Runtime.Versioning;
using Adamantium.UI.Platforms.Windows;
using Microsoft.Win32;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>The languages the user added to Windows: read once, read again only after the user's language list
/// changed, and cheap to ask on every key.</summary>
[TestFixture]
[Platform("Win")]
[SupportedOSPlatform("windows")]
public class WindowsUserLanguagesTests
{
    private string _key;

    [SetUp]
    public void SetUp()
    {
        _key = $@"Software\Adamantium\Tests\UserLanguages-{Guid.NewGuid():N}";
        Registry.CurrentUser.CreateSubKey(_key)!.Dispose();
    }

    [TearDown]
    public void TearDown() => Registry.CurrentUser.DeleteSubKeyTree(_key, false);

    [Test]
    public void TheUsersLanguages_AreWindowsOwnList()
    {
        using var languages = new WindowsUserLanguages(_key);

        Assert.That(languages.Current, Is.EqualTo(WindowsUserLanguages.Read()));
        Assert.That(languages.Current, Is.Not.Empty, "Windows lists at least the language it shows");
    }

    [Test]
    public void Unchanged_TheListIsKept_AndAChangeReadsItAgain()
    {
        using var languages = new WindowsUserLanguages(_key);
        var first = languages.Current;
        var unchanged = languages.Current;

        using (var key = Registry.CurrentUser.OpenSubKey(_key, true)!)
        {
            key.SetValue("Languages", new[] { "ru-RU" }, RegistryValueKind.MultiString);
        }

        var afterChange = languages.Current;

        Assert.Multiple(() =>
        {
            Assert.That(unchanged, Is.SameAs(first), "not read again while nothing changed");
            Assert.That(afterChange, Is.Not.SameAs(first), "read again once the list changed");
        });
    }

    [Test]
    public void Asked_OnEveryKey_ItCostsNextToNothing()
    {
        using var languages = new WindowsUserLanguages(_key);
        _ = languages.Current;
        var clock = Stopwatch.StartNew();

        for (var ask = 0; ask < 10_000; ask++)
        {
            _ = languages.Current;
        }

        clock.Stop();
        TestContext.Out.WriteLine($"one ask: {clock.Elapsed.TotalMilliseconds * 1000 / 10_000:F2} µs");
        Assert.That(clock.Elapsed.TotalMilliseconds / 10_000, Is.LessThan(0.05), "under 50 µs an ask");
    }
}
