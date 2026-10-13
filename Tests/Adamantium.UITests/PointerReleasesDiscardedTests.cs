using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>The pointers live as long as the application. What one was over, clicked or hovered last must not outlive
/// its release - a window the pointer last touched stayed alive after it closed, and with it all it held.</summary>
[TestFixture]
public class PointerReleasesDiscardedTests
{
    [Test]
    public void APointer_LetsGoOfAReleasedElement()
    {
        var pointer = PointerDevice.Current.MousePointer;
        var button = new Button();
        pointer.DirectlyOver = button;
        pointer.HoverRoot = button;
        pointer.PositionRoot = button;
        pointer.LastClicked = button;

        DiscardedVisuals.Publish(button);
        while (DiscardedVisuals.Drain(64) > 0) { }

        Assert.Multiple(() =>
        {
            Assert.That(pointer.DirectlyOver, Is.Null, "what it is over");
            Assert.That(pointer.HoverRoot, Is.Null, "where it hovers");
            Assert.That(pointer.PositionRoot, Is.Null, "where it is");
            Assert.That(pointer.LastClicked, Is.Null, "what it clicked last");
        });
    }
}
