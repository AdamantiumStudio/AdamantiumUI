using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core.Input;
using NUnit.Framework;

namespace Adamantium.UITests;

// The keyboard focus moving tells the element that takes it, before and after, with the element it came from.
[TestFixture]
public class KeyboardFocusEventsTests
{
    [Test]
    public void TheElementTakingTheFocus_IsToldBeforeAndAfter_WhereItCameFrom()
    {
        var device = KeyboardDevice.CurrentDevice;
        var before = device.FocusedComponent;
        var from = new Border();
        var to = new Border();
        KeyboardGotFocusEventArgs preview = null;
        KeyboardGotFocusEventArgs got = null;
        to.PreviewGotKeyboardFocus += (_, e) => preview = e;
        to.GotKeyboardFocus += (_, e) => got = e;

        try
        {
            device.SetFocusedElement(from);
            device.SetFocusedElement(to, NavigationMethod.Tab);
        }
        finally
        {
            device.SetFocusedElement(before);
        }

        Assert.Multiple(() =>
        {
            Assert.That(preview, Is.Not.Null, "the preview reaches the element taking the focus");
            Assert.That(preview?.OldFocus, Is.SameAs(from));
            Assert.That(preview?.NewFocus, Is.SameAs(to));
            Assert.That(got?.OldFocus, Is.SameAs(from));
            Assert.That(got?.NavigationMethod, Is.EqualTo(NavigationMethod.Tab));
        });
    }
}
