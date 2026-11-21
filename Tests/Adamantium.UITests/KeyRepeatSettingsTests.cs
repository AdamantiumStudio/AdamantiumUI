using System;
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

    // The registry holds the same steps the control panel writes, so it tells what the system call must answer.
    [Test]
    [Platform("Win")]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void WindowsAnswersWithTheControlPanelsSteps()
    {
        using var keyboard = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Keyboard");
        var delayStep = int.Parse((string)keyboard!.GetValue("KeyboardDelay"));
        var speedStep = int.Parse((string)keyboard.GetValue("KeyboardSpeed"));
        var windows = new Adamantium.UI.Platforms.Windows.WindowsInput();

        Assert.That(windows.KeyRepeatDelay, Is.EqualTo((delayStep + 1) * 250));
        Assert.That(windows.KeyRepeatInterval, Is.EqualTo((uint)Math.Round(1000 / (2.5 + speedStep * 27.5 / 31))));
    }

    [Test]
    public void NothingSaidKeepsTheDefaultPace()
    {
        PlatformSettings.Platform = new Settings(0, 0);

        Assert.That(PlatformSettings.KeyRepeatDelay, Is.EqualTo(500));
        Assert.That(PlatformSettings.KeyRepeatInterval, Is.EqualTo(33));
    }
}
