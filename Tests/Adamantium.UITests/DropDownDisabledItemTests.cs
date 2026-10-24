using System.Collections.Generic;
using Adamantium.UI.Controls;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A disabled row of a drop-down is shown but cannot be taken: not by a click, not by Enter, and the arrows and
/// the wheel step over it.</summary>
[TestFixture]
public class DropDownDisabledItemTests
{
    private static KeyEventArgs Press(Key key, object on) =>
        new(KeyboardDevice.CurrentDevice, key, InputModifiers.None, 0)
        {
            RoutedEvent = Keyboard.KeyDownEvent,
            OriginalSource = on
        };

    private static DropDown WithMiddleDisabled()
    {
        var drop = new DropDown();
        drop.Items.Add(new DropDownItem { Content = "One" });
        drop.Items.Add(new DropDownItem { Content = "Two", IsEnabled = false });
        drop.Items.Add(new DropDownItem { Content = "Three" });
        return drop;
    }

    [Test]
    public void TheArrows_StepOverIt()
    {
        var drop = WithMiddleDisabled();
        drop.RaiseEvent(Press(Key.Enter, drop));

        drop.RaiseEvent(Press(Key.DownArrow, drop));
        drop.RaiseEvent(Press(Key.DownArrow, drop));
        drop.RaiseEvent(Press(Key.Enter, drop));

        Assert.That(drop.SelectedIndex, Is.EqualTo(2));
    }

    [Test]
    public void AClickOnIt_TakesNothing()
    {
        var drop = WithMiddleDisabled();
        drop.SelectedIndex = 0;

        PointerInput.Press((DropDownItem)drop.Items[1]);

        Assert.That(drop.SelectedIndex, Is.EqualTo(0));
    }

    [Test]
    public void EnterOnIt_TakesNothing()
    {
        var drop = WithMiddleDisabled();
        drop.SelectedIndex = 0;
        var row = (DropDownItem)drop.Items[1];

        row.RaiseEvent(Press(Key.Enter, row));

        Assert.That(drop.SelectedIndex, Is.EqualTo(0));
    }

    [Test]
    public void HoveringIt_DoesNotMarkItForEnter()
    {
        var drop = WithMiddleDisabled();
        drop.SelectedIndex = 0;
        drop.RaiseEvent(Press(Key.Enter, drop));

        PointerInput.Enter((DropDownItem)drop.Items[1]);
        drop.RaiseEvent(Press(Key.Enter, drop));

        Assert.Multiple(() =>
        {
            Assert.That(((DropDownItem)drop.Items[1]).IsHighlighted, Is.False);
            Assert.That(drop.SelectedIndex, Is.EqualTo(0));
        });
    }

    [Test]
    public void TheWheel_StepsOverIt()
    {
        var drop = WithMiddleDisabled();
        drop.StepsWithMouseWheel = true;
        drop.SelectedIndex = 0;

        drop.RaiseEvent(new PointerWheelEventArgs(Mouse.Pointer, InputModifiers.None, -120, 0)
        {
            RoutedEvent = PointerEvents.PointerWheelChangedEvent
        });

        Assert.That(drop.SelectedIndex, Is.EqualTo(2));
    }

    private sealed record Choice(string Name, bool Allowed);

    // As a view-model gives them: items of data, a container style binding each row's IsEnabled - and the list never
    // opened, so no row was made by showing it.
    [Test]
    public void RowsDisabledByTheirData_AreSteppedOver_BeforeTheListWasEverOpen()
    {
        var containerStyle = new Style { Selector = new StyleSelector { Types = { typeof(DropDownItem) } } };
        containerStyle.Setters.Add(new Setter("IsEnabled", new Binding("Allowed")));
        var drop = new DropDown
        {
            StepsWithMouseWheel = true,
            ItemContainerStyle = containerStyle,
            ItemsSource = new[] { new Choice("Auto", true), new Choice("Heap", false), new Choice("Sets", true) }
        };
        drop.SelectedIndex = 0;

        drop.RaiseEvent(new PointerWheelEventArgs(Mouse.Pointer, InputModifiers.None, -120, 0)
        {
            RoutedEvent = PointerEvents.PointerWheelChangedEvent
        });

        Assert.That(drop.SelectedIndex, Is.EqualTo(2));
    }

    private static DropDown Closed(System.Collections.IEnumerable choices)
    {
        var containerStyle = new Style { Selector = new StyleSelector { Types = { typeof(DropDownItem) } } };
        containerStyle.Setters.Add(new Setter("IsEnabled", new Binding("Allowed")));
        return new DropDown { StepsWithMouseWheel = true, ItemContainerStyle = containerStyle, ItemsSource = choices };
    }

    private static void WheelDown(DropDown drop) =>
        drop.RaiseEvent(new PointerWheelEventArgs(Mouse.Pointer, InputModifiers.None, -120, 0)
        {
            RoutedEvent = PointerEvents.PointerWheelChangedEvent
        });

    [Test]
    public void AClosedList_KeepsNoRow_ForWhatTheWheelLookedAt()
    {
        var choices = new List<Choice> { new("A", true) };
        for (var i = 0; i < 200; i++) choices.Add(new Choice($"Off {i}", false));
        choices.Add(new Choice("Z", true));
        var drop = Closed(choices);
        drop.SelectedIndex = 0;

        WheelDown(drop);

        Assert.Multiple(() =>
        {
            Assert.That(drop.SelectedIndex, Is.EqualTo(choices.Count - 1));
            Assert.That(drop.ItemContainerGenerator.RealizedCount, Is.Zero);
        });
    }

    [Test]
    public void AnItemPutInWhileClosed_IsReadAsItself()
    {
        var choices = new System.Collections.ObjectModel.ObservableCollection<Choice>
        {
            new("X", true), new("Y", false), new("Z", true)
        };
        var drop = Closed(choices);
        drop.SelectedIndex = 0;
        WheelDown(drop);

        choices.Insert(0, new Choice("W", true));
        drop.SelectedIndex = 0;
        WheelDown(drop);

        Assert.That(drop.SelectedItem, Is.SameAs(choices[1]), "X, not the disabled Y once at index 1");
    }

    [Test]
    public void TheWheel_StopsWhereOnlyDisabledRowsAreLeft()
    {
        var drop = new DropDown { StepsWithMouseWheel = true };
        drop.Items.Add(new DropDownItem { Content = "One" });
        drop.Items.Add(new DropDownItem { Content = "Two", IsEnabled = false });
        drop.SelectedIndex = 0;
        var wheel = new PointerWheelEventArgs(Mouse.Pointer, InputModifiers.None, -120, 0)
        {
            RoutedEvent = PointerEvents.PointerWheelChangedEvent
        };

        drop.RaiseEvent(wheel);

        Assert.Multiple(() =>
        {
            Assert.That(drop.SelectedIndex, Is.EqualTo(0));
            Assert.That(wheel.Handled, Is.False, "the page under it keeps scrolling");
        });
    }
}
