using System;
using System.Runtime.Versioning;
using System.Threading;
using Adamantium.UI.Platforms.Windows;
using Microsoft.Win32;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A watch on a key of the current user, on a key of the test's own.</summary>
[TestFixture]
[Platform("Win")]
[SupportedOSPlatform("windows")]
public class RegistryKeyWatchTests
{
    private TestRegistryKey _key;

    [SetUp]
    public void SetUp() => _key = new TestRegistryKey();

    [TearDown]
    public void TearDown() => _key.Dispose();

    [Test]
    public void NothingDone_IsNoChange()
    {
        using var watch = new RegistryKeyWatch(_key.Path);

        Assert.That(watch.TakeChange(), Is.False);
    }

    [Test]
    public void AValueSet_IsAChange_TakenOnce()
    {
        using var watch = new RegistryKeyWatch(_key.Path);

        _key.SetValue("Languages", "en-US");

        Assert.Multiple(() =>
        {
            Assert.That(TakeChangeSoon(watch), Is.True);
            Assert.That(watch.TakeChange(), Is.False);
        });
    }

    [Test]
    public void AfterAChangeIsTaken_TheNextIsSeenToo()
    {
        using var watch = new RegistryKeyWatch(_key.Path);
        _key.SetValue("Languages", "en-US");
        Assume.That(TakeChangeSoon(watch), Is.True);

        _key.SetValue("Languages", "en-US ru-UA");

        Assert.That(TakeChangeSoon(watch), Is.True);
    }

    [Test]
    public void AKeyAddedUnderIt_IsAChange()
    {
        using var watch = new RegistryKeyWatch(_key.Path);

        Registry.CurrentUser.CreateSubKey(_key.Path + @"\ru-UA").Dispose();

        Assert.That(TakeChangeSoon(watch), Is.True);
    }

    // Windows by default marks the event when the thread that asked for the notice ends.
    [Test]
    public void TheThreadThatWatchedEnding_IsNoChange()
    {
        RegistryKeyWatch watch = null;
        var thread = new Thread(() => watch = new RegistryKeyWatch(_key.Path));
        thread.Start();
        thread.Join();

        using (watch)
        {
            Thread.Sleep(100);
            Assert.That(watch.TakeChange(), Is.False);
            _key.SetValue("Languages", "en-US");
            Assert.That(TakeChangeSoon(watch), Is.True);
        }
    }

    [Test]
    public void AKeyThatDoesNotExist_NeverChanges()
    {
        using var watch = new RegistryKeyWatch(_key.Path + @"\missing");

        Assert.That(watch.TakeChange(), Is.False);
    }

    // The event is marked by Windows right after the change, not within the call that made it.
    private static bool TakeChangeSoon(RegistryKeyWatch watch)
    {
        var until = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < until)
        {
            if (watch.TakeChange())
                return true;

            Thread.Sleep(10);
        }

        return false;
    }
}
