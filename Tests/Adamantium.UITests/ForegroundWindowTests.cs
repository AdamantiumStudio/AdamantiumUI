using Adamantium.UI.Core.Input;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>The window the system gives keys to, told apart from what its title says.</summary>
[TestFixture]
public class ForegroundWindowTests
{
    [Test]
    public void APlatformThatDoesNotSay_GivesNoWindow()
    {
        INativeKeyboard keyboard = new AllKeysUpKeyboard();

        Assert.That(keyboard.ForegroundWindowId, Is.EqualTo((nint)0));
    }
}
