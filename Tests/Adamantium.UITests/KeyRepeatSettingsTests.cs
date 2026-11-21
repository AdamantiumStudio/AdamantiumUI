using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using NUnit.Framework;

namespace Adamantium.UITests;

[TestFixture]
public class KeyRepeatSettingsTests
{
    private sealed class Settings(uint delay, uint interval) : INativePlatformSettings
    {
        public uint DoubleClickTime => 500;

        public Size<double> DoubleClickSize => new(4, 4);

        public Size<double> DragThreshold => new(4, 4);

        public uint HoverTime => 400;

        public uint KeyRepeatDelay => delay;

        public uint KeyRepeatInterval => interval;
    }

    private INativePlatformSettings _previous;

    [SetUp]
    public void RememberPlatform() => _previous = PlatformSettings.Platform;

    [TearDown]
    public void RestorePlatform() => PlatformSettings.Platform = _previous;

    [Test]
    public void TheUsersRepeatIsTaken()
    {
        PlatformSettings.Platform = new Settings(750, 50);

        Assert.That(PlatformSettings.KeyRepeatDelay, Is.EqualTo(750));
        Assert.That(PlatformSettings.KeyRepeatInterval, Is.EqualTo(50));
    }

    // The control panel writes its steps to the registry: the delay's four are a quarter of a second each, the speed's
    // 32 run from about 2.5 repeats a second to about 30.
    [Test]
    [Platform("Win")]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void WindowsAnswersWithTheControlPanelsSteps()
    {
        using var keyboard = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Keyboard");
        var delayStep = keyboard?.GetValue("KeyboardDelay") as string;
        Assume.That(delayStep, Is.Not.Null, "This profile has no keyboard settings in the registry.");
        var windows = new Adamantium.UI.Platforms.Windows.WindowsInput();

        Assert.That(windows.KeyRepeatDelay, Is.EqualTo(new[] { 250, 500, 750, 1000 }[int.Parse(delayStep)]));
        Assert.That(windows.KeyRepeatInterval, Is.InRange(33, 400));
    }

    [Test]
    public void NoDelaySaidKeepsTheDefault()
    {
        PlatformSettings.Platform = new Settings(0, 33);

        Assert.That(PlatformSettings.KeyRepeatDelay, Is.EqualTo(500));
    }

    [Test]
    public void RepeatingTurnedOffIsTold()
    {
        PlatformSettings.Platform = new Settings(600, 0);

        Assert.That(PlatformSettings.KeyRepeatInterval, Is.Zero);
    }

    [Test]
    public void WithoutAPlatformKeysRepeatThirtyASecond()
    {
        PlatformSettings.Platform = null;

        Assert.That(PlatformSettings.KeyRepeatDelay, Is.EqualTo(500));
        Assert.That(PlatformSettings.KeyRepeatInterval, Is.EqualTo(33));
    }
}
