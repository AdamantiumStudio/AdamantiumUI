using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Diagnostics;
using Adamantium.UI.Core.RoutedEvents;
using NUnit.Framework;

namespace Adamantium.UITests;

// How a routed event walks its route and calls its handlers, and what an observer of it is told. Each test registers
// its own events, so class handlers and registrations do not pile up when the tests run again in one process.
[TestFixture]
public class RoutedEventDispatchTests
{
    private delegate void TypedSenderHandler(Border sender, RoutedEventArgs e);

    private delegate void RefSenderHandler(ref object sender, RoutedEventArgs e);

    [Test]
    public void BubbleRunsLeafToRoot_TunnelRootToLeaf_DirectStaysPut()
    {
        var bubble = Register(RoutingStrategy.Bubble);
        var tunnel = Register(RoutingStrategy.Tunnel);
        var direct = Register(RoutingStrategy.Direct);
        var (root, middle, leaf) = Chain();
        List<object> order = [];
        Border[] all = [root, middle, leaf];
        foreach (var element in all)
        {
            element.AddHandler(bubble, (s, e) => order.Add(s));
            element.AddHandler(tunnel, (s, e) => order.Add(s));
            element.AddHandler(direct, (s, e) => order.Add(s));
        }

        leaf.RaiseEvent(new RoutedEventArgs(bubble));
        Assert.That(order, Is.EqualTo(new object[] { leaf, middle, root }));

        order.Clear();
        leaf.RaiseEvent(new RoutedEventArgs(tunnel));
        Assert.That(order, Is.EqualTo(new object[] { root, middle, leaf }));

        order.Clear();
        middle.RaiseEvent(new RoutedEventArgs(direct));
        Assert.That(order, Is.EqualTo(new object[] { middle }));
    }

    [Test]
    public void ClassHandlersRunFirst_AndAHandledEventReachesOnlyThoseAskingForIt()
    {
        var routedEvent = Register(RoutingStrategy.Bubble);
        var element = new Border();
        List<string> heard = [];
        routedEvent.RegisterClassHandler<Border>(new RoutedEventHandler((s, e) =>
        {
            heard.Add("class");
            e.Handled = true;
        }));
        routedEvent.RegisterClassHandler<Border>(new RoutedEventHandler((s, e) => heard.Add("class, skipped")));
        routedEvent.RegisterClassHandler<Border>(new RoutedEventHandler((s, e) => heard.Add("class, handled too")), true);
        element.AddHandler(routedEvent, (s, e) => heard.Add("own, skipped"));
        element.AddHandler(routedEvent, (s, e) => heard.Add("own, handled too"), handledEventsToo: true);

        element.RaiseEvent(new RoutedEventArgs(routedEvent));

        Assert.That(heard, Is.EqualTo(new[] { "class", "class, handled too", "own, handled too" }));
    }

    [Test]
    public void AHandlerAddedOrRemovedByItsElementWhileRaising_TakesEffectOnTheNextRaise()
    {
        var routedEvent = Register(RoutingStrategy.Bubble);
        var element = new Border();
        var late = 0;
        var removed = 0;
        RoutedEventHandler lateHandler = (s, e) => late++;
        RoutedEventHandler removedHandler = (s, e) => removed++;
        element.AddHandler(routedEvent, (s, e) =>
        {
            element.AddHandler(routedEvent, lateHandler);
            element.RemoveHandler(routedEvent, removedHandler);
        });
        element.AddHandler(routedEvent, removedHandler);

        element.RaiseEvent(new RoutedEventArgs(routedEvent));
        Assert.Multiple(() =>
        {
            Assert.That(late, Is.EqualTo(0), "added during the raise");
            Assert.That(removed, Is.EqualTo(1), "removed during the raise");
        });

        element.RaiseEvent(new RoutedEventArgs(routedEvent));
        Assert.Multiple(() =>
        {
            Assert.That(late, Is.EqualTo(1));
            Assert.That(removed, Is.EqualTo(1));
        });
    }

    [Test]
    public void AHandlerAddedOrRemovedFurtherUpTheRoute_TakesEffectInThisRaise()
    {
        var routedEvent = Register(RoutingStrategy.Bubble);
        var (root, _, leaf) = Chain();
        var added = 0;
        var removed = 0;
        RoutedEventHandler addedHandler = (s, e) => added++;
        RoutedEventHandler removedHandler = (s, e) => removed++;
        root.AddHandler(routedEvent, removedHandler);
        leaf.AddHandler(routedEvent, (s, e) =>
        {
            root.AddHandler(routedEvent, addedHandler);
            root.RemoveHandler(routedEvent, removedHandler);
        });

        leaf.RaiseEvent(new RoutedEventArgs(routedEvent));

        Assert.Multiple(() =>
        {
            Assert.That(added, Is.EqualTo(1));
            Assert.That(removed, Is.EqualTo(0));
        });
    }

    [Test]
    public void RemoveHandler_RemovesEverySubscriptionOfIt()
    {
        var routedEvent = Register(RoutingStrategy.Bubble);
        var element = new Border();
        var calls = 0;
        RoutedEventHandler handler = (s, e) => calls++;
        element.AddHandler(routedEvent, handler);
        element.AddHandler(routedEvent, handler);

        element.RaiseEvent(new RoutedEventArgs(routedEvent));
        Assert.That(calls, Is.EqualTo(2));

        element.RemoveHandler(routedEvent, handler);
        element.RaiseEvent(new RoutedEventArgs(routedEvent));
        Assert.That(calls, Is.EqualTo(2));
    }

    [Test]
    public void BubbleFollowsTheParentAsItIsWhenItGetsThere()
    {
        var routedEvent = Register(RoutingStrategy.Bubble);
        var (root, middle, leaf) = Chain();
        var other = new Border();
        List<object> reached = [];
        leaf.AddHandler(routedEvent, (s, e) =>
        {
            root.Child = null;
            other.Child = middle;
        });
        root.AddHandler(routedEvent, (s, e) => reached.Add(s));
        other.AddHandler(routedEvent, (s, e) => reached.Add(s));

        leaf.RaiseEvent(new RoutedEventArgs(routedEvent));

        Assert.That(reached, Is.EqualTo(new object[] { other }));
    }

    [Test]
    public void TunnelWalksTheRouteAsItWasWhenRaised()
    {
        var routedEvent = Register(RoutingStrategy.Tunnel);
        var (root, middle, leaf) = Chain();
        var other = new Border();
        List<object> reached = [];
        root.AddHandler(routedEvent, (s, e) =>
        {
            reached.Add(s);
            middle.Child = null;
            other.Child = leaf;
        });
        middle.AddHandler(routedEvent, (s, e) => reached.Add(s));
        leaf.AddHandler(routedEvent, (s, e) => reached.Add(s));

        leaf.RaiseEvent(new RoutedEventArgs(routedEvent));

        Assert.That(reached, Is.EqualTo(new object[] { root, middle, leaf }));
    }

    [Test]
    public void HandlersOfEveryShape_GetTheirOwnArgs()
    {
        var sized = Register(RoutingStrategy.Bubble, typeof(SizeChangedEventHandler));
        var plain = Register(RoutingStrategy.Bubble);
        var element = new Border();
        SizeChangedEventArgs gotSized = null;
        Border gotSender = null;
        object gotByRef = null;
        var multicast = 0;
        element.AddHandler(sized, new SizeChangedEventHandler((s, e) => gotSized = e));
        element.AddHandler(plain, new TypedSenderHandler((s, e) => gotSender = s));
        element.AddHandler(plain, new RefSenderHandler((ref object s, RoutedEventArgs e) => gotByRef = s));
        element.AddHandler(plain, (RoutedEventHandler)((s, e) => multicast++) + ((s, e) => multicast++));

        var args = new SizeChangedEventArgs(default, default, true, true) { RoutedEvent = sized };
        element.RaiseEvent(args);
        element.RaiseEvent(new RoutedEventArgs(plain));

        Assert.Multiple(() =>
        {
            Assert.That(gotSized, Is.SameAs(args));
            Assert.That(gotSender, Is.SameAs(element));
            Assert.That(gotByRef, Is.SameAs(element));
            Assert.That(multicast, Is.EqualTo(2), "every delegate of a multicast handler");
        });
    }

    [Test]
    public void AnExceptionAHandlerThrows_ComesOutAsItIs_WhateverItsShape()
    {
        var plain = Register(RoutingStrategy.Bubble);
        var byRef = Register(RoutingStrategy.Bubble);
        var element = new Border();
        element.AddHandler(plain, (s, e) => throw new InvalidOperationException("typed"));
        element.AddHandler(byRef, new RefSenderHandler((ref object s, RoutedEventArgs e) =>
            throw new InvalidOperationException("dynamic")));

        Assert.Multiple(() =>
        {
            Assert.That(() => element.RaiseEvent(new RoutedEventArgs(plain)),
                Throws.InvalidOperationException.With.Message.EqualTo("typed"));
            Assert.That(() => element.RaiseEvent(new RoutedEventArgs(byRef)),
                Throws.InvalidOperationException.With.Message.EqualTo("dynamic"));
        });
    }

    [Test]
    public void AClassHandler_RunsOnItsTypeOnly_AndOneRegisteredLaterIsSeen()
    {
        var routedEvent = Register(RoutingStrategy.Bubble);
        var border = new Border();
        _ = new Viewbox { Child = border };
        List<string> heard = [];

        routedEvent.RegisterClassHandler<Border>(new RoutedEventHandler((s, e) => heard.Add("border:" + s.GetType().Name)));
        border.RaiseEvent(new RoutedEventArgs(routedEvent));
        Assert.That(heard, Is.EqualTo(new[] { "border:Border" }));

        heard.Clear();
        routedEvent.RegisterClassHandler<Decorator>(new RoutedEventHandler((s, e) => heard.Add("decorator:" + s.GetType().Name)));
        border.RaiseEvent(new RoutedEventArgs(routedEvent));
        Assert.That(heard, Is.EqualTo(new[] { "border:Border", "decorator:Border", "decorator:Viewbox" }));
    }

    [Test]
    public void AnObserver_IsToldTheRaise_EveryHandlerRunOrSkipped_AndTheEnd()
    {
        var routedEvent = Register(RoutingStrategy.Bubble);
        var (root, middle, leaf) = Chain();
        RoutedEventHandler handles = (s, e) => e.Handled = true;
        RoutedEventHandler skipped = (s, e) => { };
        RoutedEventHandler late = (s, e) => { };
        RoutedEventHandler byClass = (s, e) => { };
        routedEvent.RegisterClassHandler<Viewbox>(byClass);
        leaf.AddHandler(routedEvent, handles);
        middle.AddHandler(routedEvent, skipped);
        root.AddHandler(routedEvent, late, handledEventsToo: true);
        var box = new Viewbox { Child = root };
        var observer = new RecordingObserver();
        routedEvent.AddObserver(observer);
        try
        {
            leaf.RaiseEvent(new RoutedEventArgs(routedEvent));
        }
        finally
        {
            routedEvent.RemoveObserver(observer);
        }

        Assert.Multiple(() =>
        {
            Assert.That(observer.Log, Is.EqualTo(new[] { "raised", "handler", "handler", "handler", "handler", "finished" }));
            Assert.That(observer.Calls[0].Element, Is.SameAs(leaf));
            Assert.That(observer.Calls[0].Handler, Is.SameAs(handles));
            Assert.That(observer.Calls[0].WasHandled, Is.False);
            Assert.That(observer.Calls[0].IsHandled, Is.True, "the handler handled it");
            Assert.That(observer.Calls[1].Handler, Is.SameAs(skipped));
            Assert.That(observer.Calls[1].Skipped, Is.True);
            Assert.That(observer.Calls[2].Element, Is.SameAs(root));
            Assert.That(observer.Calls[2].HandledEventsToo, Is.True);
            Assert.That(observer.Calls[2].WasHandled, Is.True);
            Assert.That(observer.Calls[2].Skipped, Is.False);
            Assert.That(observer.Calls[3].Element, Is.SameAs(box));
            Assert.That(observer.Calls[3].IsClassHandler, Is.True);
            Assert.That(observer.Calls[3].Skipped, Is.True, "a class handler not asking for handled events");
        });
    }

    [Test]
    public void AnObserver_SeesARaiseNobodyHears_AndTheHandlerThatThrew()
    {
        var routedEvent = Register(RoutingStrategy.Bubble);
        var quiet = new Border();
        var observer = new RecordingObserver();
        routedEvent.AddObserver(observer);
        routedEvent.AddObserver(observer);
        try
        {
            quiet.RaiseEvent(new RoutedEventArgs(routedEvent));
            Assert.That(observer.Log, Is.EqualTo(new[] { "raised", "finished" }), "added twice, told once");

            observer.Log.Clear();
            var throwing = new Border();
            var thrown = new InvalidOperationException();
            throwing.AddHandler(routedEvent, (s, e) => throw thrown);
            Assert.That(() => throwing.RaiseEvent(new RoutedEventArgs(routedEvent)), Throws.InvalidOperationException);
            Assert.That(observer.Log, Is.EqualTo(new[] { "raised", "handler", "finished" }));
            Assert.That(observer.Calls.Single().Exception, Is.SameAs(thrown));
        }
        finally
        {
            routedEvent.RemoveObserver(observer);
        }

        observer.Log.Clear();
        quiet.RaiseEvent(new RoutedEventArgs(routedEvent));
        Assert.That(observer.Log, Is.Empty, "removed");
    }

    [Test]
    public void AnObserverThatThrows_DoesNotBreakTheRaise()
    {
        var routedEvent = Register(RoutingStrategy.Bubble);
        var element = new Border();
        var ran = 0;
        element.AddHandler(routedEvent, (s, e) => ran++);
        var observer = new ThrowingObserver();
        routedEvent.AddObserver(observer);
        try
        {
            Assert.That(() => element.RaiseEvent(new RoutedEventArgs(routedEvent)), Throws.Nothing);
        }
        finally
        {
            routedEvent.RemoveObserver(observer);
        }

        Assert.That(ran, Is.EqualTo(1));
    }

    [Test]
    public void EveryRegisteredEventIsListed_AndANewOneIsAnnounced_EvenPastASubscriberThatThrows()
    {
        var listed = Register(RoutingStrategy.Bubble);
        Assert.That(EventManager.GetRoutedEvents(), Does.Contain(listed));

        var announced = new System.Collections.Concurrent.ConcurrentQueue<RoutedEvent>();
        Action<RoutedEvent> throwing = e => throw new InvalidOperationException();
        Action<RoutedEvent> onRegistered = announced.Enqueue;
        EventManager.RoutedEventRegistered += throwing;
        EventManager.RoutedEventRegistered += onRegistered;
        try
        {
            var registered = Register(RoutingStrategy.Direct);
            Assert.That(announced, Does.Contain(registered));
        }
        finally
        {
            EventManager.RoutedEventRegistered -= throwing;
            EventManager.RoutedEventRegistered -= onRegistered;
        }
    }

    private static RoutedEvent Register(RoutingStrategy strategy, Type handlerType = null)
    {
        return EventManager.RegisterRoutedEvent("Dispatch" + Guid.NewGuid().ToString("N"), strategy,
            handlerType ?? typeof(RoutedEventHandler), typeof(RoutedEventDispatchTests));
    }

    private static (Border Root, Border Middle, Border Leaf) Chain()
    {
        var leaf = new Border();
        var middle = new Border { Child = leaf };
        var root = new Border { Child = middle };
        return (root, middle, leaf);
    }

    private sealed class RecordingObserver : IRoutedEventObserver
    {
        public List<string> Log { get; } = [];
        public List<RoutedEventHandlerCall> Calls { get; } = [];

        public void OnRaised(IObservableComponent source, RoutedEventArgs e) => Log.Add("raised");

        public void OnHandlerInvoked(in RoutedEventHandlerCall call)
        {
            Log.Add("handler");
            Calls.Add(call);
        }

        public void OnRouteFinished(IObservableComponent source, RoutedEventArgs e) => Log.Add("finished");
    }

    private sealed class ThrowingObserver : IRoutedEventObserver
    {
        public void OnRaised(IObservableComponent source, RoutedEventArgs e) => throw new InvalidOperationException();

        public void OnHandlerInvoked(in RoutedEventHandlerCall call) => throw new InvalidOperationException();

        public void OnRouteFinished(IObservableComponent source, RoutedEventArgs e) => throw new InvalidOperationException();
    }
}
