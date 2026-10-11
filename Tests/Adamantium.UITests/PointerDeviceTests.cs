using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Input.Raw;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>The device's model of pointers, fed the raw events a platform sends: every pointer is its own - its capture,
/// the element under it and its click count - so two fingers work two things at once.</summary>
[TestFixture]
public class PointerDeviceTests
{
    private Window _window;
    private Border _left;
    private Border _right;

    [SetUp]
    public void SetUp()
    {
        _left = new Border { Width = 100, Height = 100, Background = Brushes.Gray };
        _right = new Border { Width = 100, Height = 100, Background = Brushes.Gray };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_left);
        row.Children.Add(_right);
        _window = new Window { Width = 200, Height = 100, Content = row };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(_window);
        }

        // Whatever pressed a pointer down presses it into its own capture, as a drag would.
        foreach (var border in new[] { _left, _right })
        {
            border.AddHandler(PointerEvents.PointerPressedEvent,
                new PointerPressedEventHandler((sender, e) => e.Pointer.Capture((IInputComponent)sender)), handledEventsToo: true);
        }
    }

    [TearDown]
    public void TearDown()
    {
        Mouse.Pointer.Capture(null);
        Send(RawPointerEventType.LeaveWindow, PointerDevice.MouseId, PointerType.Mouse, Vector2.Zero, 0);
    }

    [Test]
    public void TwoFingers_EachHoldTheirOwnElement()
    {
        Send(RawPointerEventType.Down, 10, PointerType.Touch, new Vector2(50, 50), 0);
        Send(RawPointerEventType.Down, 11, PointerType.Touch, new Vector2(150, 50), 0);

        var first = Finger(10);
        var second = Finger(11);
        Assert.Multiple(() =>
        {
            Assert.That(first.Captured, Is.SameAs(_left));
            Assert.That(second.Captured, Is.SameAs(_right));
            Assert.That(first.DirectlyOver, Is.SameAs(_left));
            Assert.That(second.DirectlyOver, Is.SameAs(_right));
            Assert.That(_left.IsPointerOver && _right.IsPointerOver, Is.True, "each finger lights what it is on");
        });

        // The first finger slides over the second one's element and stays with its own.
        var movedOnLeft = 0;
        _left.PointerMoved += (_, _) => movedOnLeft++;
        Send(RawPointerEventType.Move, 10, PointerType.Touch, new Vector2(150, 50), 0);

        Assert.Multiple(() =>
        {
            Assert.That(movedOnLeft, Is.EqualTo(1), "the move goes to the element the finger holds");
            Assert.That(first.Captured, Is.SameAs(_left));
            Assert.That(second.Captured, Is.SameAs(_right), "and the other finger's hold is untouched");
        });
    }

    [Test]
    public void ALiftedFinger_IsGoneAndLetsGo()
    {
        Send(RawPointerEventType.Down, 10, PointerType.Touch, new Vector2(50, 50), 0);
        Send(RawPointerEventType.Down, 11, PointerType.Touch, new Vector2(150, 50), 0);
        var first = Finger(10);

        Send(RawPointerEventType.Up, 10, PointerType.Touch, new Vector2(50, 50), 0);

        Assert.Multiple(() =>
        {
            Assert.That(PointerDevice.Current.Pointers.Any(p => p.Id == 10), Is.False);
            Assert.That(first.Captured, Is.Null);
            Assert.That(_left.IsPointerOver, Is.False, "nothing is touching the left one any more");
            Assert.That(Finger(11).Captured, Is.SameAs(_right), "the other finger still holds its own");
        });

        Send(RawPointerEventType.Cancel, 11, PointerType.Touch, new Vector2(150, 50), 0);

        Assert.Multiple(() =>
        {
            Assert.That(PointerDevice.Current.Pointers.Any(p => p.Type == PointerType.Touch), Is.False);
            Assert.That(_right.IsPointerOver, Is.False);
        });
    }

    // A tap elsewhere in between does not break a double click: the count belongs to the pointer that clicked.
    [Test]
    public void ClickCount_IsCountedPerPointer()
    {
        var mouseClicks = 0;
        _left.AddHandler(PointerEvents.PointerPressedEvent, new PointerPressedEventHandler((_, e) =>
        {
            if (e.Pointer.Type == PointerType.Mouse) mouseClicks = e.ClickCount;
        }), handledEventsToo: true);

        Send(RawPointerEventType.Down, PointerDevice.MouseId, PointerType.Mouse, new Vector2(50, 50), 1000);
        Send(RawPointerEventType.Up, PointerDevice.MouseId, PointerType.Mouse, new Vector2(50, 50), 1010);
        Send(RawPointerEventType.Down, 20, PointerType.Touch, new Vector2(150, 50), 1020);
        Send(RawPointerEventType.Up, 20, PointerType.Touch, new Vector2(150, 50), 1030);
        Send(RawPointerEventType.Down, PointerDevice.MouseId, PointerType.Mouse, new Vector2(50, 50), 1040);
        Send(RawPointerEventType.Up, PointerDevice.MouseId, PointerType.Mouse, new Vector2(50, 50), 1050);

        Assert.That(mouseClicks, Is.EqualTo(2));
    }

    private static Pointer Finger(uint id) => PointerDevice.Current.Pointers.Single(p => p.Id == id);

    private void Send(RawPointerEventType type, uint id, PointerType pointerType, Vector2 at, uint timestamp)
    {
        var held = type == RawPointerEventType.Down ? InputModifiers.LeftMouseButton : InputModifiers.None;
        PointerDevice.Current.ProcessEvent(new RawPointerEventArgs(type, _window, at, held, timestamp)
        {
            PointerId = id,
            PointerType = pointerType,
            IsPrimary = pointerType == PointerType.Mouse,
            ChangedButton = type is RawPointerEventType.Down or RawPointerEventType.Up ? MouseButtons.Left : MouseButtons.None
        });
    }
}
