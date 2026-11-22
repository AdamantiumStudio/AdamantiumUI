using Adamantium.UI.Controls;
using NUnit.Framework;

namespace Adamantium.UITests;

// A window with no window of the system behind it - one drawn headless - takes what talks to the worker as nothing to do.
[TestFixture]
public class WindowWithoutSystemWindowTests
{
    [Test]
    public void Hidden_DoesNothing()
    {
        var window = new Window();

        Assert.DoesNotThrow(window.Hide);
        Assert.That(window.IsDisplayed, Is.False);
    }
}
