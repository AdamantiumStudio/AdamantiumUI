using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>
/// The CLR event wrappers must actually UNSUBSCRIBE. The move and wheel wrappers once had <c>AddHandler</c> in their
/// remove accessor, so every <c>-=</c> quietly added another handler: DragDrop.Detach ran after each gesture, doubling
/// the per-move drag work every time, and OverlayWindow's template teardown did the same. Cheap guard against the typo
/// coming back on any wrapper it is added to.
/// </summary>
[TestFixture]
public class RoutedEventUnsubscribeTests
{
    [Test]
    public void PointerMoved_MinusEquals_ActuallyUnsubscribes()
    {
        var element = new Border();
        var calls = 0;
        void Handler(object sender, PointerEventArgs e) => calls++;

        element.PointerMoved += Handler;
        Raise(element, PointerEvents.PointerMovedEvent);
        Assert.That(calls, Is.EqualTo(1), "the handler must run while subscribed");

        element.PointerMoved -= Handler;
        Raise(element, PointerEvents.PointerMovedEvent);
        Assert.That(calls, Is.EqualTo(1), "-= must remove the handler, not add a second one");
    }

    [Test]
    public void PointerWheelChanged_MinusEquals_ActuallyUnsubscribes()
    {
        var element = new Border();
        var calls = 0;
        void Handler(object sender, PointerWheelEventArgs e) => calls++;

        element.PointerWheelChanged += Handler;
        Raise(element, PointerEvents.PointerWheelChangedEvent);
        Assert.That(calls, Is.EqualTo(1));

        element.PointerWheelChanged -= Handler;
        Raise(element, PointerEvents.PointerWheelChangedEvent);
        Assert.That(calls, Is.EqualTo(1));
    }

    private static void Raise(IObservableComponent element, Adamantium.UI.Core.RoutedEvents.RoutedEvent routedEvent)
    {
        PointerEventArgs args = routedEvent == PointerEvents.PointerWheelChangedEvent
            ? new PointerWheelEventArgs(Mouse.Pointer, InputModifiers.None, 120, 0)
            : new PointerEventArgs(Mouse.Pointer, InputModifiers.None, 0);
        args.RoutedEvent = routedEvent;
        element.RaiseEvent(args);
    }
}
