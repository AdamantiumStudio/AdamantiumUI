using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Core.Commands;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Input.Raw;
using Adamantium.UI.Core.Media.Animation;
using Adamantium.UI.Extensions;
using Adamantium.UI.Input;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>Several contacts at once - from two fingers to the ten a touch screen reports - which is what pointers are
/// for: each finger works its own element, scrolls its own list and ends its own gesture, and none of them takes what
/// another is doing. Elements stand side by side, one column of 100 per finger.</summary>
[TestFixture]
public class MultiTouchTests
{
    private const uint FirstId = 60;
    private const double Column = 100;

    private Window _window;
    private TimeSpan _holdingTime;

    [SetUp]
    public void SetUp()
    {
        // The tests say themselves when a contact has rested long enough; a real timer must not race them.
        _holdingTime = Gestures.HoldingTime;
        Gestures.HoldingTime = TimeSpan.FromHours(1);
    }

    [TearDown]
    public void TearDown()
    {
        Gestures.HoldingTime = _holdingTime;
        AnimationManager.Reset();
    }

    [Test]
    public void EveryFinger_PressesItsOwnButton_AndClicksItOnItsOwnLift([Range(2, 10)] int fingers)
    {
        var buttons = Hosted(fingers, _ => new Button { Width = Column, Height = 100 });
        var clicks = new int[fingers];
        for (var i = 0; i < fingers; i++)
        {
            var index = i;
            buttons[i].Click += (_, _) => clicks[index]++;
        }

        for (var i = 0; i < fingers; i++)
        {
            Send(RawPointerEventType.Down, i, MiddleOf(i, 50));
        }

        Assert.That(buttons.All(b => b.IsPressed), Is.True, "every button is held down at once");

        // Lifted last to first: each lift clicks its own button and no other.
        for (var i = fingers - 1; i >= 0; i--)
        {
            Send(RawPointerEventType.Up, i, MiddleOf(i, 50));
            Assert.That(clicks, Is.EqualTo(Enumerable.Range(0, fingers).Select(c => c >= i ? 1 : 0)),
                $"finger {i} clicked its button, and the fingers still down clicked nothing");
        }
    }

    [Test]
    public void EveryFinger_ScrollsItsOwnList([Range(2, 10)] int fingers)
    {
        var lists = Hosted(fingers, _ => Scroller());

        for (var i = 0; i < fingers; i++)
        {
            Send(RawPointerEventType.Down, i, MiddleOf(i, 250));
        }

        // Interleaved, as fingers move together: each finger a distance of its own, in two steps.
        for (var i = 0; i < fingers; i++)
        {
            Send(RawPointerEventType.Move, i, MiddleOf(i, 250 - (10 + 5 * i)));
        }

        for (var i = 0; i < fingers; i++)
        {
            Send(RawPointerEventType.Move, i, MiddleOf(i, 250 - (20 + 10 * i)));
        }

        for (var i = 0; i < fingers; i++)
        {
            Send(RawPointerEventType.Up, i, MiddleOf(i, 250 - (20 + 10 * i)));
        }

        Assert.That(lists.Select(l => Math.Round(l.Offset.Y)),
            Is.EqualTo(Enumerable.Range(0, fingers).Select(i => (double)(20 + 10 * i))),
            "each list followed its own finger, and only that one");
    }

    [Test]
    public void TheFingersThatFollow_LeaveTheFirstOnesScrollAlone([Range(2, 10)] int fingers)
    {
        var list = Hosted(1, _ => Scroller())[0];

        Send(RawPointerEventType.Down, 0, new Vector2(50, 250));
        Send(RawPointerEventType.Move, 0, new Vector2(50, 200));
        for (var i = 1; i < fingers; i++)
        {
            Send(RawPointerEventType.Down, i, new Vector2(20 + 6 * i, 120));
            Send(RawPointerEventType.Move, i, new Vector2(20 + 6 * i, 20));
        }

        Send(RawPointerEventType.Move, 0, new Vector2(50, 190));

        Assert.That(list.Offset.Y, Is.EqualTo(60).Within(0.5), "only the first finger drives the list");

        for (var i = 0; i < fingers; i++)
        {
            Send(RawPointerEventType.Up, i, new Vector2(20 + 6 * i, 20));
        }
    }

    // One finger holding its button does not stop the others from tapping theirs - and the held one, lifted, is a long
    // press, not a click.
    [Test]
    public void OneFingerHolds_WhileTheOthersTap([Range(2, 10)] int fingers)
    {
        var buttons = Hosted(fingers, _ => new Button { Width = Column, Height = 100 });
        var clicks = new int[fingers];
        for (var i = 0; i < fingers; i++)
        {
            var index = i;
            buttons[i].Click += (_, _) => clicks[index]++;
        }

        Send(RawPointerEventType.Down, 0, MiddleOf(0, 50));
        PointerDevice.Current.Holding.Elapsed(FirstId, 600);
        for (var i = 1; i < fingers; i++)
        {
            Send(RawPointerEventType.Down, i, MiddleOf(i, 50));
            Send(RawPointerEventType.Up, i, MiddleOf(i, 50));
        }

        Send(RawPointerEventType.Up, 0, MiddleOf(0, 50));

        Assert.That(clicks, Is.EqualTo(Enumerable.Range(0, fingers).Select(i => i == 0 ? 0 : 1)),
            "every tap went through beside the hold, and the held button was not clicked");
    }

    // A drag belongs to the finger that started it: the other fingers' presses and lifts do not drop what it carries.
    [Test]
    public void TheOtherFingers_DoNotEndTheFirstOnesDrag([Range(2, 10)] int fingers)
    {
        var pads = Hosted(fingers, _ => Pad());
        var started = 0;
        var completed = 0;
        DragDrop.SetAllowDrag(pads[0], true);
        DragDrop.SetDragStartedCommand(pads[0], new TestCommand(_ => started++));
        DragDrop.SetDragCompletedCommand(pads[0], new TestCommand(_ => completed++));

        Send(RawPointerEventType.Down, 0, MiddleOf(0, 50));
        Send(RawPointerEventType.Move, 0, MiddleOf(0, 70));
        for (var i = 1; i < fingers; i++)
        {
            Send(RawPointerEventType.Down, i, MiddleOf(i, 50));
            Send(RawPointerEventType.Move, i, MiddleOf(i, 70));
            Send(RawPointerEventType.Up, i, MiddleOf(i, 70));
        }

        Assert.Multiple(() =>
        {
            Assert.That(started, Is.EqualTo(1), "the first finger picked its pad up");
            Assert.That(completed, Is.Zero, "and no other finger's lift dropped it");
        });

        Send(RawPointerEventType.Up, 0, MiddleOf(0, 70));
        Assert.That(completed, Is.EqualTo(1), "the first finger's own lift drops it");
    }

    // Over is counted per pointer: an element stays hovered while any one of them is still over it - a pen hovering
    // while the fingers come and go.
    [Test]
    public void AnElementStaysOver_WhileAnyPointerIsOverIt([Range(2, 10)] int contacts)
    {
        var pad = Hosted(1, _ => Pad())[0];

        Send(RawPointerEventType.Move, 0, new Vector2(10, 10), kind: PointerType.Pen);
        for (var i = 1; i < contacts; i++)
        {
            Send(RawPointerEventType.Down, i, new Vector2(10 + 8 * i, 50));
        }

        for (var i = 1; i < contacts; i++)
        {
            Send(RawPointerEventType.Up, i, new Vector2(10 + 8 * i, 50));
            Assert.That(pad.IsPointerOver, Is.True, $"finger {i} lifted, the pen and the others are still over it");
        }

        Send(RawPointerEventType.LeaveWindow, 0, Vector2.Zero, kind: PointerType.Pen);
        Assert.That(pad.IsPointerOver, Is.False, "the last one has gone");
    }

    // Every touch is a new pointer, yet two taps in a row on one spot are a double tap.
    [Test]
    public void TwoTapsInARow_AreADoubleTap()
    {
        var pad = Hosted(1, _ => Pad())[0];
        var counts = new List<int>();
        pad.AddHandler(PointerEvents.PointerPressedEvent,
            new PointerPressedEventHandler((_, e) => counts.Add(e.ClickCount)), handledEventsToo: true);

        Send(RawPointerEventType.Down, 0, new Vector2(50, 50), 1000);
        Send(RawPointerEventType.Up, 0, new Vector2(50, 50), 1050);
        Send(RawPointerEventType.Down, 1, new Vector2(51, 50), 1150);
        Send(RawPointerEventType.Up, 1, new Vector2(51, 50), 1200);

        Assert.That(counts, Is.EqualTo(new[] { 1, 2 }));
    }

    private static Border Pad() =>
        new() { Width = Column, Height = 100, Background = Adamantium.UI.Core.Media.Brushes.Gray };

    // A 300-high scroll area of ten 100-high rows.
    private static ScrollContentPresenter Scroller()
    {
        var rows = new StackPanel();
        for (var i = 0; i < 10; i++)
        {
            rows.Children.Add(new Button { Height = 100 });
        }

        return new ScrollContentPresenter { Content = rows, Width = Column, Height = 300, IsInertiaEnabled = false };
    }

    // The elements side by side, one column each, in a window just wide enough.
    private T[] Hosted<T>(int count, Func<int, T> make) where T : IMeasurableComponent
    {
        var elements = Enumerable.Range(0, count).Select(make).ToArray();
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var element in elements)
        {
            row.Children.Add(element);
        }

        _window = new Window { Width = Column * count, Height = 300, Content = row };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(_window);
        }

        return elements;
    }

    private static Vector2 MiddleOf(int column, double y) => new(Column * column + Column / 2, y);

    private void Send(RawPointerEventType type, int finger, Vector2 at, uint timestamp = 0, PointerType kind = PointerType.Touch) =>
        PointerDevice.Current.ProcessEvent(new RawPointerEventArgs(type, _window, at, InputModifiers.None, timestamp)
        {
            PointerId = FirstId + (uint)finger,
            PointerType = kind,
            IsPrimary = finger == 0
        });

    private sealed class TestCommand(Action<object> run) : ICommand
    {
        public bool CanExecute(object parameter = null) => true;
        public void Execute(object parameter = null) => run(parameter);
        public event EventHandler CanExecuteChanged;
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
