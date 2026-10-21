using System.Collections.Generic;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
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

    [Test]
    public void APreviewHandled_KeepsTheFocusWhereItIs()
    {
        var device = KeyboardDevice.CurrentDevice;
        var before = device.FocusedComponent;
        var from = new Border();
        var to = new Border();
        var lost = 0;
        to.PreviewGotKeyboardFocus += (_, e) => e.Handled = true;
        from.LostKeyboardFocus += (_, _) => lost++;

        bool moved;
        IInputComponent focused;
        int lostThen;
        try
        {
            device.SetFocusedElement(from);
            moved = device.SetFocusedElement(to);
            focused = device.FocusedComponent;
            lostThen = lost;
        }
        finally
        {
            device.SetFocusedElement(before);
        }

        Assert.Multiple(() =>
        {
            Assert.That(moved, Is.False);
            Assert.That(focused, Is.SameAs(from));
            Assert.That(lostThen, Is.Zero);
        });
    }

    [Test]
    public void APreviewThatMovesTheFocusElsewhere_Wins_AndEachElementIsToldOnce()
    {
        var device = KeyboardDevice.CurrentDevice;
        var before = device.FocusedComponent;
        var from = new Border();
        var to = new Border();
        var other = new Border();
        List<string> told = [];
        to.PreviewGotKeyboardFocus += (_, _) => device.SetFocusedElement(other);
        from.LostKeyboardFocus += (_, _) => told.Add("from lost");
        other.GotKeyboardFocus += (_, _) => told.Add("other got");
        to.GotKeyboardFocus += (_, _) => told.Add("to got");

        bool moved;
        IInputComponent focused;
        try
        {
            device.SetFocusedElement(from);
            moved = device.SetFocusedElement(to);
            focused = device.FocusedComponent;
        }
        finally
        {
            device.SetFocusedElement(before);
        }

        Assert.Multiple(() =>
        {
            Assert.That(moved, Is.False);
            Assert.That(focused, Is.SameAs(other));
            Assert.That(told, Is.EqualTo((string[])["from lost", "other got"]));
        });
    }
}
